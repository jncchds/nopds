using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Nopds.Conversion;
using Nopds.Conversion.Converters;
using Nopds.Domain.Entities;
using Nopds.Formats;
using Nopds.Formats.Parsers;
using Nopds.Infrastructure.Settings;

namespace Nopds.Tests.Conversion;

public class DocumentConverterTests
{
    private static readonly XNamespace X = "http://www.w3.org/1999/xhtml";
    private const string Png1X1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    static DocumentConverterTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Converted book: metadata read back by the EPUB parser, spine documents and TOC labels.</summary>
    private sealed record Epub(BookMetadata Meta, List<XDocument> Spine, List<string> Toc, int Images)
    {
        public string Text => string.Join("\n", Spine.Select(d => d.Root!.Value));

        public IEnumerable<XElement> All(string name) => Spine.SelectMany(d => d.Descendants(X + name));
    }

    private static Epub Convert(IBookConverter converter, byte[] input, BookMetadata? meta = null)
    {
        using var ms = new MemoryStream();
        converter.Convert(input, meta ?? new BookMetadata { Title = "Fallback title" }, ms);
        ms.Position = 0;
        var parsed = new EpubParser().Parse(ms, "out.epub", includeCover: false);

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        XDocument Load(string path)
        {
            using var s = zip.GetEntry(path)!.Open();
            return XDocument.Load(s); // Throws on malformed XHTML.
        }

        var opf = Load("OEBPS/content.opf");
        XNamespace o = "http://www.idpf.org/2007/opf";
        var manifest = opf.Descendants(o + "item").ToDictionary(i => (string)i.Attribute("id")!, i => (string)i.Attribute("href")!);
        var spine = opf.Descendants(o + "itemref").Select(r => Load("OEBPS/" + manifest[(string)r.Attribute("idref")!])).ToList();
        var toc = Load("OEBPS/nav.xhtml").Descendants(X + "a").Select(a => a.Value).ToList();
        foreach (var href in Load("OEBPS/nav.xhtml").Descendants(X + "a").Select(a => ((string)a.Attribute("href")!).Split('#')[0]))
        {
            Assert.NotNull(zip.GetEntry("OEBPS/" + href));
        }

        return new Epub(parsed, spine, toc, zip.Entries.Count(e => e.FullName.StartsWith("OEBPS/images/", StringComparison.Ordinal)));
    }

    [Fact]
    public void Txt_in_windows_1251_splits_chapters()
    {
        var text = "Глава 1\r\n\r\n    Привет, мир. Это первая глава.\r\n    Второй абзац.\r\n\r\nГлава 2\r\n\r\n    Текст второй главы.\r\n* * *\r\n    После разрыва.\r\n";
        var epub = Convert(new TxtToEpubConverter(), Encoding.GetEncoding(1251).GetBytes(text),
            new BookMetadata { Title = "Повесть", Lang = "ru", Authors = { "Иванов Иван" } });

        Assert.Equal("Повесть", epub.Meta.Title);
        Assert.Equal(["Иванов Иван"], epub.Meta.Authors);
        Assert.Equal(["Глава 1", "Глава 2"], epub.Toc);
        Assert.Contains("Привет, мир.", epub.Text);
        Assert.Single(epub.All("p"), p => (string?)p.Attribute("class") == "scene-break");
    }

    [Fact]
    public void Txt_joins_hard_wrapped_paragraphs()
    {
        var text = "The first paragraph is\nwrapped over two lines.\n\nThe second one\nalso wraps.\n";
        var epub = Convert(new TxtToEpubConverter(), Encoding.UTF8.GetBytes(text));

        var paragraphs = epub.All("p").Select(p => p.Value).ToList();
        Assert.Equal(["The first paragraph is wrapped over two lines.", "The second one also wraps."], paragraphs);
        Assert.Equal("Fallback title", epub.Meta.Title);
    }

