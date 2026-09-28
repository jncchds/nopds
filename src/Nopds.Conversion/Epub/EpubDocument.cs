using System.Xml.Linq;
using Nopds.Formats;

namespace Nopds.Conversion.Epub;

/// <summary>One XHTML file in the spine. <see cref="File"/> is relative to OEBPS (e.g. "text/ch001.xhtml").</summary>
public sealed class EpubChapter(string file, string title)
{
    public string File { get; } = file;
    public string Title { get; } = title;

    /// <summary>Body content; converters emit XHTML elements in <see cref="Xhtml.Ns"/>.</summary>
    public XElement Body { get; set; } = new(Xhtml.Ns + "section");

    /// <summary>Hidden from the table of contents (e.g. continuation files of a long text).</summary>
    public bool InToc { get; set; } = true;

    /// <summary>Nested table-of-contents entries; hrefs are relative to OEBPS ("text/ch001.xhtml#h2").</summary>
    public List<(string Title, string Href)> Children { get; } = [];
}

public sealed record EpubImage(string File, string MediaType, byte[] Data);

/// <summary>Everything needed to package an EPUB 3 (with an EPUB 2 NCX for older readers).</summary>
public sealed class EpubDocument
{
    public BookMetadata Meta { get; init; } = new();
    public List<EpubChapter> Chapters { get; } = [];
    public List<EpubImage> Images { get; } = [];

    /// <summary>Image file (one of <see cref="Images"/>) used as the cover page and cover-image.</summary>
    public string? CoverFile { get; set; }

    private int _imageNo;
    private readonly Dictionary<string, string> _imageKeys = new(StringComparer.Ordinal);

    /// <summary>Adds an image once per <paramref name="key"/> and returns its href relative to a chapter file.</summary>
    public string? AddImage(string key, byte[] data, string? mediaType = null)
        => AddImageFile(key, data, mediaType) is { } file ? "../" + file : null;

    /// <summary>Like <see cref="AddImage"/> but returns the OEBPS-relative file (for <see cref="CoverFile"/>).</summary>
    public string? AddImageFile(string key, byte[] data, string? mediaType = null)
    {
        if (_imageKeys.TryGetValue(key, out var existing))
        {
            return existing;
        }

        mediaType = SniffImage(data) ?? (mediaType == "image/svg+xml" ? mediaType : null);
        if (mediaType is null)
        {
            return null;
        }

        var ext = mediaType switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => ".jpg",
        };
        var file = "images/img" + (++_imageNo).ToString("D4") + ext;
        Images.Add(new EpubImage(file, mediaType, data));
        _imageKeys[key] = file;
        return file;
    }

    /// <summary>Recognizes the raster formats every EPUB reader supports; anything else (EMF, WMF, TIFF) is skipped.</summary>
    public static string? SniffImage(byte[] d) => d switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, ..] => "image/jpeg",
        [0x47, 0x49, 0x46, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}
