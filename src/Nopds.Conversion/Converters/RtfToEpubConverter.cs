using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nopds.Conversion.Epub;
using Nopds.Formats;
using Nopds.Formats.Parsers;
using Nopds.Formats.Rtf;

namespace Nopds.Conversion.Converters;

/// <summary>
/// RTF → EPUB. Interprets the document body with per-font code pages and \u characters, keeps bold/
/// italic/underline/strike/super/subscript, headings (outline levels or "heading N" styles), alignment,
/// tables, hyperlinks, bookmarks, PNG/JPEG pictures and footnotes.
/// </summary>
public sealed partial class RtfToEpubConverter : IBookConverter
{
    public IReadOnlyCollection<string> Sources { get; } = ["rtf"];

    public string Target => "epub";

    public void Convert(byte[] input, BookMetadata meta, Stream output)
    {
        var embedded = RtfParser.ReadInfo(input);
        var doc = new EpubDocument { Meta = MetadataMerge.Combine(meta, embedded) };
        var interpreter = new Interpreter(input, doc);
        interpreter.Run();
        ChapterBuilder.Build(doc, interpreter.Blocks, interpreter.Notes);
        EpubWriter.Write(doc, output);
    }

    private enum Dest
    {
        Body,
        Skip,
        FontTable,
        StyleSheet,
        Picture,
        FieldInstruction,
        Bookmark,
    }

    private sealed record State
    {
        public Dest Dest { get; set; } = Dest.Body;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strike { get; set; }
        public int VAlign { get; set; }
        public bool Hidden { get; set; }
        public string? Link { get; set; }
        public int Uc { get; set; } = 1;
        public int Font { get; set; } = -1;

        // Paragraph properties.
        public bool InTable { get; set; }
        public int Outline { get; set; } = -1;
        public int Style { get; set; } = -1;
        public string? Align { get; set; }

        public void PlainChar()
        {
            Bold = Italic = Underline = Strike = Hidden = false;
            VAlign = 0;
        }

        public void PlainPara()
        {
            InTable = false;
            Outline = Style = -1;
            Align = null;
        }
    }

    /// <summary>Where text currently goes: the body, or a footnote being read.</summary>
    private sealed class Sink
    {
        public List<XElement> Blocks { get; } = [];
        public List<XNode> Inline { get; } = [];
        public List<XElement> Cell { get; } = [];
        public List<XElement> Row { get; } = [];
        public List<XElement> Rows { get; } = [];
    }

    private sealed class Interpreter(byte[] data, EpubDocument doc)
    {
        private readonly Stack<State> _stack = new();
        private readonly Stack<Sink> _sinks = new();
        private readonly Dictionary<int, int> _fontCodePages = [];
        private readonly Dictionary<int, int> _styleLevels = [];
        private State _s = new();
        private Sink _sink = new();
        private RtfText _text = new(RtfText.CodePage(1252));
        private int _defaultCodePage = 1252;
        private int _defaultFont = -1;
        private State? _runState;
        private int _skipChars;
        private bool _pendingStar;

        // Nested destinations that collect data until their group closes.
        private int _footnoteDepth = -1;
        private readonly Stack<int> _footnoteDepths = new();
        private int _fontEntry = -1;
        private int _styleEntry = -1;
        private int _styleOutline = -1;
        private readonly StringBuilder _hex = new();
        private readonly List<byte> _binary = [];
        private string? _pictType;
        private readonly StringBuilder _instruction = new();
        private string? _fieldLink;

        public List<XElement> Blocks => _sink.Blocks;
        public List<XElement> Notes { get; } = [];

        public void Run()
        {
            foreach (var t in RtfTokenizer.Tokenize(data))
            {
                switch (t.Type)
                {
                    case RtfTokenType.GroupStart:
                        _stack.Push(_s);
                        _s = _s with { };
                        _skipChars = 0;
                        break;
                    case RtfTokenType.GroupEnd:
                        EndGroup();
                        break;
                    case RtfTokenType.Word:
                        var star = _pendingStar;
                        _pendingStar = false;
                        _skipChars = 0;
                        Word(t.Word!, t.Param, star);
                        break;
                    case RtfTokenType.Symbol:
                        Symbol(t.Symbol);
                        break;
                    case RtfTokenType.Hex:
                        if (_skipChars > 0)
                        {
                            _skipChars--;
                        }
                        else
                        {
                            Byte((byte)t.Param!.Value);
                        }

                        break;
                    case RtfTokenType.Text:
                        var span = data.AsSpan(t.Start, t.Length);
                        var skip = Math.Min(_skipChars, span.Length);
                        _skipChars -= skip;
                        Bytes(span[skip..]);
                        break;
                    case RtfTokenType.Binary:
                        if (_s.Dest == Dest.Picture)
                        {
                            _binary.AddRange(data.AsSpan(t.Start, t.Length).ToArray());
                        }

                        break;
                }
            }

            while (_sinks.Count > 0)
            {
                EndFootnote();
            }

            EndParagraph();
            FlushTable();
        }