    [Fact]
    public void Html_is_sanitized_and_split_with_working_links()
    {
        var html = $$"""
            <html lang="de"><head><meta charset="utf-8"><title>Ein Buch</title><meta name="author" content="Anna Schmidt">
            <script>alert(1)</script><style>p { color: red }</style></head>
            <body><div class="wrap"><h1>Erstes</h1><p>Siehe <a href="#later">unten</a>.<img src="data:image/png;base64,{{Png1X1}}"></p>
            <h1>Zweites</h1><p id="later" onclick="x()">Ziel <b>fett</b></p><form><input></form></div></body></html>
            """;
        var epub = Convert(new HtmlToEpubConverter(), Encoding.UTF8.GetBytes(html), new BookMetadata());

        Assert.Equal("Ein Buch", epub.Meta.Title);
        Assert.Equal(["Schmidt Anna"], epub.Meta.Authors);
        Assert.Equal("de", epub.Meta.Lang);
        Assert.Equal(["Erstes", "Zweites"], epub.Toc);
        Assert.DoesNotContain("alert", epub.Text);
        Assert.Empty(epub.All("script"));
        Assert.Single(epub.All("strong"));
        Assert.Equal(1, epub.Images);
        var link = Assert.Single(epub.All("a"));
        Assert.EndsWith(".xhtml#later", (string)link.Attribute("href")!);
        Assert.DoesNotContain(epub.Spine.SelectMany(d => d.Descendants()), e => e.Attribute("onclick") is not null);
    }

    [Fact]
    public void Docx_headings_formatting_notes_and_images()
    {
        var epub = Convert(new DocxToEpubConverter(), Docx(), new BookMetadata());

        Assert.Equal("Word Book", epub.Meta.Title);
        Assert.Equal(["Writer Jane"], epub.Meta.Authors);
        Assert.Equal("en", epub.Meta.Lang);
        Assert.Equal(["Part One", "Section A", "Part Two", "Notes"], epub.Toc);
        Assert.Contains(epub.All("strong"), e => e.Value == "bold");
        Assert.Contains(epub.All("em"), e => e.Value == "italic");
        Assert.Single(epub.All("ol"));
        Assert.Equal(2, epub.All("li").Count());
        Assert.Single(epub.All("table"));
        Assert.Equal(1, epub.Images);
        Assert.Contains(epub.All("a"), a => (string?)a.Attribute("href") == "https://example.org/");
        var noteRef = Assert.Single(epub.All("a"), a => (string?)a.Attribute("class") == "noteref");
        Assert.Contains("#fn1", (string)noteRef.Attribute("href")!);
        Assert.Contains("A footnote text.", epub.Text);
    }

    [Fact]
    public void Odt_headings_styles_and_notes()
    {
        var epub = Convert(new OdtToEpubConverter(), Odt(), new BookMetadata());

        Assert.Equal("Writer Book", epub.Meta.Title);
        Assert.Equal(["Author Olga"], epub.Meta.Authors);
        Assert.Equal(["Chapter One", "Chapter Two", "Notes"], epub.Toc);
        Assert.Contains(epub.All("strong"), e => e.Value == "strong words");
        Assert.Single(epub.All("ul"));
        Assert.Single(epub.All("a"), a => (string?)a.Attribute("class") == "noteref");
        Assert.Contains("The note body.", epub.Text);
    }

