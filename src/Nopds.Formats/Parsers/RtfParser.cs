using Nopds.Formats.Rtf;

namespace Nopds.Formats.Parsers;

/// <summary>RTF: title, author, comments and creation year from the {\info} group.</summary>
public sealed class RtfParser : IBookParser
{
    /// <summary>The info group sits in the header; there is no need to read embedded pictures.</summary>
    private const int HeaderBytes = 256 * 1024;

    public IReadOnlyCollection<string> Formats { get; } = ["rtf"];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover)
    {
        var buffer = new byte[HeaderBytes];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read < 5 || buffer[0] != '{' || buffer[1] != '\\' || buffer[2] != 'r' || buffer[3] != 't' || buffer[4] != 'f')
        {
            throw new FormatException("Not an RTF document.");
        }

        var meta = ReadInfo(buffer, read);
        if (string.IsNullOrWhiteSpace(meta.Title))
        {
            meta.Title = DocumentMeta.TitleFromFileName(fileName);
        }

        return meta;
    }

    public static BookMetadata ReadInfo(byte[] data, int length = -1)
    {
        var meta = new BookMetadata();
        var text = new RtfText(RtfText.CodePage(1252));
        var depth = 0;
        var infoDepth = -1;
        string? field = null;
        var fieldDepth = -1;
        var skipDepth = -1;
        var pendingStar = false;
        var uc = 1;
        var skipChars = 0;
        foreach (var t in RtfTokenizer.Tokenize(data, length))
        {
            switch (t.Type)
            {
                case RtfTokenType.GroupStart:
                    depth++;
                    skipChars = 0;
                    continue;
                case RtfTokenType.GroupEnd:
                    if (depth == fieldDepth)
                    {
                        var value = DocumentMeta.NullIfBlank(text.Take());
                        switch (field)
                        {
                            case "title":
                                meta.Title ??= value;
                                break;
                            case "author":
                                DocumentMeta.AddAuthors(meta, value);
                                break;
                            case "doccomm":
                                meta.Annotation ??= value;
                                break;
                        }

                        field = null;
                        fieldDepth = -1;
                    }

                    if (depth == skipDepth)
                    {
                        skipDepth = -1;
                    }

                    if (depth == infoDepth)
                    {
                        return meta;
                    }

                    depth--;
                    skipChars = 0;
                    continue;
            }

            if (skipDepth >= 0)
            {
                continue;
            }

            if (t.Type == RtfTokenType.Symbol && t.Symbol == '*')
            {
                pendingStar = true;
                continue;
            }

            if (t.Type == RtfTokenType.Word)
            {
                var star = pendingStar;
                pendingStar = false;
                switch (t.Word)
                {
                    case "ansicpg":
                        text.Encoding = RtfText.CodePage(t.Param);
                        break;
                    case "info":
                        infoDepth = depth;
                        break;
                    case "title" or "author" or "doccomm" when infoDepth >= 0:
                        field = t.Word;
                        fieldDepth = depth;
                        break;
                    case "yr" when infoDepth >= 0 && meta.DocDate is null && t.Param is > 1000 and < 3000:
                        meta.DocDate = t.Param.Value.ToString();
                        break;
                    case "uc":
                        uc = Math.Max(0, t.Param ?? 1);
                        break;
                    case "u" when t.Param is { } u && field is not null:
                        text.AddChar((char)(u < 0 ? u + 65536 : u));
                        skipChars = uc;
                        break;
                    case "pard" or "par" or "sectd" when infoDepth < 0:
                        // The document body started without an info group.
                        return meta;
                    default:
                        if (star)
                        {
                            skipDepth = depth;
                        }

                        break;
                }

                continue;
            }

            if (field is null)
            {
                continue;
            }

            if (t.Type == RtfTokenType.Hex)
            {
                if (skipChars > 0)
                {
                    skipChars--;
                }
                else
                {
                    text.AddByte((byte)t.Param!.Value);
                }
            }
            else if (t.Type == RtfTokenType.Text)
            {
                var span = data.AsSpan(t.Start, t.Length);
                var skip = Math.Min(skipChars, span.Length);
                skipChars -= skip;
                text.AddBytes(span[skip..]);
            }
            else if (t.Type == RtfTokenType.Symbol && t.Symbol is '\\' or '{' or '}')
            {
                text.AddChar(t.Symbol);
            }
        }

        return meta;
    }
}