        private void EndGroup()
        {
            var closing = _s;
            var depth = _stack.Count;
            switch (closing.Dest)
            {
                case Dest.StyleSheet:
                    // Each style entry is a group ending in its name ("heading 1;").
                    var name = _text.Take();
                    if (_styleEntry >= 0)
                    {
                        var level = _styleOutline >= 0 ? _styleOutline + 1 : HeadingStyle(name);
                        if (level > 0)
                        {
                            _styleLevels[_styleEntry] = level;
                        }
                    }

                    _styleEntry = -1;
                    _styleOutline = -1;
                    break;
                case Dest.Picture when _stack.Count > 0 && _stack.Peek().Dest != Dest.Picture:
                    EndPicture();
                    break;
                case Dest.FieldInstruction when _stack.Count > 0 && _stack.Peek().Dest != Dest.FieldInstruction:
                    var m = HyperlinkField().Match(_instruction.ToString());
                    _fieldLink = !m.Success ? null
                        : m.Groups[1].Success ? "#" + Xhtml.SafeId(m.Groups[2].Value)
                        : ExternalLink().IsMatch(m.Groups[2].Value) ? m.Groups[2].Value : null;
                    _instruction.Clear();
                    break;
                case Dest.Bookmark when _stack.Count > 0 && _stack.Peek().Dest != Dest.Bookmark:
                    var id = _text.Take().Trim();
                    _text = new RtfText(_text.Encoding);
                    if (id.Length > 0 && !id.StartsWith("_Go", StringComparison.Ordinal))
                    {
                        FlushRun();
                        _sink.Inline.Add(Xhtml.El("span", new XAttribute("id", Xhtml.SafeId(id))));
                    }

                    break;
            }

            if (depth == _footnoteDepth)
            {
                EndFootnote();
            }

            if (_stack.Count == 0)
            {
                return;
            }

            if (closing.Dest == Dest.Body)
            {
                FlushRun();
            }

            _s = _stack.Pop();
            _skipChars = 0;
            UpdateEncoding();
        }