    [Fact]
    public void Rtf_code_pages_unicode_headings_tables_and_notes()
    {
        const string rtf = """
            {\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil\fcharset0 Arial;}{\f1\fnil\fcharset204 Times;}}
            {\stylesheet{\s0 Normal;}{\s1\outlinelevel0 heading 1;}}
            {\info{\title RTF Book}{\author Anna Karenina}}
            \pard\s1 Chapter One\par
            \pard\f1 \'cf\'f0\'e8\'e2\'e5\'f2\f0  and \u1103?\u1082? \b bold\b0  text{\super\chftn}{\footnote\pard {\super\chftn} Footnote here.}\par
            \pard\intbl A\cell B\cell\row
            \pard\s1 Chapter Two\par
            \pard\qc Centered {\i italic}\par
            {\*\unknowndest should be skipped}{\pict\pngblip\picw1\pich1 89504e470d0a1a0a}
            }
            """;
        var epub = Convert(new RtfToEpubConverter(), Encoding.ASCII.GetBytes(rtf), new BookMetadata());

        Assert.Equal("RTF Book", epub.Meta.Title);
        Assert.Equal(["Karenina Anna"], epub.Meta.Authors);
        Assert.Equal(["Chapter One", "Chapter Two", "Notes"], epub.Toc);
        Assert.Contains("Привет and як", epub.Text);
        Assert.Contains(epub.All("strong"), e => e.Value == "bold");
        Assert.Contains(epub.All("em"), e => e.Value == "italic");
        Assert.Contains(epub.All("p"), p => (string?)p.Attribute("class") == "center");
        Assert.Equal(2, epub.All("td").Count());
        Assert.Contains("Footnote here.", epub.Text);
        Assert.DoesNotContain("should be skipped", epub.Text);
    }

