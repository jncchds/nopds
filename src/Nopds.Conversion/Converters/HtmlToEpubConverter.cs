using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Nopds.Conversion.Epub;
using Nopds.Formats;

namespace Nopds.Conversion.Converters;

/// <summary>
/// HTML → EPUB. The page is parsed leniently and rebuilt from a whitelist of structural and inline
/// elements (no scripts, styles or forms); embedded data: images are kept, external resources are not.
/// </summary>
public sealed partial class HtmlToEpubConverter : IBookConverter
{
    public IReadOnlyCollection<string> Sources { get; } = ["html", "htm", "xhtml"];

    public string Target => "epub";

    private static readonly HashSet<string> Skip =
        ["script", "style", "noscript", "iframe", "object", "embed", "form", "input", "button", "select", "textarea", "svg", "math", "template", "head", "nav", "canvas", "video", "audio", "map"];

    private static readonly Dictionary<string, string> Rename = new()
    {
        ["b"] = "strong", ["i"] = "em", ["strike"] = "s", ["del"] = "s", ["tt"] = "code", ["kbd"] = "code", ["var"] = "em",
        ["big"] = "span", ["font"] = "span", ["ins"] = "span", ["aside"] = "div", ["center"] = "div", ["address"] = "p",
    };

    private static readonly HashSet<string> Keep =
    [
        "p", "div", "section", "article", "main", "header", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre",
        "ul", "ol", "li", "dl", "dt", "dd", "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "figure", "figcaption", "hr", "br",
        "a", "em", "strong", "u", "s", "sub", "sup", "code", "small", "span", "q", "cite", "abbr", "mark", "img",
    ];

    internal static readonly HashSet<string> Inline = ["a", "em", "strong", "u", "s", "sub", "sup", "code", "small", "span", "q", "cite", "abbr", "mark", "img", "br"];

    public void Convert(byte[] input, BookMetadata meta, Stream output)
    {
        var document = new HtmlParser().ParseDocument(Decode(input));
        var embedded = new BookMetadata
        {
            Title = NullIfBlank(document.Title),
            Lang = NullIfBlank(document.DocumentElement.GetAttribute("lang")),
        };
        if (NullIfBlank(document.QuerySelector("meta[name=author]")?.GetAttribute("content")) is { } author)
        {
            var parts = author.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            embedded.Authors.Add(parts.Length > 1 ? parts[^1] + " " + string.Join(' ', parts[..^1]) : author);
        }

        var doc = new EpubDocument { Meta = MetadataMerge.Combine(meta, embedded) };
        var body = document.Body ?? document.DocumentElement;
        var nodes = body.ChildNodes.SelectMany(n => ToXhtml(n, doc)).ToList();
        ChapterBuilder.Build(doc, WrapLoose(nodes));
        EpubWriter.Write(doc, output);
    }

    /// <summary>Honors a declared charset; otherwise detects the encoding like plain text.</summary>
    private static string Decode(byte[] input)
    {
        var head = Encoding.ASCII.GetString(input, 0, Math.Min(input.Length, 4096));
        var m = Charset().Match(head);
        if (m.Success && TextDecoding.TryGetEncoding(m.Groups[1].Value.Trim()) is { } enc)
        {
            return enc.GetString(input);
        }

        return TextDecoding.Decode(input);
    }

    private static IEnumerable<XNode> ToXhtml(INode node, EpubDocument doc)
    {
        if (node is IText text)
        {
            yield return new XText(Xhtml.CleanText(text.Data));
            yield break;
        }

        if (node is not IElement el)
        {
            yield break;
        }

        var name = el.LocalName;
        if (Skip.Contains(name))
        {
            yield break;
        }

        var cls = name == "center" || el.GetAttribute("align")?.ToLowerInvariant() == "center" ? "center" : null;
        name = Rename.GetValueOrDefault(name, name);
        if (!Keep.Contains(name))
        {
            foreach (var n in el.ChildNodes.SelectMany(c => ToXhtml(c, doc)))
            {
                yield return n;
            }

            yield break;
        }

        var x = Xhtml.El(name);
        if (el.GetAttribute("id") is { Length: > 0 } id)
        {
            x.SetAttributeValue("id", Xhtml.SafeId(id));
        }

        if (cls is not null)
        {
            x.SetAttributeValue("class", cls);
        }

        switch (name)
        {
            case "img":
                var src = el.GetAttribute("src") ?? "";
                var data = DataUri(src, out var type);
                var href = data is null ? null : doc.AddImage(src, data, type);
                if (href is null)
                {
                    yield break;
                }

                x.SetAttributeValue("src", href);
                x.SetAttributeValue("alt", el.GetAttribute("alt") ?? "");
                yield return x;
                yield break;
            case "a":
                var link = el.GetAttribute("href");
                if (link is { Length: > 1 } && link[0] == '#')
                {
                    x.SetAttributeValue("href", "#" + Xhtml.SafeId(link[1..]));
                }
                else if (link is not null && ExternalLink().IsMatch(link))
                {
                    x.SetAttributeValue("href", link);
                }

                if (el.GetAttribute("name") is { Length: > 0 } anchor && x.Attribute("id") is null)
                {
                    x.SetAttributeValue("id", Xhtml.SafeId(anchor));
                }

                break;
            case "td" or "th":
                foreach (var a in new[] { "colspan", "rowspan" })
                {
                    if (el.GetAttribute(a) is { } v && int.TryParse(v, out var span) && span > 1)
                    {
                        x.SetAttributeValue(a, span);
                    }
                }

                break;
            case "hr" or "br":
                yield return x;
                yield break;
        }

        x.Add(el.ChildNodes.SelectMany(c => ToXhtml(c, doc)));
        yield return x;
    }

    /// <summary>Wraps runs of loose text and inline elements at block level into paragraphs.</summary>
    internal static IEnumerable<XElement> WrapLoose(IEnumerable<XNode> nodes)
    {
        XElement? p = null;
        foreach (var n in nodes)
        {
            if (n is XElement e && !Inline.Contains(e.Name.LocalName))
            {
                if (p is not null && !Xhtml.IsEmpty(p))
                {
                    yield return p;
                }

                p = null;
                yield return e;
            }
            else
            {
                p ??= Xhtml.El("p");
                p.Add(n);
            }
        }

        if (p is not null && !Xhtml.IsEmpty(p))
        {
            yield return p;
        }
    }

    private static byte[]? DataUri(string src, out string? mediaType)
    {
        mediaType = null;
        var m = DataUriPattern().Match(src);
        if (!m.Success)
        {
            return null;
        }

        mediaType = m.Groups[1].Value.ToLowerInvariant();
        try
        {
            return System.Convert.FromBase64String(m.Groups[2].Value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"<meta[^>]+charset\s*=\s*[""']?([\w\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Charset();

    [GeneratedRegex(@"^(?:https?|mailto):", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalLink();

    [GeneratedRegex(@"^data:(image/[\w.+\-]+);base64,(.+)$", RegexOptions.Singleline)]
    private static partial Regex DataUriPattern();
}
