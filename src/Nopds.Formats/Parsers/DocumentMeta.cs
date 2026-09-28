using System.IO.Compression;
using Nopds.Domain.Text;

namespace Nopds.Formats.Parsers;

/// <summary>Shared helpers for office-document metadata (DOCX, ODT, RTF, HTML).</summary>
public static class DocumentMeta
{
    public static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string TitleFromFileName(string fileName) => Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();

    /// <summary>"en-US" → "en".</summary>
    public static string? Language(string? tag) =>
        NullIfBlank(tag) is { } t ? t.Split('-', '_')[0].ToLowerInvariant() : null;

    /// <summary>Leading four-digit year of an ISO date.</summary>
    public static string? Year(string? date) =>
        date is { Length: >= 4 } d && int.TryParse(d.AsSpan(0, 4), out var y) && y is > 1000 and < 3000 ? d[..4] : null;

    /// <summary>
    /// Adds "First Last" names (several separated by ";" or ",") in the catalog's "Last First" order.
    /// Values that look like "Last, First" are kept as one name.
    /// </summary>
    public static void AddAuthors(BookMetadata meta, string? creators)
    {
        if (NullIfBlank(creators) is not { } value)
        {
            return;
        }

        var names = value.Contains(';') ? value.Split(';') : [value];
        foreach (var raw in names)
        {
            var name = TextNormalizer.Strip(raw);
            if (name.Length == 0)
            {
                continue;
            }

            string lastFirst;
            if (name.Count(c => c == ',') == 1)
            {
                lastFirst = TextNormalizer.Strip(name.Replace(",", " ").Replace("  ", " "));
            }
            else
            {
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                lastFirst = parts.Length > 1 ? parts[^1] + " " + string.Join(' ', parts[..^1]) : name;
            }

            if (!meta.Authors.Contains(lastFirst))
            {
                meta.Authors.Add(lastFirst);
            }
        }
    }

    public static CoverImage? ReadImage(ZipArchiveEntry? entry)
    {
        if (entry is null || entry.Length is 0 or > 16 * 1024 * 1024)
        {
            return null;
        }

        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return new CoverImage(ms.ToArray(), MediaTypes.ForImage(entry.Name));
    }
}
