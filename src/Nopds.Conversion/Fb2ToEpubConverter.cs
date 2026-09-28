using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Nopds.Formats;
using Nopds.Formats.Parsers;

namespace Nopds.Conversion;

/// <summary>
/// Native FictionBook 2 → EPUB 3 converter (no Java/Python tools). Top-level sections become chapters,
/// notes bodies become a notes document, links are rewritten across chapter files, and both an EPUB 3
/// navigation document and an EPUB 2 NCX are generated for older readers.
/// </summary>
public sealed class Fb2ToEpubConverter
{
    private static readonly XNamespace X = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Epub = "http://www.idpf.org/2007/ops";

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

    public void Convert(byte[] fb2, Stream output)
    {
        var doc = Fb2Text.LoadDocument(fb2);
        var root = doc.Root ?? throw new FormatException("Empty FB2 document.");
        var ns = root.Name.Namespace;
        var meta = new Fb2Parser().ParseText(doc.ToString(), includeCover: false);
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

        var title = meta.Title ?? "Untitled";
        var lang = string.IsNullOrWhiteSpace(meta.Lang) ? "en" : meta.Lang;
        var uid = "urn:uuid:" + Guid.NewGuid();

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimetype.Open(), new UTF8Encoding(false)))
        {
            w.Write("application/epub+zip");
        }

        Write(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);
        Write(zip, "OEBPS/styles.css", Css);

        foreach (var (_, file, _, data) in binaries)
        {
            var e = zip.CreateEntry("OEBPS/" + file, CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(data);
        }

        if (hasCover)
        {
            var img = ctx.Images[coverId!].File;
            WriteXhtml(zip, "OEBPS/text/cover.xhtml", title, lang,
                new XElement(X + "div", new XAttribute("class", "cover"),
                    new XElement(X + "img", new XAttribute("src", "../" + img), new XAttribute("alt", title))));
        }

        foreach (var ch in chapters)
        {
            ctx.CurrentFile = ch.File;
            var body = new XElement(X + "section");
            foreach (var node in ch.Content)
            {
                body.Add(Transform(node, ns, ctx, depth: 1));
            }

            WriteXhtml(zip, "OEBPS/" + ch.File, ch.Title, lang, body);
        }

        // Navigation.
        var navList = new XElement(X + "ol");
        foreach (var ch in chapters)
        {
            var li = new XElement(X + "li", new XElement(X + "a", new XAttribute("href", ch.File), ch.Title));
            if (ch.Children.Count > 0)
            {
                li.Add(new XElement(X + "ol", ch.Children.Select(c => new XElement(X + "li", new XElement(X + "a", new XAttribute("href", c.File), c.Title)))));
            }

            navList.Add(li);
        }

        WriteXhtml(zip, "OEBPS/nav.xhtml", title, lang,
            new XElement(X + "nav", new XAttribute(Epub + "type", "toc"), new XAttribute("id", "toc"), new XElement(X + "h1", title), navList));

        XNamespace n = "http://www.daisy.org/z3986/2005/ncx/";
        var navMap = new XElement(n + "navMap");
        for (var i = 0; i < chapters.Count; i++)
        {
            navMap.Add(new XElement(n + "navPoint",
                new XAttribute("id", $"np{i + 1}"),
                new XAttribute("playOrder", i + 1),
                new XElement(n + "navLabel", new XElement(n + "text", chapters[i].Title)),
                new XElement(n + "content", new XAttribute("src", chapters[i].File))));
        }

        var ncx = new XDocument(new XElement(n + "ncx",
            new XAttribute("version", "2005-1"),
            new XElement(n + "head", new XElement(n + "meta", new XAttribute("name", "dtb:uid"), new XAttribute("content", uid))),
            new XElement(n + "docTitle", new XElement(n + "text", title)),
            navMap));
        WriteDoc(zip, "OEBPS/toc.ncx", ncx);

        // Package document.
        XNamespace opf = "http://www.idpf.org/2007/opf";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        var metadata = new XElement(opf + "metadata", new XAttribute(XNamespace.Xmlns + "dc", dc),
            new XElement(dc + "identifier", new XAttribute("id", "uid"), uid),
            new XElement(dc + "title", title),
            new XElement(dc + "language", lang),
            new XElement(opf + "meta", new XAttribute("property", "dcterms:modified"), DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        var authorNo = 0;
        foreach (var a in meta.Authors)
        {
            var id = $"author{++authorNo}";
            metadata.Add(new XElement(dc + "creator", new XAttribute("id", id), DisplayName(a)));
            metadata.Add(new XElement(opf + "meta", new XAttribute("refines", "#" + id), new XAttribute("property", "file-as"), a));
            metadata.Add(new XElement(opf + "meta", new XAttribute("refines", "#" + id), new XAttribute("property", "role"), new XAttribute("scheme", "marc:relators"), "aut"));
        }

        foreach (var g in meta.Genres)
        {
            metadata.Add(new XElement(dc + "subject", g));
        }

        if (!string.IsNullOrEmpty(meta.Annotation))
        {
            metadata.Add(new XElement(dc + "description", meta.Annotation));
        }

        if (meta.Series.FirstOrDefault() is { } series)
        {
            metadata.Add(new XElement(opf + "meta", new XAttribute("property", "belongs-to-collection"), new XAttribute("id", "series"), series.Name));
            metadata.Add(new XElement(opf + "meta", new XAttribute("refines", "#series"), new XAttribute("property", "collection-type"), "series"));
            metadata.Add(new XElement(opf + "meta", new XAttribute("refines", "#series"), new XAttribute("property", "group-position"), series.Number));
            metadata.Add(new XElement(opf + "meta", new XAttribute("name", "calibre:series"), new XAttribute("content", series.Name)));
            metadata.Add(new XElement(opf + "meta", new XAttribute("name", "calibre:series_index"), new XAttribute("content", series.Number)));
        }

        var manifest = new XElement(opf + "manifest",
            new XElement(opf + "item", new XAttribute("id", "nav"), new XAttribute("href", "nav.xhtml"), new XAttribute("media-type", "application/xhtml+xml"), new XAttribute("properties", "nav")),
            new XElement(opf + "item", new XAttribute("id", "ncx"), new XAttribute("href", "toc.ncx"), new XAttribute("media-type", "application/x-dtbncx+xml")),
            new XElement(opf + "item", new XAttribute("id", "css"), new XAttribute("href", "styles.css"), new XAttribute("media-type", "text/css")));
        var spine = new XElement(opf + "spine", new XAttribute("toc", "ncx"));

        if (hasCover)
        {
            manifest.Add(new XElement(opf + "item", new XAttribute("id", "cover"), new XAttribute("href", "text/cover.xhtml"), new XAttribute("media-type", "application/xhtml+xml")));
            spine.Add(new XElement(opf + "itemref", new XAttribute("idref", "cover"), new XAttribute("linear", "no")));
            metadata.Add(new XElement(opf + "meta", new XAttribute("name", "cover"), new XAttribute("content", "img-cover")));
        }

        for (var i = 0; i < binaries.Count; i++)
        {
            var (id, file, mediaType, _) = binaries[i];
            var item = new XElement(opf + "item", new XAttribute("id", id == coverId ? "img-cover" : $"img{i}"),
                new XAttribute("href", file), new XAttribute("media-type", mediaType));
            if (id == coverId)
            {
                item.Add(new XAttribute("properties", "cover-image"));
            }

            manifest.Add(item);
        }

        for (var i = 0; i < chapters.Count; i++)
        {
            manifest.Add(new XElement(opf + "item", new XAttribute("id", $"ch{i + 1}"), new XAttribute("href", chapters[i].File), new XAttribute("media-type", "application/xhtml+xml")));
            spine.Add(new XElement(opf + "itemref", new XAttribute("idref", $"ch{i + 1}")));
        }

        var package = new XDocument(new XElement(opf + "package", new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "uid"),
            new XAttribute(XNamespace.Xml + "lang", lang), metadata, manifest, spine));
        WriteDoc(zip, "OEBPS/content.opf", package);
    }

    private static string DisplayName(string lastFirst)
    {
        var parts = lastFirst.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? string.Join(' ', parts[1..]) + " " + parts[0] : lastFirst;
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

    private static void WriteXhtml(ZipArchive zip, string path, string title, string lang, XElement body)
    {
        var depth = path.Count(c => c == '/') - 1;
        var css = string.Concat(Enumerable.Repeat("../", depth)) + "styles.css";
        var html = new XElement(X + "html",
            new XAttribute(XNamespace.Xml + "lang", lang), new XAttribute("lang", lang),
            new XElement(X + "head",
                new XElement(X + "meta", new XAttribute("charset", "utf-8")),
                new XElement(X + "title", title),
                new XElement(X + "link", new XAttribute("rel", "stylesheet"), new XAttribute("type", "text/css"), new XAttribute("href", css))),
            new XElement(X + "body", body));
        html.Add(new XAttribute(XNamespace.Xmlns + "epub", Epub));
        WriteDoc(zip, path, new XDocument(new XDocumentType("html", null, null, null), html));
    }

    private static void WriteDoc(ZipArchive zip, string path, XDocument doc)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        doc.Save(w);
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private const string Css = """
        body { margin: 0 3%; line-height: 1.4; text-align: justify; }
        h1, h2, h3, h4, h5, h6 { text-align: center; text-indent: 0; margin: 1.5em 0 1em; line-height: 1.2; page-break-after: avoid; }
        h1 { font-size: 1.6em; page-break-before: always; }
        p { margin: 0; text-indent: 1.5em; }
        p.empty-line { text-indent: 0; }
        p.subtitle { text-align: center; font-weight: bold; text-indent: 0; margin: 1em 0; }
        blockquote { margin: 1em 0 1em 2em; font-style: italic; }
        blockquote.epigraph { margin-left: 30%; font-size: 0.95em; }
        p.text-author { text-align: right; font-style: italic; }
        div.poem { margin: 1em 0 1em 2em; }
        div.stanza { margin: 0.5em 0; }
        p.v { text-indent: 0; text-align: left; }
        div.image, div.cover { text-align: center; text-indent: 0; margin: 1em 0; }
        img { max-width: 100%; }
        div.cover img { height: 95%; }
        a.noteref { vertical-align: super; font-size: 0.75em; text-decoration: none; }
        table { border-collapse: collapse; margin: 1em auto; }
        td, th { border: 1px solid #888; padding: 0.2em 0.4em; }
        """;
}