        private void Word(string word, int? param, bool star)
        {
            if (_s.Dest == Dest.Skip)
            {
                return;
            }

            if (_s.Dest == Dest.FontTable)
            {
                switch (word)
                {
                    case "f":
                        _fontEntry = param ?? -1;
                        break;
                    case "fcharset" when _fontEntry >= 0 && RtfTokenizer.CodePageForCharset(param ?? 0) is { } cp:
                        _fontCodePages[_fontEntry] = cp;
                        break;
                    case "cpg" when _fontEntry >= 0 && param > 0:
                        _fontCodePages[_fontEntry] = param.Value;
                        break;
                    default:
                        if (star || word is "panose" or "fname" or "falt")
                        {
                            _s.Dest = Dest.Skip;
                        }

                        break;
                }

                return;
            }

            if (_s.Dest == Dest.StyleSheet)
            {
                switch (word)
                {
                    case "s":
                        _styleEntry = param ?? 0;
                        break;
                    case "outlinelevel":
                        _styleOutline = param ?? -1;
                        break;
                    case "cs" or "ds" or "ts":
                        _styleEntry = -1;
                        _s.Dest = Dest.Skip;
                        break;
                    default:
                        if (star)
                        {
                            _s.Dest = Dest.Skip;
                        }

                        break;
                }

                return;
            }

            if (_s.Dest == Dest.Picture)
            {
                switch (word)
                {
                    case "pngblip":
                        _pictType = "image/png";
                        break;
                    case "jpegblip":
                        _pictType = "image/jpeg";
                        break;
                    case "emfblip" or "wmetafile" or "macpict" or "pmmetafile" or "dibitmap" or "wbitmap":
                        _pictType = null;
                        break;
                    default:
                        if (star)
                        {
                            _s.Dest = Dest.Skip;
                        }

                        break;
                }

                return;
            }

            if (_s.Dest == Dest.FieldInstruction)
            {
                // Keep switches such as \l (link to a bookmark) for the HYPERLINK pattern.
                _instruction.Append('\\').Append(word).Append(param).Append(' ');
                return;
            }

            if (_s.Dest == Dest.Bookmark)
            {
                return;
            }

            switch (word)
            {
                // Destinations.
                case "fonttbl":
                    _s.Dest = Dest.FontTable;
                    return;
                case "stylesheet":
                    _s.Dest = Dest.StyleSheet;
                    _text = new RtfText(_text.Encoding);
                    return;
                case "pict":
                    _s.Dest = Dest.Picture;
                    _hex.Clear();
                    _binary.Clear();
                    _pictType = null;
                    return;
                case "fldinst":
                    _s.Dest = Dest.FieldInstruction;
                    _instruction.Clear();
                    return;
                case "fldrslt":
                    _s.Link = _fieldLink;
                    _fieldLink = null;
                    return;
                case "bkmkstart":
                    FlushRun();
                    _s.Dest = Dest.Bookmark;
                    _text = new RtfText(_text.Encoding);
                    return;
                case "footnote":
                    StartFootnote();
                    return;
                case "shppict" or "field":
                    return;
                case "colortbl" or "info" or "header" or "headerl" or "headerr" or "headerf" or "footer" or "footerl" or "footerr" or "footerf"
                    or "object" or "nonshppict" or "listtable" or "listoverridetable" or "rsidtbl" or "generator" or "xmlnstbl" or "themedata"
                    or "colorschememapping" or "datastore" or "latentstyles" or "pgdsctbl" or "filetbl" or "revtbl" or "annotation" or "atnid"
                    or "atnauthor" or "comment" or "private" or "userprops" or "docvar" or "pntxta" or "pntxtb" or "sp"
                    or "shpinst" or "fldtype" or "background" or "ftnsep" or "ftnsepc" or "aftnsep" or "aftnsepc" or "mmathPr" or "bkmkend":
                    _s.Dest = Dest.Skip;
                    return;

                // Document and character sets.
                case "ansicpg" when param > 0:
                    _defaultCodePage = param.Value;
                    UpdateEncoding();
                    return;
                case "deff":
                    _defaultFont = param ?? -1;
                    return;
                case "f":
                    _s.Font = param ?? -1;
                    UpdateEncoding();
                    return;
                case "uc":
                    _s.Uc = Math.Max(0, param ?? 1);
                    return;
                case "u" when param is { } u:
                    Char((char)(u < 0 ? u + 65536 : u));
                    _skipChars = _s.Uc;
                    return;

                // Paragraphs.
                case "par":
                    EndParagraph();
                    return;
                case "sect":
                    EndParagraph();
                    return;
                case "pard":
                    _s.PlainPara();
                    return;
                case "intbl":
                    _s.InTable = true;
                    return;
                case "cell" or "nestcell":
                    EndCell();
                    return;
                case "row" or "nestrow":
                    EndRow();
                    return;
                case "outlinelevel":
                    _s.Outline = param ?? -1;
                    return;
                case "s":
                    _s.Style = param ?? -1;
                    return;
                case "qc":
                    _s.Align = "center";
                    return;
                case "qr":
                    _s.Align = "right";
                    return;
                case "ql" or "qj":
                    _s.Align = null;
                    return;
                case "line":
                    FlushRun();
                    _sink.Inline.Add(Xhtml.El("br"));
                    return;
                case "tab":
                    Char(' ');
                    return;

                // Character formatting.
                case "plain":
                    FlushRun();
                    _s.PlainChar();
                    _s.Font = _defaultFont;
                    UpdateEncoding();
                    return;
                case "b":
                    _s.Bold = param is null or not 0;
                    return;
                case "i":
                    _s.Italic = param is null or not 0;
                    return;
                case "ul" or "uld" or "uldb" or "ulw" or "ulwave" or "uldash" or "ulth":
                    _s.Underline = param is null or not 0;
                    return;
                case "ulnone":
                    _s.Underline = false;
                    return;
                case "strike" or "striked":
                    _s.Strike = param is null or not 0;
                    return;
                case "super":
                    _s.VAlign = 1;
                    return;
                case "sub":
                    _s.VAlign = -1;
                    return;
                case "nosupersub":
                    _s.VAlign = 0;
                    return;
                case "v":
                    _s.Hidden = param is null or not 0;
                    return;

                // Special characters.
                case "emdash":
                    Char('—');
                    return;
                case "endash":
                    Char('–');
                    return;
                case "bullet":
                    Char('•');
                    return;
                case "lquote":
                    Char('‘');
                    return;
                case "rquote":
                    Char('’');
                    return;
                case "ldblquote":
                    Char('“');
                    return;
                case "rdblquote":
                    Char('”');
                    return;
                case "emspace" or "enspace" or "qmspace":
                    Char(' ');
                    return;
            }

            if (star)
            {
                _s.Dest = Dest.Skip;
            }
        }