    [Fact]
    public void Office_and_html_metadata_parsers()
    {
        var parsers = BookParsers.CreateDefault();
        using (var s = new MemoryStream(Docx()))
        {
            var m = parsers.Parse(s, "x.docx");
            Assert.Equal("Word Book", m.Title);
            Assert.Equal(["Writer Jane"], m.Authors);
            Assert.Equal("2021", m.DocDate);
        }

        using (var s = new MemoryStream(Odt()))
        {
            Assert.Equal("Writer Book", parsers.Parse(s, "x.odt").Title);
        }

        using (var s = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg1251{\info{\title \'cf\'f0\'e8\'e2\'e5\'f2}{\author Lev Tolstoy}{\creatim\yr1999\mo1}}\pard Body\par}")))
        {
            var m = parsers.Parse(s, "x.rtf");
            Assert.Equal("Привет", m.Title);
            Assert.Equal(["Tolstoy Lev"], m.Authors);
            Assert.Equal("1999", m.DocDate);
        }

        using (var s = new MemoryStream(Encoding.UTF8.GetBytes("<html lang=\"pl-PL\"><head><title>Tytuł &amp; coś</title><meta content='Jan Kowalski' name='author'></head></html>")))
        {
            var m = parsers.Parse(s, "x.html");
            Assert.Equal("Tytuł & coś", m.Title);
            Assert.Equal(["Kowalski Jan"], m.Authors);
            Assert.Equal("pl", m.Lang);
        }
    }

    [Fact]
    public async Task Routes_chain_builtin_and_external_steps()
    {
        var root = Path.Combine(Path.GetTempPath(), "nopds-conv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var settings = new ConversionSettings
            {
                External = [new ExternalConverter { Source = "epub", Target = "azw3", Command = "cp {input} {output}" }],
            };
            var service = new ConversionService(root, () => settings, NullLogger<ConversionService>.Instance);

            Assert.Equal(["epub", "azw3"], service.TargetsFor("docx"));
            Assert.Equal(["epub", "azw3"], service.TargetsFor("fb2"));
            Assert.Equal(["azw3"], service.TargetsFor("epub"));
            Assert.Empty(service.TargetsFor("pdf"));
            Assert.Equal(["docx", "fb2", "htm", "html", "odt", "rtf", "txt", "xhtml"], service.SourcesFor("epub"));

            await File.WriteAllBytesAsync(Path.Combine(root, "book.docx"), Docx());
            var library = new Library { Name = "t", RootPath = root };
            var book = new Book { Id = 1, Library = library, RelPath = "book.docx", FileName = "book.docx", Format = "docx", Title = "Catalog title", SearchTitle = "catalog title", FileSize = 1 };

            // DOCX → EPUB (built-in) → AZW3 (external; "cp" keeps the EPUB bytes, which is enough to prove the chain).
            var result = await service.ConvertAsync(library, book, "azw3");
            Assert.NotNull(result);
            await using (var fs = File.OpenRead(result.Path))
            {
                Assert.Equal("Catalog title", new EpubParser().Parse(fs, "x.epub", false).Title);
            }

            settings = settings with { BuiltIn = false };
            Assert.Empty(service.TargetsFor("docx"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Zip(Dictionary<string, object> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(content is string text ? Encoding.UTF8.GetBytes(text) : (byte[])content);
            }
        }

        return ms.ToArray();
    }

    private static byte[] Docx()
    {
        const string w = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"";
        return Zip(new()
        {
            ["[Content_Types].xml"] = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>",
            ["docProps/core.xml"] = """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/">
                  <dc:title>Word Book</dc:title><dc:creator>Jane Writer</dc:creator><dc:language>en-GB</dc:language><dcterms:created>2021-04-01T10:00:00Z</dcterms:created>
                </cp:coreProperties>
                """,
            ["word/_rels/document.xml.rels"] = """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.org/" TargetMode="External"/>
                </Relationships>
                """,
            ["word/styles.xml"] = $"""
                <w:styles {w}>
                  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/></w:style>
                </w:styles>
                """,
            ["word/numbering.xml"] = $"""
                <w:numbering {w}>
                  <w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/></w:lvl></w:abstractNum>
                  <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
                </w:numbering>
                """,
            ["word/footnotes.xml"] = $"""
                <w:footnotes {w}>
                  <w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>
                  <w:footnote w:id="1"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space="preserve"> A footnote text.</w:t></w:r></w:p></w:footnote>
                </w:footnotes>
                """,
            ["word/document.xml"] = $"""
                <w:document {w}><w:body>
                  <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Part One</w:t></w:r></w:p>
                  <w:p><w:r><w:t xml:space="preserve">Some </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r><w:r><w:t xml:space="preserve"> and </w:t></w:r><w:r><w:rPr><w:i w:val="1"/><w:b w:val="0"/></w:rPr><w:t>italic</w:t></w:r><w:r><w:rPr><w:vertAlign w:val="superscript"/></w:rPr><w:footnoteReference w:id="1"/></w:r></w:p>
                  <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Section A</w:t></w:r></w:p>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>First item</w:t></w:r></w:p>
                  <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Second item</w:t></w:r></w:p>
                  <w:p><w:hyperlink r:id="rId2"><w:r><w:t>a link</w:t></w:r></w:hyperlink></w:p>
                  <w:p><w:r><w:drawing><wp:inline><wp:docPr id="1" name="Picture" descr="A dot"/><a:graphic><a:graphicData><a:blip r:embed="rId1"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
                  <w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell 1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>cell 2</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                  <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Part Two</w:t></w:r></w:p>
                  <w:p><w:r><w:t>The end.</w:t></w:r></w:p>
                </w:body></w:document>
                """,
            ["word/media/image1.png"] = System.Convert.FromBase64String(Png1X1),
        });
    }

    private static byte[] Odt()
    {
        const string ns = "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\"";
        return Zip(new()
        {
            ["mimetype"] = "application/vnd.oasis.opendocument.text",
            ["meta.xml"] = $"<office:document-meta {ns}><office:meta><dc:title>Writer Book</dc:title><meta:initial-creator>Olga Author</meta:initial-creator></office:meta></office:document-meta>",
            ["content.xml"] = $"""
                <office:document-content {ns}>
                  <office:automatic-styles><style:style style:name="T1" style:family="text"><style:text-properties fo:font-weight="bold"/></style:style></office:automatic-styles>
                  <office:body><office:text>
                    <text:h text:outline-level="1">Chapter One</text:h>
                    <text:p>Some <text:span text:style-name="T1">strong words</text:span>.<text:note text:id="n1" text:note-class="footnote"><text:note-citation>1</text:note-citation><text:note-body><text:p>The note body.</text:p></text:note-body></text:note></text:p>
                    <text:list><text:list-item><text:p>Item</text:p></text:list-item></text:list>
                    <text:h text:outline-level="1">Chapter Two</text:h>
                    <text:p>More<text:s text:c="2"/>text.</text:p>
                  </office:text></office:body>
                </office:document-content>
                """,
        });
    }
}
