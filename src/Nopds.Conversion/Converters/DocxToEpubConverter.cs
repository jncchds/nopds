using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Nopds.Conversion.Epub;
using Nopds.Formats;
using Nopds.Formats.Parsers;

namespace Nopds.Conversion.Converters;

/// <summary>
/// Word (DOCX / OOXML) → EPUB. Maps heading styles and outline levels to chapters, keeps bold/italic/
/// underline/strike/super/subscript, hyperlinks and bookmarks, lists, tables, embedded raster images,
/// and footnotes/endnotes as popup notes.
/// </summary>
public sealed class DocxToEpubConverter : IBookConverter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public IReadOnlyCollection<string> Sources { get; } = ["docx"];

    public string Target => "epub";

    private sealed class Context(ZipArchive zip, EpubDocument doc)
    {
        public ZipArchive Zip { get; } = zip;
        public EpubDocument Doc { get; } = doc;
        public Dictionary<string, (string Target, bool External)> Rels { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> HeadingStyles { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OrderedLists { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, XElement> Footnotes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, XElement> Endnotes { get; } = new(StringComparer.Ordinal);
        public List<XElement> Notes { get; } = [];
    }

    public void Convert(byte[] input, BookMetadata meta, Stream output)
    {
        using var zip = new ZipArchive(new MemoryStream(input), ZipArchiveMode.Read);
        var embedded = DocxParser.ReadCore(zip);
        var doc = new EpubDocument { Meta = MetadataMerge.Combine(meta, embedded) };
        var ctx = new Context(zip, doc);

        var main = Load(zip, "word/document.xml") ?? throw new FormatException("DOCX without word/document.xml.");
        LoadRels(ctx, "word/_rels/document.xml.rels");
        LoadStyles(ctx);
        LoadNumbering(ctx);
        LoadNotes(ctx, "word/footnotes.xml", W + "footnote", ctx.Footnotes);
        LoadNotes(ctx, "word/endnotes.xml", W + "endnote", ctx.Endnotes);

        var body = main.Root?.Element(W + "body");
        var blocks = body is null ? [] : Blocks(body.Elements(), ctx).ToList();
        ChapterBuilder.Build(doc, blocks, ctx.Notes);
        EpubWriter.Write(doc, output);
    }

    internal static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        using var s = entry.Open();
        using var reader = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static void LoadRels(Context ctx, string path)
    {
        foreach (var r in Load(ctx.Zip, path)?.Root?.Elements(Rel + "Relationship") ?? [])
        {
            if ((string?)r.Attribute("Id") is { } id && (string?)r.Attribute("Target") is { } target)
            {
                ctx.Rels[id] = (target, (string?)r.Attribute("TargetMode") == "External");
            }
        }
    }

    private static void LoadStyles(Context ctx)
    {
        foreach (var s in Load(ctx.Zip, "word/styles.xml")?.Root?.Elements(W + "style") ?? [])
        {
            var id = (string?)s.Attribute(W + "styleId");
            if (id is null || (string?)s.Attribute(W + "type") != "paragraph")
            {
                continue;
            }

            var name = ((string?)s.Element(W + "name")?.Attribute(W + "val") ?? "").ToLowerInvariant();
            var outline = (int?)s.Element(W + "pPr")?.Element(W + "outlineLvl")?.Attribute(W + "val");
            if (name == "title")
            {
                ctx.HeadingStyles[id] = 1;
            }
            else if (name.StartsWith("heading ", StringComparison.Ordinal) && int.TryParse(name[8..], out var level))
            {
                ctx.HeadingStyles[id] = level;
            }
            else if (outline is >= 0 and < 6)
            {
                ctx.HeadingStyles[id] = outline.Value + 1;
            }
        }
    }

    /// <summary>Lists whose level-0 number format is not a bullet are rendered as ordered lists.</summary>
    private static void LoadNumbering(Context ctx)
    {
        var root = Load(ctx.Zip, "word/numbering.xml")?.Root;
        if (root is null)
        {
            return;
        }

        var ordered = root.Elements(W + "abstractNum")
            .Where(a => (string?)a.Elements(W + "lvl").FirstOrDefault()?.Element(W + "numFmt")?.Attribute(W + "val") is { } fmt && fmt != "bullet" && fmt != "none")
            .Select(a => (string?)a.Attribute(W + "abstractNumId"))
            .ToHashSet();
        foreach (var num in root.Elements(W + "num"))
        {
            if ((string?)num.Attribute(W + "numId") is { } id && ordered.Contains((string?)num.Element(W + "abstractNumId")?.Attribute(W + "val")))
            {
                ctx.OrderedLists.Add(id);
            }
        }
    }

    private static void LoadNotes(Context ctx, string path, XName name, Dictionary<string, XElement> target)
    {
        foreach (var n in Load(ctx.Zip, path)?.Root?.Elements(name) ?? [])
        {
            // Types "separator" / "continuationSeparator" are layout artifacts, not notes.
            if ((string?)n.Attribute(W + "id") is { } id && n.Attribute(W + "type") is null)
            {
                target[id] = n;
            }
        }
    }

    private static IEnumerable<XElement> Blocks(IEnumerable<XElement> elements, Context ctx)
    {
        XElement? list = null;
        string? listId = null;
        foreach (var el in Unwrap(elements))
        {
            if (el.Name == W + "p")
            {
                var numId = (string?)el.Element(W + "pPr")?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val");
                if (numId is not null && numId != "0" && HeadingLevel(el, ctx) == 0)
                {
                    if (list is null || listId != numId)
                    {
                        if (list is not null)
                        {
                            yield return list;
                        }

                        list = Xhtml.El(ctx.OrderedLists.Contains(numId) ? "ol" : "ul");
                        listId = numId;
                    }

                    list.Add(Xhtml.El("li", Inlines(el.Elements(), ctx)));
                    continue;
                }

                if (list is not null)
                {
                    yield return list;
                    list = null;
                }

                yield return Paragraph(el, ctx);
            }
            else
            {
                if (list is not null)
                {
                    yield return list;
                    list = null;
                }

                if (el.Name == W + "tbl")
                {
                    yield return Table(el, ctx);
                }
            }
        }

        if (list is not null)
        {
            yield return list;
        }
    }

    /// <summary>Content controls, tracked insertions and compatibility wrappers only hold regular content.</summary>
    private static IEnumerable<XElement> Unwrap(IEnumerable<XElement> elements)
    {
        foreach (var el in elements)
        {
            if (el.Name == W + "sdt")
            {
                foreach (var c in Unwrap(el.Element(W + "sdtContent")?.Elements() ?? []))
                {
                    yield return c;
                }
            }
            else if (el.Name == W + "customXml" || el.Name == W + "ins" || el.Name == W + "smartTag" || el.Name == W + "fldSimple")
            {
                foreach (var c in Unwrap(el.Elements()))
                {
                    yield return c;
                }
            }
            else if (el.Name == Mc + "AlternateContent")
            {
                foreach (var c in Unwrap(el.Elements(Mc + "Choice").FirstOrDefault()?.Elements() ?? el.Element(Mc + "Fallback")?.Elements() ?? []))
                {
                    yield return c;
                }
            }
            else if (el.Name != W + "del" && el.Name != W + "moveFrom")
            {
                yield return el;
            }
        }
    }

    private static int HeadingLevel(XElement p, Context ctx)
    {
        var pPr = p.Element(W + "pPr");
        var style = (string?)pPr?.Element(W + "pStyle")?.Attribute(W + "val");
        if (style is not null && ctx.HeadingStyles.TryGetValue(style, out var level))
        {
            return level;
        }

        var outline = (int?)pPr?.Element(W + "outlineLvl")?.Attribute(W + "val");
        return outline is >= 0 and < 6 ? outline.Value + 1 : 0;
    }

    private static XElement Paragraph(XElement p, Context ctx)
    {
        var level = HeadingLevel(p, ctx);
        var content = Inlines(p.Elements(), ctx).ToList();
        var x = level > 0 ? Xhtml.Heading(level, content) : Xhtml.El("p", content);
        if (level == 0 && Xhtml.IsEmpty(x))
        {
            return Xhtml.El("p", new XAttribute("class", "empty-line"), " ");
        }

        var jc = (string?)p.Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val");
        if (level == 0 && jc is "center" or "right" or "end")
        {
            x.SetAttributeValue("class", jc == "center" ? "center" : "right");
        }

        return x;
    }

    private static XElement Table(XElement tbl, Context ctx)
    {
        var table = Xhtml.El("table");
        foreach (var tr in Unwrap(tbl.Elements()).Where(e => e.Name == W + "tr"))
        {
            var row = Xhtml.El("tr");
            foreach (var tc in Unwrap(tr.Elements()).Where(e => e.Name == W + "tc"))
            {
                var props = tc.Element(W + "tcPr");
                if ((string?)props?.Element(W + "vMerge")?.Attribute(W + "val") is null && props?.Element(W + "vMerge") is not null)
                {
                    continue; // Continuation of a vertically merged cell.
                }

                var cell = Xhtml.El("td", Blocks(tc.Elements(), ctx));
                if ((int?)props?.Element(W + "gridSpan")?.Attribute(W + "val") is > 1 and var span)
                {
                    cell.SetAttributeValue("colspan", span);
                }

                row.Add(cell);
            }

            table.Add(row);
        }

        return table;
    }

    private static IEnumerable<XNode> Inlines(IEnumerable<XElement> elements, Context ctx)
    {
        foreach (var el in Unwrap(elements))
        {
            var name = el.Name;
            if (name == W + "r")
            {
                foreach (var n in Run(el, ctx))
                {
                    yield return n;
                }
            }
            else if (name == W + "hyperlink")
            {
                var a = Xhtml.El("a", Inlines(el.Elements(), ctx));
                if ((string?)el.Attribute(R + "id") is { } rid && ctx.Rels.TryGetValue(rid, out var rel) && rel.External)
                {
                    a.SetAttributeValue("href", rel.Target);
                }
                else if ((string?)el.Attribute(W + "anchor") is { } anchor)
                {
                    a.SetAttributeValue("href", "#" + Xhtml.SafeId(anchor));
                }

                yield return a;
            }
            else if (name == W + "bookmarkStart" && (string?)el.Attribute(W + "name") is { } bm && !bm.StartsWith("_Go", StringComparison.Ordinal))
            {
                yield return Xhtml.El("span", new XAttribute("id", Xhtml.SafeId(bm)));
            }
        }
    }

    private static IEnumerable<XNode> Run(XElement r, Context ctx)
    {
        var nodes = new List<XNode>();
        foreach (var c in r.Elements())
        {
            var n = c.Name.LocalName;
            if (c.Name == W + "t")
            {
                nodes.Add(new XText(Xhtml.CleanText(c.Value)));
            }
            else if (n == "tab" || n == "ptab")
            {
                nodes.Add(new XText(" "));
            }
            else if (n == "br" || n == "cr")
            {
                if ((string?)c.Attribute(W + "type") is null or "textWrapping")
                {
                    nodes.Add(Xhtml.El("br"));
                }
            }
            else if (n == "noBreakHyphen")
            {
                nodes.Add(new XText("\u2011"));
            }
            else if (n == "footnoteReference" || n == "endnoteReference")
            {
                var id = (string?)c.Attribute(W + "id");
                var map = n == "footnoteReference" ? ctx.Footnotes : ctx.Endnotes;
                if (id is not null && map.TryGetValue(id, out var note))
                {
                    var noteId = (n == "footnoteReference" ? "fn" : "en") + id;
                    var label = (ctx.Notes.Count + 1).ToString();
                    ctx.Notes.Add(Xhtml.Note(noteId, label, Blocks(note.Elements(), ctx).Where(b => !Xhtml.IsEmpty(b))));
                    nodes.Add(Xhtml.NoteRef(noteId, label));
                }
            }
            else if (n == "drawing" || n == "pict" || c.Name == Mc + "AlternateContent" || n == "object")
            {
                foreach (var img in Images(c, ctx))
                {
                    nodes.Add(img);
                }
            }
        }

        var props = r.Element(W + "rPr");
        if (props is null || nodes.Count == 0)
        {
            return nodes;
        }

        IEnumerable<XNode> result = nodes;
        var vert = (string?)props.Element(W + "vertAlign")?.Attribute(W + "val");
        if (vert == "superscript")
        {
            result = [Xhtml.El("sup", result)];
        }
        else if (vert == "subscript")
        {
            result = [Xhtml.El("sub", result)];
        }

        if (On(props.Element(W + "strike")) || On(props.Element(W + "dstrike")))
        {
            result = [Xhtml.El("s", result)];
        }

        if (props.Element(W + "u") is { } u && (string?)u.Attribute(W + "val") != "none")
        {
            result = [Xhtml.El("u", result)];
        }

        if (On(props.Element(W + "i")))
        {
            result = [Xhtml.El("em", result)];
        }

        if (On(props.Element(W + "b")))
        {
            result = [Xhtml.El("strong", result)];
        }

        return result;
    }

    /// <summary>Toggle properties: present without a value, or with a truthy one.</summary>
    private static bool On(XElement? e) =>
        e is not null && (string?)e.Attribute(W + "val") is null or "1" or "true" or "on";

    private static IEnumerable<XElement> Images(XElement container, Context ctx)
    {
        var ids = container.Descendants(A + "blip").Select(b => (string?)b.Attribute(R + "embed"))
            .Concat(container.Descendants(V + "imagedata").Select(i => (string?)i.Attribute(R + "id")))
            .Where(id => id is not null)
            .Distinct();
        foreach (var id in ids)
        {
            if (!ctx.Rels.TryGetValue(id!, out var rel) || rel.External)
            {
                continue;
            }

            var path = EpubParser.ResolvePath("word/document.xml", rel.Target);
            var entry = ctx.Zip.GetEntry(path);
            if (entry is null)
            {
                continue;
            }

            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            if (ctx.Doc.AddImage(path, ms.ToArray()) is { } href)
            {
                var alt = (string?)container.Descendants().FirstOrDefault(d => d.Name.LocalName == "docPr")?.Attribute("descr") ?? "";
                yield return Xhtml.El("img", new XAttribute("src", href), new XAttribute("alt", alt));
            }
        }
    }
}
