using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Nopds.Formats.Parsers;

/// <summary>Word DOCX: Dublin Core properties from docProps/core.xml, cover from the package thumbnail.</summary>
public sealed class DocxParser : IBookParser
{
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DcTerms = "http://purl.org/dc/terms/";

    public IReadOnlyCollection<string> Formats { get; } = ["docx"];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.GetEntry("word/document.xml") is null)
        {
            throw new FormatException("Not a DOCX package.");
        }

        var meta = ReadCore(zip);
        if (string.IsNullOrWhiteSpace(meta.Title))
        {
            meta.Title = DocumentMeta.TitleFromFileName(fileName);
        }

        if (includeCover)
        {
            var thumb = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("docProps/thumbnail", StringComparison.OrdinalIgnoreCase));
            meta.Cover = DocumentMeta.ReadImage(thumb);
        }

        return meta;
    }

    public static BookMetadata ReadCore(ZipArchive zip)
    {
        var meta = new BookMetadata();
        var entry = zip.GetEntry("docProps/core.xml");
        if (entry is null)
        {
            return meta;
        }

        XDocument doc;
        using (var s = entry.Open())
        {
            using var reader = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }

        var root = doc.Root;
        meta.Title = DocumentMeta.NullIfBlank(root?.Element(Dc + "title")?.Value);
        meta.Lang = DocumentMeta.Language(root?.Element(Dc + "language")?.Value);
        meta.Annotation = DocumentMeta.NullIfBlank(root?.Element(Dc + "description")?.Value);
        meta.DocDate = DocumentMeta.Year(root?.Element(DcTerms + "created")?.Value);
        DocumentMeta.AddAuthors(meta, root?.Element(Dc + "creator")?.Value);
        return meta;
    }
}
