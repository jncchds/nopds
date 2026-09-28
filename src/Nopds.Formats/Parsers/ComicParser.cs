using System.IO.Compression;

namespace Nopds.Formats.Parsers;

/// <summary>CBZ comics: title from file name, cover = first image in name order.</summary>
public sealed class ComicParser : IBookParser
{
    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    public IReadOnlyCollection<string> Formats { get; } = ["cbz"];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover)
    {
        var meta = new BookMetadata { Title = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim() };
        if (!includeCover)
        {
            return meta;
        }

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var first = Pages(zip).FirstOrDefault();
        if (first is not null)
        {
            using var s = first.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            meta.Cover = new CoverImage(ms.ToArray(), MediaTypes.ForImage(first.Name));
        }

        return meta;
    }

    public static IEnumerable<ZipArchiveEntry> Pages(ZipArchive zip) =>
        zip.Entries
            .Where(e => ImageExt.Contains(Path.GetExtension(e.Name).ToLowerInvariant()) && !e.FullName.StartsWith("__MACOSX", StringComparison.Ordinal))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);
}
