using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Nopds.Conversion.Epub;

/// <summary>Packages an <see cref="EpubDocument"/> as EPUB 3 with a navigation document and an EPUB 2 NCX.</summary>
public static class EpubWriter
{
    private static readonly XNamespace X = Xhtml.Ns;

    public static void Write(EpubDocument doc, Stream output)
    {
        var meta = doc.Meta;
        var title = string.IsNullOrWhiteSpace(meta.Title) ? "Untitled" : meta.Title;
        var lang = string.IsNullOrWhiteSpace(meta.Lang) ? "en" : meta.Lang;
        var uid = "urn:uuid:" + Guid.NewGuid();
        var chapters = doc.Chapters.Count > 0 ? doc.Chapters : [new EpubChapter("text/ch001.xhtml", title)];

        RewriteAnchors(chapters);

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

        foreach (var img in doc.Images)
        {
            var e = zip.CreateEntry("OEBPS/" + img.File, CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(img.Data);
        }

        var hasCover = doc.CoverFile is not null && doc.Images.Any(i => i.File == doc.CoverFile);
        if (hasCover)
        {
            WriteXhtml(zip, "OEBPS/text/cover.xhtml", title, lang,
                new XElement(X + "div", new XAttribute("class", "cover"),
                    new XElement(X + "img", new XAttribute("src", "../" + doc.CoverFile), new XAttribute("alt", title))));
        }

        foreach (var ch in chapters)
        {
            WriteXhtml(zip, "OEBPS/" + ch.File, ch.Title, lang, ch.Body);
        }

        // Navigation.
        var toc = chapters.Where(c => c.InToc).ToList();
        if (toc.Count == 0)
        {
            toc.Add(chapters[0]);
        }

        var navList = new XElement(X + "ol");
        foreach (var ch in toc)
        {
            var li = new XElement(X + "li", new XElement(X + "a", new XAttribute("href", ch.File), ch.Title));
            if (ch.Children.Count > 0)
            {
                li.Add(new XElement(X + "ol", ch.Children.Select(c => new XElement(X + "li", new XElement(X + "a", new XAttribute("href", c.Href), c.Title)))));
            }

            navList.Add(li);
        }

        WriteXhtml(zip, "OEBPS/nav.xhtml", title, lang,
            new XElement(X + "nav", new XAttribute(Xhtml.Ops + "type", "toc"), new XAttribute("id", "toc"), new XElement(X + "h1", title), navList));

        XNamespace n = "http://www.daisy.org/z3986/2005/ncx/";
        var navMap = new XElement(n + "navMap");
        var order = 0;
        XElement NavPoint(string label, string src) => new(n + "navPoint",
            new XAttribute("id", $"np{++order}"),
            new XAttribute("playOrder", order),
            new XElement(n + "navLabel", new XElement(n + "text", label)),
            new XElement(n + "content", new XAttribute("src", src)));
        foreach (var ch in toc)
        {
            var point = NavPoint(ch.Title, ch.File);
            foreach (var (childTitle, href) in ch.Children)
            {
                point.Add(NavPoint(childTitle, href));
            }

            navMap.Add(point);
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

        for (var i = 0; i < doc.Images.Count; i++)
        {
            var img = doc.Images[i];
            var isCover = hasCover && img.File == doc.CoverFile;
            var item = new XElement(opf + "item", new XAttribute("id", isCover ? "img-cover" : $"img{i}"),
                new XAttribute("href", img.File), new XAttribute("media-type", img.MediaType));
            if (isCover)
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

    /// <summary>Points "#id" links at the chapter file that actually holds the id after splitting.</summary>
    private static void RewriteAnchors(List<EpubChapter> chapters)
    {
        var idToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ch in chapters)
        {
            foreach (var el in ch.Body.DescendantsAndSelf())
            {
                if ((string?)el.Attribute("id") is { } id)
                {
                    idToFile.TryAdd(id, ch.File);
                }
            }
        }

        foreach (var ch in chapters)
        {
            foreach (var a in ch.Body.Descendants(X + "a"))
            {
                var href = (string?)a.Attribute("href");
                if (href is not { Length: > 1 } || href[0] != '#')
                {
                    continue;
                }

                if (idToFile.TryGetValue(href[1..], out var file))
                {
                    if (file != ch.File)
                    {
                        a.SetAttributeValue("href", "../" + file + href);
                    }
                }
                else
                {
                    // Dangling anchor (bookmark lost in conversion): keep the text, drop the link.
                    a.Attribute("href")!.Remove();
                }
            }
        }
    }

    public static string DisplayName(string lastFirst)
    {
        var parts = lastFirst.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? string.Join(' ', parts[1..]) + " " + parts[0] : lastFirst;
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
        html.Add(new XAttribute(XNamespace.Xmlns + "epub", Xhtml.Ops));
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
        p.center, .center { text-align: center; text-indent: 0; }
        p.right, .right { text-align: right; text-indent: 0; }
        p.scene-break { text-align: center; text-indent: 0; margin: 1em 0; }
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
        aside.note, div.note { margin: 0.5em 0; font-size: 0.9em; }
        li p, td p, th p, aside p { text-indent: 0; }
        pre { white-space: pre-wrap; text-align: left; font-size: 0.9em; }
        table { border-collapse: collapse; margin: 1em auto; }
        td, th { border: 1px solid #888; padding: 0.2em 0.4em; }
        """;
}
