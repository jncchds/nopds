using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Nopds.Domain.Text;

namespace Nopds.Formats.Parsers;

/// <summary>
/// FictionBook 2 metadata reader. Parses only the root tag + &lt;description&gt; with a streaming
/// XmlReader (fast, tolerant of broken bodies) and falls back to regular expressions when even the
/// description is malformed. Replaces SimpleOPDS fb2parse.py / fb2sax.py.
/// </summary>
public sealed partial class Fb2Parser : IBookParser
{
    public IReadOnlyCollection<string> Formats { get; } = ["fb2"];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover)
    {
        var bytes = ReadAll(stream);
        var text = Fb2Text.Decode(bytes);
        return ParseText(text, includeCover);
    }

    public BookMetadata ParseText(string text, bool includeCover)
    {
        var meta = new BookMetadata();
        string? coverId = null;

        var rootStart = text.IndexOf("<FictionBook", StringComparison.Ordinal);
        var descStart = text.IndexOf("<description", StringComparison.Ordinal);
        var descEnd = text.IndexOf("</description>", StringComparison.Ordinal);
        if (rootStart < 0 || descStart < 0 || descEnd < 0)
        {
            throw new FormatException("Not a FictionBook document.");
        }

        var rootEnd = text.IndexOf('>', rootStart);
        var head = text[rootStart..(rootEnd + 1)] + text[descStart..(descEnd + "</description>".Length)] + "</FictionBook>";

        try
        {
            coverId = ReadDescription(Fb2Text.Sanitize(head), meta);
        }
        catch (XmlException)
        {
            meta = new BookMetadata();
            coverId = RegexFallback(head, meta);
        }

        if (includeCover && coverId is not null)
        {
            meta.Cover = FindBinary(text, coverId);
        }

        return meta;
    }

    private static string? ReadDescription(string xml, BookMetadata meta)
    {
        using var r = XmlReader.Create(new StringReader(xml), Fb2Text.ReaderSettings);
        string? coverId = null;
        var path = new List<string>();
        string? first = null, middle = null, last = null, nick = null;

        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                var name = r.LocalName;
                var empty = r.IsEmptyElement;
                path.Add(name);
                var section = path.Count > 2 ? path[2] : null;

                if (section == "title-info")
                {
                    switch (name)
                    {
                        case "book-title":
                            meta.Title = TextNormalizer.Strip(ReadText(r, path));
                            continue;
                        case "author":
                            first = middle = last = nick = null;
                            break;
                        case "first-name" when Parent(path) == "author":
                            first = ReadText(r, path);
                            continue;
                        case "middle-name" when Parent(path) == "author":
                            middle = ReadText(r, path);
                            continue;
                        case "last-name" when Parent(path) == "author":
                            last = ReadText(r, path);
                            continue;
                        case "nickname" when Parent(path) == "author":
                            nick = ReadText(r, path);
                            continue;
                        case "genre":
                            var g = TextNormalizer.Strip(ReadText(r, path)).ToLowerInvariant();
                            if (g.Length > 0 && !meta.Genres.Contains(g))
                            {
                                meta.Genres.Add(g);
                            }

                            continue;
                        case "lang":
                            meta.Lang = TextNormalizer.Strip(ReadText(r, path));
                            continue;
                        case "annotation":
                            meta.Annotation = ReadAnnotation(r, path);
                            continue;
                        case "sequence":
                            AddSequence(meta, r.GetAttribute("name"), r.GetAttribute("number"));
                            break;
                        case "image" when Parent(path) == "coverpage":
                            var href = r.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? FirstHrefAttribute(r);
                            if (href is { Length: > 1 } && href[0] == '#')
                            {
                                coverId ??= href[1..];
                            }

                            break;
                    }
                }
                else if (section == "document-info" && name == "date" && meta.DocDate is null)
                {
                    var value = r.GetAttribute("value");
                    var txt = ReadText(r, path);
                    meta.DocDate = TextNormalizer.Strip(string.IsNullOrWhiteSpace(value) ? txt : value);
                    continue;
                }

                if (empty)
                {
                    path.RemoveAt(path.Count - 1);
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.LocalName == "author" && path.Count > 2 && path[2] == "title-info")
                {
                    var author = TextNormalizer.AuthorName(first, middle, last, nick);
                    if (author.Length > 0 && !meta.Authors.Contains(author))
                    {
                        meta.Authors.Add(author);
                    }
                }

                if (path.Count > 0)
                {
                    path.RemoveAt(path.Count - 1);
                }
            }
        }

        return coverId;
    }

    private static string? Parent(List<string> path) => path.Count > 1 ? path[^2] : null;

    private static string? FirstHrefAttribute(XmlReader r)
    {
        if (!r.HasAttributes)
        {
            return null;
        }

        for (var i = 0; i < r.AttributeCount; i++)
        {
            r.MoveToAttribute(i);
            if (r.LocalName == "href")
            {
                var v = r.Value;
                r.MoveToElement();
                return v;
            }
        }

        r.MoveToElement();
        return null;
    }

    /// <summary>Reads all text inside the current element and consumes its end tag.</summary>
    private static string ReadText(XmlReader r, List<string> path)
    {
        path.RemoveAt(path.Count - 1);
        if (r.IsEmptyElement)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var depth = r.Depth;
        while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            if (r.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                sb.Append(r.Value);
            }
        }

        return sb.ToString().Trim();
    }

    private static string ReadAnnotation(XmlReader r, List<string> path)
    {
        path.RemoveAt(path.Count - 1);
        if (r.IsEmptyElement)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var depth = r.Depth;
        while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    sb.Append(r.Value);
                    break;
                case XmlNodeType.Element when r.LocalName is "empty-line":
                    sb.Append('\n');
                    break;
                case XmlNodeType.EndElement when r.LocalName is "p" or "v" or "subtitle":
                    sb.Append('\n');
                    break;
            }
        }

        return Collapse(sb.ToString());
    }

    private static string Collapse(string s)
    {
        var lines = s.Split('\n').Select(l => Whitespace().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        return string.Join('\n', lines);
    }

    private static void AddSequence(BookMetadata meta, string? name, string? number)
    {
        var n = TextNormalizer.Strip(name);
        if (n.Length == 0 || meta.Series.Any(s => s.Name == n))
        {
            return;
        }

        meta.Series.Add(new SeriesRef(n, ParseNumber(number)));
    }

    public static int ParseNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return 0;
        }

        var m = LeadingDigits().Match(number);
        return m.Success && int.TryParse(m.Value, out var v) ? v : 0;
    }

    private static CoverImage? FindBinary(string text, string id)
    {
        var escaped = Regex.Escape(id);
        var m = Regex.Match(text, $@"<binary\b[^>]*\bid\s*=\s*[""']{escaped}[""'][^>]*>([^<]*)</binary>", RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return null;
        }

        var ct = ContentType().Match(m.Value);
        try
        {
            var data = Convert.FromBase64String(Whitespace().Replace(m.Groups[1].Value, string.Empty));
            return new CoverImage(data, ct.Success ? ct.Groups[1].Value : MediaTypes.ForImage(id));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? RegexFallback(string head, BookMetadata meta)
    {
        string Tag(string tag) =>
            Regex.Match(head, $@"<(?:\w+:)?{tag}\b[^>]*>(.*?)</(?:\w+:)?{tag}>", RegexOptions.Singleline) is { Success: true } m
                ? TextNormalizer.Strip(Tags().Replace(m.Groups[1].Value, " "))
                : string.Empty;

        var titleInfo = Regex.Match(head, @"<title-info>(.*?)</title-info>", RegexOptions.Singleline);
        var scope = titleInfo.Success ? titleInfo.Groups[1].Value : head;
        meta.Title = Tag("book-title");
        foreach (Match a in Regex.Matches(scope, @"<author>(.*?)</author>", RegexOptions.Singleline))
        {
            string Part(string tag) => Regex.Match(a.Groups[1].Value, $@"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline).Groups[1].Value;
            var name = TextNormalizer.AuthorName(Part("first-name"), Part("middle-name"), Part("last-name"), Part("nickname"));
            if (name.Length > 0 && !meta.Authors.Contains(name))
            {
                meta.Authors.Add(name);
            }
        }

        foreach (Match g in Regex.Matches(scope, @"<genre[^>]*>(.*?)</genre>", RegexOptions.Singleline))
        {
            meta.Genres.Add(TextNormalizer.Strip(g.Groups[1].Value).ToLowerInvariant());
        }

        foreach (Match s in Regex.Matches(scope, @"<sequence\b([^>]*)/?>", RegexOptions.Singleline))
        {
            var attrs = s.Groups[1].Value;
            AddSequence(meta, Attr(attrs, "name"), Attr(attrs, "number"));
        }

        meta.Lang = Tag("lang");
        var cover = Regex.Match(scope, @"<coverpage>.*?href\s*=\s*[""']#([^""']+)[""']", RegexOptions.Singleline);
        return cover.Success ? cover.Groups[1].Value : null;
    }

    private static string? Attr(string attrs, string name) =>
        Regex.Match(attrs, $@"\b{name}\s*=\s*[""']([^""']*)[""']") is { Success: true } m ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null;

    internal static byte[] ReadAll(Stream s)
    {
        if (s is MemoryStream ms && ms.TryGetBuffer(out var seg) && seg.Offset == 0 && s.Position == 0)
        {
            return seg.Count == seg.Array!.Length ? seg.Array : seg.ToArray();
        }

        using var copy = new MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\s*(\d+)")]
    private static partial Regex LeadingDigits();

    [GeneratedRegex(@"content-type\s*=\s*[""']([^""']+)[""']")]
    private static partial Regex ContentType();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();
}
