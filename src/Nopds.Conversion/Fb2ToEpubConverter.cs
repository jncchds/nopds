using System.Xml.Linq;
using Nopds.Conversion.Epub;
using Nopds.Formats;
using Nopds.Formats.Parsers;

namespace Nopds.Conversion;

/// <summary>
/// Native FictionBook 2 → EPUB 3 converter (no Java/Python tools). Top-level sections become chapters,
/// notes bodies become a notes document, links are rewritten across chapter files, and both an EPUB 3
/// navigation document and an EPUB 2 NCX are generated for older readers.
/// </summary>
public sealed class Fb2ToEpubConverter : IBookConverter
{
    public IReadOnlyCollection<string> Sources { get; } = ["fb2"];

    public string Target => "epub";

    /// <summary>FB2 carries rich metadata of its own, so the catalog metadata only fills gaps.</summary>
    public void Convert(byte[] input, BookMetadata meta, Stream output) => Convert(input, output, meta);

    private static readonly XNamespace X = Xhtml.Ns;
    private static readonly XNamespace Epub = Xhtml.Ops;

    private sealed class Chapter(string file, string title, int level)
    {
        public string File { get; } = file;
        public string Title { get; } = title;
        public int Level { get; } = level;
        public List<XNode> Content { get; } = [];
        public List<Chapter> Children { get; } = [];
    }

    private sealed class Context
    {
        public Dictionary<string, string> IdToFile { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (string File, string MediaType)> Images { get; } = new(StringComparer.Ordinal);
        public string CurrentFile { get; set; } = "";
    }

    public void Convert(byte[] fb2, Stream output, BookMetadata? fallback = null)
    {
        var doc = Fb2Text.LoadDocument(fb2);
        var root = doc.Root ?? throw new FormatException("Empty FB2 document.");
        var ns = root.Name.Namespace;
        var meta = new Fb2Parser().ParseText(doc.ToString(), includeCover: false);
        if (fallback is not null)
        {
            MetadataMerge.FillGaps(meta, fallback);
        }

        var ctx = new Context();

        // Images.
        var binaries = new List<(string Id, string File, string MediaType, byte[] Data)>();
        foreach (var bin in root.Elements(ns + "binary"))
        {
            var id = (string?)bin.Attribute("id");
            if (id is null)
            {
                continue;
            }

            byte[] data;
            try
            {
                data = System.Convert.FromBase64String(bin.Value.Trim());
            }
            catch (FormatException)
            {
                continue;
            }

            var mediaType = (string?)bin.Attribute("content-type") ?? MediaTypes.ForImage(id);
            var ext = mediaType switch { "image/png" => ".png", "image/gif" => ".gif", _ => ".jpg" };
            var file = "images/img" + binaries.Count.ToString("D4") + ext;
            ctx.Images[id] = (file, mediaType);
            binaries.Add((id, file, mediaType, data));
        }

        var coverId = root.Element(ns + "description")?.Element(ns + "title-info")?.Element(ns + "coverpage")?.Elements()
            .Select(Href).FirstOrDefault(h => h is not null)?.TrimStart('#');
        var hasCover = coverId is not null && ctx.Images.ContainsKey(coverId);

        // Split bodies into chapter files.
        var bodies = root.Elements(ns + "body").ToList();
        var main = bodies.FirstOrDefault(b => (string?)b.Attribute("name") is null or "main") ?? bodies.FirstOrDefault();
        var chapters = new List<Chapter>();
        var fileNo = 0;
        string NextFile() => $"text/ch{++fileNo:D3}.xhtml";

        if (main is not null)
        {
            var intro = new Chapter(NextFile(), meta.Title ?? "Title", 1);
            foreach (var el in main.Elements().Where(e => e.Name.LocalName != "section"))
            {
                intro.Content.Add(el);
            }

            var sections = main.Elements(ns + "section").ToList();
            if (intro.Content.Count > 0 || sections.Count == 0)
            {
                chapters.Add(intro);
            }
            else
            {
                fileNo--;
            }

            foreach (var s in sections)
            {
                var ch = new Chapter(NextFile(), SectionTitle(s, ns) ?? $"{chapters.Count + 1}", 1);
                ch.Content.Add(s);
                foreach (var sub in s.Elements(ns + "section"))
                {
                    var subTitle = SectionTitle(sub, ns);
                    if (subTitle is not null)
                    {
                        ch.Children.Add(new Chapter(ch.File, subTitle, 2));
                    }
                }

                chapters.Add(ch);
            }
        }

        foreach (var notes in bodies.Where(b => b != main))
        {
            var ch = new Chapter(NextFile(), BodyTitle(notes, ns) ?? "Notes", 1);
            ch.Content.AddRange(notes.Elements());
            chapters.Add(ch);
        }

        // Map ids to files for link rewriting.
        foreach (var ch in chapters)
        {
            foreach (var node in ch.Content.OfType<XElement>())
            {
                foreach (var el in node.DescendantsAndSelf())
                {
                    if ((string?)el.Attribute("id") is { } id)
                    {
                        ctx.IdToFile.TryAdd(id, ch.File);
                    }
                }
            }
        }

        var epub = new EpubDocument { Meta = meta };
        foreach (var (_, file, mediaType, data) in binaries)
        {
            epub.Images.Add(new EpubImage(file, mediaType, data));
        }

        if (hasCover)
        {
            epub.CoverFile = ctx.Images[coverId!].File;
        }

        foreach (var ch in chapters)
        {
            ctx.CurrentFile = ch.File;
            var body = new XElement(X + "section");
            foreach (var node in ch.Content)
            {
                body.Add(Transform(node, ns, ctx, depth: 1));
            }

            var chapter = new EpubChapter(ch.File, ch.Title) { Body = body };
            chapter.Children.AddRange(ch.Children.Select(c => (c.Title, c.File)));
            epub.Chapters.Add(chapter);
        }

        EpubWriter.Write(epub, output);
    }

