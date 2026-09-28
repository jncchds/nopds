using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Nopds.Formats.Parsers;

/// <summary>OpenDocument text (ODT): meta.xml properties, cover from the package thumbnail.</summary>
public sealed class OdtParser : IBookParser
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    public IReadOnlyCollection<string> Formats { get; } = ["odt"];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.GetEntry("content.xml") is null)
        {
            throw new FormatException("Not an OpenDocument package.");
        }

        var meta = ReadMeta(zip);
        if (string.IsNullOrWhiteSpace(meta.Title))
        {
            meta.Title = DocumentMeta.TitleFromFileName(fileName);
        }

        if (includeCover)
        {
            meta.Cover = DocumentMeta.ReadImage(zip.GetEntry("Thumbnails/thumbnail.png"));
        }

        return meta;
    }

    public static BookMetadata ReadMeta(ZipArchive zip)
    {
        var meta = new BookMetadata();
        var entry = zip.GetEntry("meta.xml");
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

        var m = doc.Root?.Element(Office + "meta");
        meta.Title = DocumentMeta.NullIfBlank(m?.Element(Dc + "title")?.Value);
        meta.Lang = DocumentMeta.Language(m?.Element(Dc + "language")?.Value);
        meta.Annotation = DocumentMeta.NullIfBlank(m?.Element(Dc + "description")?.Value);
        meta.DocDate = DocumentMeta.Year(m?.Element(Meta + "creation-date")?.Value);
        DocumentMeta.AddAuthors(meta, m?.Element(Meta + "initial-creator")?.Value ?? m?.Element(Dc + "creator")?.Value);
        return meta;
    }
}
