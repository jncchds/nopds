using Nopds.Domain.Entities;

namespace Nopds.Infrastructure.Browse;

/// <summary>Visibility of the catalog for the current caller.</summary>
/// <param name="IsAdmin">Admins also see other users' private uploads.</param>
public sealed record Scope(int[]? AllowedLibraries, int? LibraryId, bool HideDuplicates, string[] PreferredFormats, string Lang, Guid? UserId = null, bool IsAdmin = false)
{
    public Scope WithLibrary(int? libraryId) => this with { LibraryId = libraryId };
}

public enum TextMatch
{
    Contains,
    Begins,
    Exact,
}

public enum BookSort
{
    Title,
    Added,
    SeriesNumber,
    Shelf,
}

public sealed record BookQuery
{
    public string? Text { get; init; }
    public TextMatch Match { get; init; } = TextMatch.Contains;
    public LangCode? Lang { get; init; }
    public long? AuthorId { get; init; }
    public long? SeriesId { get; init; }

    /// <summary>Only books that belong to no series (used with <see cref="AuthorId"/>).</summary>
    public bool WithoutSeries { get; init; }

    public int? GenreId { get; init; }
    public string? GenreSection { get; init; }
    public long? CatalogId { get; init; }

    /// <summary>List all editions (duplicate group) of this book.</summary>
    public long? EditionsOf { get; init; }

    /// <summary>Books on this user's shelf (opened or downloaded).</summary>
    public Guid? ShelfOf { get; init; }

    /// <summary>Filter by the book's declared language (e.g. "uk").</summary>
    public string? BookLanguage { get; init; }

    public string? Format { get; init; }
    public BookSort Sort { get; init; } = BookSort.Title;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 60;
    public bool CountTotal { get; init; }
}

public sealed record NameQuery
{
    public string? Text { get; init; }
    public TextMatch Match { get; init; } = TextMatch.Begins;
    public LangCode? Lang { get; init; }

    /// <summary>For series: only series that contain books by this author.</summary>
    public long? AuthorId { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 60;
    public bool CountTotal { get; init; }
}

public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, bool HasNext, long? Total);

public sealed record AuthorRef(long Id, string Name);

public sealed record SeriesInfo(long Id, string Name, int Number);

public sealed record GenreRef(int Id, string Code, string Name, string Section, string SectionName);

public sealed record BookSummary(
    long Id,
    int LibraryId,
    string Title,
    string FileName,
    string Format,
    long FileSize,
    string? Lang,
    string? DocDate,
    DateTimeOffset RegisteredAt,
    string? Annotation,
    CoverState Cover,
    IReadOnlyList<AuthorRef> Authors,
    IReadOnlyList<SeriesInfo> Series,
    IReadOnlyList<GenreRef> Genres,
    int Editions,
    UploadInfo? Upload = null);

/// <summary>Set for books that came from a user upload.</summary>
/// <param name="Mine">The caller uploaded it (and may change its privacy).</param>
public sealed record UploadInfo(long Id, bool IsPrivate, bool Mine, string? UploadedBy, DateTimeOffset UploadedAt);

public sealed record NamedCount(long Id, string Name, int Books);

public sealed record AlphabetGroup(string Prefix, int Count);

public sealed record GenreSectionInfo(string Key, string Name, int Books);

public sealed record GenreInfo(int Id, string Code, string Name, string Section, int Books);

public sealed record LibraryInfo(int Id, string Name, int Books, DateTimeOffset? LastScanFinishedAt, bool IsUploads = false);

public sealed record CatalogNode(long Id, string Name, string Path, CatalogType Type, int? LibraryId);

public sealed record CatalogListing(CatalogNode? Current, IReadOnlyList<CatalogNode> Breadcrumbs, IReadOnlyList<CatalogNode> Children, Page<BookSummary> Books);

public sealed record Stats(long Books, long Authors, long Series, long Genres, long Libraries, DateTimeOffset? LastScan);