    private static string? Href(XElement e) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;

    private static string? SectionTitle(XElement section, XNamespace ns)
    {
        var t = section.Element(ns + "title");
        if (t is null)
        {
            return null;
        }

        var text = string.Join(" ", t.Elements().Select(p => p.Value.Trim()).Where(s => s.Length > 0));
        text = string.IsNullOrWhiteSpace(text) ? t.Value.Trim() : text;
        return text.Length == 0 ? null : text.Length > 120 ? text[..120] + "…" : text;
    }

    private static string? BodyTitle(XElement body, XNamespace ns) =>
        SectionTitle(body, ns) ?? (string?)body.Attribute("name");

    private IEnumerable<XNode> Transform(XNode node, XNamespace ns, Context ctx, int depth)
    {
        if (node is XText text)
        {
            yield return new XText(text.Value);
            yield break;
        }

        if (node is not XElement el)
        {
            yield break;
        }

        IEnumerable<XNode> Children(int d) => el.Nodes().SelectMany(n => Transform(n, ns, ctx, d));
        XElement E(string name, object? cls, IEnumerable<XNode> content)
        {
            var result = new XElement(X + name, content);
            if (cls is string c)
            {
                result.SetAttributeValue("class", c);
            }

            if ((string?)el.Attribute("id") is { } id)
            {
                result.SetAttributeValue("id", id);
            }

            return result;
        }

        switch (el.Name.LocalName)
        {
            case "section":
                yield return E("section", null, Children(depth + 1));
                break;
            case "title":
                var h = Math.Clamp(depth, 1, 6);
                var lines = el.Elements().Where(p => p.Name.LocalName is "p").ToList();
                var heading = new XElement(X + $"h{h}", new XAttribute("class", "title"));
                if (lines.Count == 0)
                {
                    heading.Add(Children(depth));
                }
                else
                {
                    for (var i = 0; i < lines.Count; i++)
                    {
                        if (i > 0)
                        {
                            heading.Add(new XElement(X + "br"));
                        }

                        heading.Add(lines[i].Nodes().SelectMany(n => Transform(n, ns, ctx, depth)));
                    }
                }

                yield return heading;
                break;
            case "subtitle":
                yield return E("p", "subtitle", Children(depth));
                break;
            case "p":
                yield return E("p", null, Children(depth));
                break;
            case "empty-line":
                yield return new XElement(X + "p", new XAttribute("class", "empty-line"), new XText(" "));
                break;
            case "emphasis":
                yield return E("em", null, Children(depth));
                break;
            case "strong":
                yield return E("strong", null, Children(depth));
                break;
            case "strikethrough":
                yield return E("s", null, Children(depth));
                break;
            case "sub":
            case "sup":
            case "code":
                yield return E(el.Name.LocalName, null, Children(depth));
                break;
            case "epigraph":
                yield return E("blockquote", "epigraph", Children(depth));
                break;
            case "cite":
                yield return E("blockquote", "cite", Children(depth));
                break;
            case "annotation":
                yield return E("div", "annotation", Children(depth));
                break;
            case "poem":
                yield return E("div", "poem", Children(depth));
                break;
            case "stanza":
                yield return E("div", "stanza", Children(depth));
                break;
            case "v":
                yield return E("p", "v", Children(depth));
                break;
            case "text-author":
                yield return E("p", "text-author", Children(depth));
                break;
            case "date":
                yield return E("p", "date", Children(depth));
                break;
            case "image":
                var src = Href(el)?.TrimStart('#');
                if (src is not null && ctx.Images.TryGetValue(src, out var img))
                {
                    var imgEl = new XElement(X + "img", new XAttribute("src", "../" + img.File), new XAttribute("alt", (string?)el.Attribute("alt") ?? ""));
                    yield return el.Parent?.Name.LocalName is "p" or "v" or "subtitle" or "td" or "th"
                        ? imgEl
                        : new XElement(X + "div", new XAttribute("class", "image"), imgEl);
                }

                break;
            case "a":
                var href = Href(el) ?? "";
                if (href.StartsWith('#'))
                {
                    var target = href[1..];
                    var file = ctx.IdToFile.GetValueOrDefault(target);
                    href = file is null || file == ctx.CurrentFile ? "#" + target : "../" + file + "#" + target;
                }

                var a = new XElement(X + "a", new XAttribute("href", href), Children(depth));
                if ((string?)el.Attribute("type") == "note")
                {
                    a.SetAttributeValue(Epub + "type", "noteref");
                    a.SetAttributeValue("class", "noteref");
                }

                yield return a;
                break;
            case "table":
            case "tr":
            case "td":
            case "th":
                var t = E(el.Name.LocalName, null, Children(depth));
                foreach (var attr in el.Attributes().Where(x => x.Name.LocalName is "colspan" or "rowspan" or "align"))
                {
                    t.SetAttributeValue(attr.Name.LocalName, attr.Value);
                }

                yield return t;
                break;
            case "style":
                foreach (var n in Children(depth))
                {
                    yield return n;
                }

                break;
            default:
                // Unknown element: keep its content.
                foreach (var n in Children(depth))
                {
                    yield return n;
                }

                break;
        }
    }
}
