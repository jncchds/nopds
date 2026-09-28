namespace Nopds.Formats;

public sealed record SeriesRef(string Name, int Number);

public sealed record CoverImage(byte[] Data, string MediaType);

/// <summary>Metadata extracted from a book file.</summary>
public sealed class BookMetadata
{
    public string? Title { get; set; }

    /// <summary>Author names in "Last First Middle" order.</summary>
    public List<string> Authors { get; } = [];

    /// <summary>Genre codes (FB2 codes or EPUB/MOBI subjects, lowercased).</summary>
    public List<string> Genres { get; } = [];

    public List<SeriesRef> Series { get; } = [];
    public string? Lang { get; set; }
    public string? DocDate { get; set; }
    public string? Annotation { get; set; }
    public CoverImage? Cover { get; set; }
}