        private void Symbol(char c)
        {
            if (_s.Dest == Dest.Skip)
            {
                return;
            }

            switch (c)
            {
                case '*':
                    _pendingStar = true;
                    break;
                case '\\' or '{' or '}':
                    Char(c);
                    break;
                case '~':
                    Char('\u00A0');
                    break;
                case '_':
                    Char('\u2011');
                    break;
                case '-':
                    // Optional hyphen: invisible unless the line breaks there.
                    break;
                case ':':
                    break;
            }
        }

        private void Bytes(ReadOnlySpan<byte> bytes)
        {
            switch (_s.Dest)
            {
                case Dest.Picture:
                    foreach (var b in bytes)
                    {
                        _hex.Append((char)b);
                    }

                    break;
                case Dest.FieldInstruction:
                    _instruction.Append(Encoding.Latin1.GetString(bytes));
                    break;
                case Dest.StyleSheet or Dest.Bookmark:
                    _text.AddBytes(bytes);
                    break;
                case Dest.Body when !_s.Hidden:
                    StartRun();
                    _text.AddBytes(bytes);
                    break;
            }
        }

        private void Byte(byte b)
        {
            switch (_s.Dest)
            {
                case Dest.StyleSheet or Dest.Bookmark:
                    _text.AddByte(b);
                    break;
                case Dest.Body when !_s.Hidden:
                    StartRun();
                    _text.AddByte(b);
                    break;
            }
        }

        private void Char(char c)
        {
            switch (_s.Dest)
            {
                case Dest.StyleSheet or Dest.Bookmark:
                    _text.AddChar(c);
                    break;
                case Dest.FieldInstruction:
                    _instruction.Append(c);
                    break;
                case Dest.Body when !_s.Hidden:
                    StartRun();
                    _text.AddChar(c);
                    break;
            }
        }

        private void UpdateEncoding()
        {
            var cp = _s.Font >= 0 && _fontCodePages.TryGetValue(_s.Font, out var fontCp) ? fontCp
                : _defaultFont >= 0 && _s.Font < 0 && _fontCodePages.TryGetValue(_defaultFont, out var defCp) ? defCp
                : _defaultCodePage;
            _text.Encoding = RtfText.CodePage(cp);
        }

        /// <summary>Text with the same formatting accumulates in one run; a formatting change starts a new run.</summary>
        private void StartRun()
        {
            if (_runState is not null && !SameFormat(_runState, _s))
            {
                FlushRun();
            }

            _runState ??= _s with { };
        }

        private static bool SameFormat(State a, State b) =>
            a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline && a.Strike == b.Strike && a.VAlign == b.VAlign && a.Link == b.Link;

        private void FlushRun()
        {
            if (_runState is null)
            {
                return;
            }

            var text = Xhtml.CleanText(_text.Take());
            var f = _runState;
            _runState = null;
            if (text.Length == 0)
            {
                return;
            }

            XNode node = new XText(text);
            if (f.VAlign != 0)
            {
                node = Xhtml.El(f.VAlign > 0 ? "sup" : "sub", node);
            }

            if (f.Strike)
            {
                node = Xhtml.El("s", node);
            }

            if (f.Underline && f.Link is null)
            {
                node = Xhtml.El("u", node);
            }

            if (f.Italic)
            {
                node = Xhtml.El("em", node);
            }

            if (f.Bold)
            {
                node = Xhtml.El("strong", node);
            }

            if (f.Link is not null)
            {
                node = Xhtml.El("a", new XAttribute("href", f.Link), node);
            }

            _sink.Inline.Add(node);
        }

