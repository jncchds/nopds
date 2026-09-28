using System.IO.Compression;
using Nopds.Formats;
using Nopds.Formats.Parsers;

namespace Nopds.Tests.Formats;

public class ParserTests
{
    private readonly BookParsers _parsers = BookParsers.CreateDefault();

    [Fact]
    public void Fb2_metadata_and_cover()
    {
        using var s = TestFiles.Open("262001.fb2");
        var m = _parsers.Parse(s, "262001.fb2", includeCover: true);

        Assert.Equal("The Sanctuary Sparrow", m.Title);
        Assert.Equal(["Peters Ellis"], m.Authors);
        Assert.Equal(["antique"], m.Genres);
        Assert.Equal("en", m.Lang);
        Assert.Equal("30.1.2011", m.DocDate);
        Assert.NotNull(m.Cover);
        // SimpleOPDS asserted 76207 = base64 length; decoded JPEG is smaller.
        Assert.Equal(56360, m.Cover!.Data.Length);
        Assert.Equal(0xFF, m.Cover.Data[0]);
        Assert.Equal("image/jpeg", m.Cover.MediaType);
    }

    [Fact]
    public void Fb2_inside_zip()
    {
        using var zip = ZipFile.OpenRead(TestFiles.PathOf("books.zip"));
        var entry = zip.GetEntry("539603.fb2")!;
        using var ms = new MemoryStream();
        using (var es = entry.Open())
        {
            es.CopyTo(ms);
        }

        ms.Position = 0;
        var m = _parsers.Parse(ms, entry.Name);
        Assert.Equal("Любовь в жизни Обломова", m.Title);
        Assert.Equal(["Логинов Святослав"], m.Authors);
        Assert.Equal("2014-09-15", m.DocDate);
    }

    [Fact]
    public void Broken_fb2_throws()
    {
        using var s = TestFiles.Open("badfile.fb2");
        Assert.ThrowsAny<Exception>(() => _parsers.Parse(s, "badfile.fb2"));
    }

    [Fact]
    public void Fb2_with_html_entities_and_broken_body_is_tolerated()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0" xmlns:l="http://www.w3.org/1999/xlink">
            <description><title-info><genre>sf_fantasy</genre>
            <author><first-name>Иван</first-name><last-name>Петров</last-name></author>
            <book-title>Тест&nbsp;книга &amp; Co</book-title>
            <annotation><p>Первый</p><p>Второй &laquo;абзац&raquo;</p></annotation>
            <sequence name="Цикл" number="3"/>
            <lang>ru</lang></title-info></description>
            <body><section><p>unclosed <b>tags</section></body></FictionBook>
            """;
        var m = new Fb2Parser().ParseText(xml, includeCover: false);
        Assert.Equal("Тест книга & Co", m.Title);
        Assert.Equal(["Петров Иван"], m.Authors);
        Assert.Equal("Первый\nВторой «абзац»", m.Annotation);
        Assert.Equal([new SeriesRef("Цикл", 3)], m.Series);
    }

    [Fact]
    public void Epub_metadata()
    {
        using var s = TestFiles.Open("mirer.epub");
        var m = _parsers.Parse(s, "mirer.epub", includeCover: true);

        Assert.Equal("У меня девять жизней (шф (продолжатели))", m.Title);
        Assert.Equal(["Мирер Александр"], m.Authors);
        Assert.Equal(["sf"], m.Genres);
        Assert.Equal("ru", m.Lang);
        Assert.StartsWith("2015", m.DocDate);
        Assert.Equal("Собрание произведений. Том 2", m.Annotation);
    }

    [Fact]
    public void Mobi_metadata_and_cover()
    {
        using var s = TestFiles.Open("robin_cook.mobi");
        var m = _parsers.Parse(s, "robin_cook.mobi", includeCover: true);

        Assert.Equal("Vector", m.Title);
        Assert.Equal(["Cook Robin"], m.Authors);
        Assert.NotNull(m.DocDate);
        Assert.NotNull(m.Cover);
        Assert.True(m.Cover!.Data.Length > 1000);
    }
}
