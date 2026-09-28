using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Nopds.Domain.Text;

namespace Nopds.Formats.Parsers;

/// <summary>EPUB 2/3 metadata reader (OPF via META-INF/container.xml).</summary>
public sealed class EpubParser : IBookParser
{
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    public IReadOnlyCollection<string> Formats { get; } = ["epub", "kepub"];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var opfPath = FindOpf(zip) ?? throw new FormatException("EPUB without OPF package.");
        var opfEntry = zip.GetEntry(opfPath) ?? throw new FormatException("OPF entry missing.");
        var opf = Load(opfEntry);
        var metadata = opf.Root?.Element(Opf + "metadata") ?? opf.Root?.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata")
                       ?? throw new FormatException("OPF without metadata.");

        var meta = new BookMetadata
        {
            Title = TextNormalizer.Strip(metadata.Elements(Dc + "title").FirstOrDefault()?.Value),
            Lang = TextNormalizer.Strip(metadata.Elements(Dc + "language").FirstOrDefault()?.Value),
            DocDate = TextNormalizer.Strip(metadata.Elements(Dc + "date").FirstOrDefault()?.Value),
            Annotation = HtmlText.ToPlain(metadata.Elements(Dc + "description").FirstOrDefault()?.Value),
        };

        foreach (var creator in metadata.Elements(Dc + "creator"))
        {
            var role = creator.Attribute(Opf + "role")?.Value ?? RefinedRole(metadata, creator);
            if (role is not null && role != "aut")
            {
                continue;
            }

            var name = AuthorName(creator, metadata);
            if (name.Length > 0 && !meta.Authors.Contains(name))
            {
                meta.Authors.Add(name);
            }
        }

        foreach (var subject in metadata.Elements(Dc + "subject"))
        {
            var g = TextNormalizer.Strip(subject.Value).ToLowerInvariant();
            if (g.Length is > 0 and <= 64 && !meta.Genres.Contains(g))
            {
                meta.Genres.Add(g);
            }
        }

        ReadSeries(metadata, meta);

        if (includeCover)
        {
            meta.Cover = ReadCover(zip, opf, opfPath);
        }

        return meta;
    }

    public static string? FindOpf(ZipArchive zip)
    {
        var container = zip.GetEntry("META-INF/container.xml");
        if (container is not null)
        {
            var doc = Load(container);
            var path = doc.Descendants(Container + "rootfile").Select(e => e.Attribute("full-path")?.Value).FirstOrDefault(p => p is not null)
                       ?? doc.Descendants().Where(e => e.Name.LocalName == "rootfile").Select(e => e.Attribute("full-path")?.Value).FirstOrDefault();
            if (path is not null)
            {
                return path;
            }
        }

        return zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))?.FullName;
    }

    internal static XDocument Load(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var reader = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string? RefinedRole(XElement metadata, XElement creator)
    {
        var id = creator.Attribute("id")?.Value;
        if (id is null)
        {
            return null;
        }

        return metadata.Elements(Opf + "meta")
            .FirstOrDefault(m => m.Attribute("refines")?.Value == "#" + id && m.Attribute("property")?.Value == "role")?.Value;
    }

    private static string AuthorName(XElement creator, XElement metadata)
    {
        // Prefer "file-as" (Last, First) when present; it matches the catalog's "Last First" ordering.
        var fileAs = creator.Attribute(Opf + "file-as")?.Value;
        var id = creator.Attribute("id")?.Value;
        if (fileAs is null && id is not null)
        {
            fileAs = metadata.Elements(Opf + "meta")
                .FirstOrDefault(m => m.Attribute("refines")?.Value == "#" + id && m.Attribute("property")?.Value == "file-as")?.Value;
        }

        if (!string.IsNullOrWhiteSpace(fileAs))
        {
            return TextNormalizer.Strip(fileAs.Replace(',', ' ').Replace("  ", " "));
        }

        var parts = TextNormalizer.Strip(creator.Value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0],
            _ => parts[^1] + " " + string.Join(' ', parts[..^1]),
        };
    }

    private static void ReadSeries(XElement metadata, BookMetadata meta)
    {
        var metas = metadata.Elements(Opf + "meta").ToList();

        // Calibre convention (EPUB 2).
        var calibreSeries = metas.FirstOrDefault(m => m.Attribute("name")?.Value == "calibre:series")?.Attribute("content")?.Value;
        if (!string.IsNullOrWhiteSpace(calibreSeries))
        {
            var index = metas.FirstOrDefault(m => m.Attribute("name")?.Value == "calibre:series_index")?.Attribute("content")?.Value;
            meta.Series.Add(new SeriesRef(TextNormalizer.Strip(calibreSeries), ParseIndex(index)));
            return;
        }

        // EPUB 3 belongs-to-collection.
        foreach (var coll in metas.Where(m => m.Attribute("property")?.Value == "belongs-to-collection"))
        {
            var id = coll.Attribute("id")?.Value;
            var pos = id is null
                ? null
                : metas.FirstOrDefault(m => m.Attribute("refines")?.Value == "#" + id && m.Attribute("property")?.Value == "group-position")?.Value;
            var name = TextNormalizer.Strip(coll.Value);
            if (name.Length > 0)
            {
                meta.Series.Add(new SeriesRef(name, ParseIndex(pos)));
            }
        }
    }

    private static int ParseIndex(string? s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (int)d : 0;

    private static CoverImage? ReadCover(ZipArchive zip, XDocument opf, string opfPath)
    {
        var manifest = opf.Root?.Element(Opf + "manifest")?.Elements(Opf + "item").ToList() ?? [];
        var metadata = opf.Root?.Element(Opf + "metadata");

        XElement? item = manifest.FirstOrDefault(i => (i.Attribute("properties")?.Value ?? "").Split(' ').Contains("cover-image"));
        if (item is null)
        {
            var coverId = metadata?.Elements(Opf + "meta").FirstOrDefault(m => m.Attribute("name")?.Value == "cover")?.Attribute("content")?.Value;
            if (coverId is not null)
            {
                item = manifest.FirstOrDefault(i => i.Attribute("id")?.Value == coverId);
            }
        }

        item ??= manifest.FirstOrDefault(i =>
            (i.Attribute("media-type")?.Value ?? "").StartsWith("image/", StringComparison.Ordinal)
            && ((i.Attribute("id")?.Value ?? "").Contains("cover", StringComparison.OrdinalIgnoreCase)
                || (i.Attribute("href")?.Value ?? "").Contains("cover", StringComparison.OrdinalIgnoreCase)));

        var href = item?.Attribute("href")?.Value;
        if (href is null)
        {
            return null;
        }

        var entry = zip.GetEntry(ResolvePath(opfPath, href));
        if (entry is null)
        {
            return null;
        }

        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return new CoverImage(ms.ToArray(), item!.Attribute("media-type")?.Value ?? MediaTypes.ForImage(href));
    }

    public static string ResolvePath(string basePath, string href)
    {
        href = Uri.UnescapeDataString(href.Split('#')[0]);
        var dir = Path.GetDirectoryName(basePath)?.Replace('\\', '/') ?? string.Empty;
        var parts = new List<string>(dir.Length == 0 ? [] : dir.Split('/'));
        foreach (var seg in href.Split('/'))
        {
            if (seg == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (seg is not ("." or ""))
            {
                parts.Add(seg);
            }
        }

        return string.Join('/', parts);
    }
}