        private void EndParagraph()
        {
            FlushRun();
            var level = _s.Outline is >= 0 and < 6 ? _s.Outline + 1 : _styleLevels.GetValueOrDefault(_s.Style);
            var content = _sink.Inline.ToList();
            _sink.Inline.Clear();
            var p = level > 0 ? Xhtml.Heading(level, content) : Xhtml.El("p", content);
            if (level == 0 && _s.Align is { } align)
            {
                p.SetAttributeValue("class", align);
            }

            if (_s.InTable)
            {
                if (!Xhtml.IsEmpty(p))
                {
                    _sink.Cell.Add(p);
                }

                return;
            }

            FlushTable();
            if (level == 0 && Xhtml.IsEmpty(p))
            {
                p = Xhtml.El("p", new XAttribute("class", "empty-line"), " ");
            }

            _sink.Blocks.Add(p);
        }

        private void EndCell()
        {
            FlushRun();
            if (_sink.Inline.Count > 0)
            {
                _sink.Cell.Add(Xhtml.El("p", _sink.Inline.ToList()));
                _sink.Inline.Clear();
            }

            _sink.Row.Add(Xhtml.El("td", _sink.Cell.ToList()));
            _sink.Cell.Clear();
        }

        private void EndRow()
        {
            if (_sink.Inline.Count > 0 || _sink.Cell.Count > 0)
            {
                EndCell();
            }

            if (_sink.Row.Count > 0)
            {
                _sink.Rows.Add(Xhtml.El("tr", _sink.Row.ToList()));
                _sink.Row.Clear();
            }
        }

        private void FlushTable()
        {
            if (_sink.Row.Count > 0 || _sink.Cell.Count > 0)
            {
                EndRow();
            }

            if (_sink.Rows.Count > 0)
            {
                _sink.Blocks.Add(Xhtml.El("table", _sink.Rows.ToList()));
                _sink.Rows.Clear();
            }
        }

        private void StartFootnote()
        {
            FlushRun();
            _footnoteDepths.Push(_footnoteDepth);
            _footnoteDepth = _stack.Count;
            _sinks.Push(_sink);
            _sink = new Sink();

            // The note starts with plain paragraph formatting of its own.
            _s.PlainPara();
        }

        private void EndFootnote()
        {
            FlushRun();
            if (_sink.Inline.Count > 0)
            {
                _sink.Blocks.Add(Xhtml.El("p", _sink.Inline.ToList()));
                _sink.Inline.Clear();
            }

            FlushTable();
            var blocks = _sink.Blocks.Where(b => !Xhtml.IsEmpty(b)).ToList();
            _sink = _sinks.Pop();
            _footnoteDepth = _footnoteDepths.Pop();

            var label = (Notes.Count + 1).ToString();
            var id = "fn" + label;
            Notes.Add(Xhtml.Note(id, label, blocks));
            _sink.Inline.Add(Xhtml.NoteRef(id, label));
        }

        private void EndPicture()
        {
            byte[]? bytes = null;
            if (_pictType is not null)
            {
                if (_binary.Count > 0)
                {
                    bytes = [.. _binary];
                }
                else if (_hex.Length > 1)
                {
                    var hex = new string(_hex.ToString().Where(Uri.IsHexDigit).ToArray());
                    try
                    {
                        bytes = System.Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex[..^1]);
                    }
                    catch (FormatException)
                    {
                    }
                }
            }

            _hex.Clear();
            _binary.Clear();
            if (bytes is { Length: > 0 } && doc.AddImage("pict" + doc.Images.Count, bytes, _pictType) is { } href)
            {
                FlushRun();
                _sink.Inline.Add(Xhtml.El("img", new XAttribute("src", href), new XAttribute("alt", "")));
            }
        }

        private static int HeadingStyle(string name)
        {
            var m = HeadingName().Match(name.Trim().TrimEnd(';'));
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }
    }

    [GeneratedRegex(@"^(?:heading|заголовок|überschrift|nagłówek|titre|título|titolo|kop)\s*(\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingName();

    [GeneratedRegex(@"HYPERLINK\s+(\\l\s+)?""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex HyperlinkField();

    [GeneratedRegex(@"^(?:https?|mailto):", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalLink();
}
