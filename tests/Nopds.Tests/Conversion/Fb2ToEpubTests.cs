using System.IO.Compression;
using System.Xml.Linq;
using Nopds.Conversion;
using Nopds.Formats.Parsers;

namespace Nopds.Tests.Conversion;

public class Fb2ToEpubTests
{
    [Fact]
    public void Converts_fb2_to_valid_epub_structure()
    {
        var fb2 = File.ReadAllBytes(TestFiles.PathOf("262001.fb2"));
        using var ms = new MemoryStream();
        new Fb2ToEpubConverter().Convert(fb2, ms);

        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal("mimetype", zip.Entries[0].FullName);
            Assert.NotNull(zip.GetEntry("OEBPS/nav.xhtml"));
            Assert.NotNull(zip.GetEntry("OEBPS/toc.ncx"));
            var chapters = zip.Entries.Where(e => e.FullName.StartsWith("OEBPS/text/ch", StringComparison.Ordinal)).ToList();
            Assert.True(chapters.Count > 1);

            // Every XHTML file must be well-formed.
            foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal)))
            {
                using var s = e.Open();
                XDocument.Load(s);
            }
        }

        ms.Position = 0;
        var meta = new EpubParser().Parse(ms, "out.epub", includeCover: true);
        Assert.Equal("The Sanctuary Sparrow", meta.Title);
        Assert.Equal(["Peters Ellis"], meta.Authors);
        Assert.Equal("en", meta.Lang);
        Assert.NotNull(meta.Cover);
    }
}
