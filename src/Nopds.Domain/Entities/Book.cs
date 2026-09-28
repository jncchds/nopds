namespace Nopds.Domain.Entities;

public class Book
{
    public long Id { get; set; }
    public int LibraryId { get; set; }
    public Library? Library { get; set; }
    public long CatalogId { get; set; }
    public Catalog? Catalog { get; set; }

    public BookContainer Container { get; set; }

    /// <summary>Library-relative path of the physical file (the book itself, or the archive that contains it).</summary>
    public required string RelPath { get; set; }

    /// <summary>Entry name inside the archive, when <see cref="Container"/> is not <see cref="BookContainer.File"/>.</summary>
    public string? EntryName { get; set; }

    /// <summary>Book file name (without directories).</summary>
    public required string FileName { get; set; }

    public required string Format { get; set; }
    public long FileSize { get; set; }
    public DateTimeOffset? FileMtime { get; set; }
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;

    public required string Title { get; set; }

    /// <summary>Normalized title used for sorting and prefix/substring search.</summary>
    public required string SearchTitle { get; set; }

    public string? Annotation { get; set; }
    public string? DocDate { get; set; }
    public string? Lang { get; set; }
    public LangCode LangCode { get; set; } = LangCode.Other;

    /// <summary>Hash of normalized title + authors; editions of one work share it.</summary>
    public long DupGroupKey { get; set; }

    /// <summary>xxHash64 of the file content (only when the library enables content hashing).</summary>
    public long? ContentHash { get; set; }

    /// <summary>KOReader partial MD5 document hash, used to match sync progress to books.</summary>
    public string? KoreaderHash { get; set; }

    public CoverState Cover { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public List<BookAuthor> Authors { get; set; } = [];
    public List<BookGenre> Genres { get; set; } = [];
    public List<BookSeries> Series { get; set; } = [];
}

public class Author
{
    public long Id { get; set; }
    public required string FullName { get; set; }
    public required string SearchName { get; set; }
    public LangCode LangCode { get; set; } = LangCode.Other;
    public List<BookAuthor> Books { get; set; } = [];
}

public class BookAuthor
{
    public long BookId { get; set; }
    public Book? Book { get; set; }
    public long AuthorId { get; set; }
    public Author? Author { get; set; }
    public short Position { get; set; }
}

public class Genre
{
    public int Id { get; set; }

    /// <summary>FB2 genre code, e.g. "sf_fantasy".</summary>
    public required string Code { get; set; }

    /// <summary>Section key (see genre localization), "unknown" for codes outside the catalog.</summary>
    public required string Section { get; set; }

    public List<BookGenre> Books { get; set; } = [];
}

public class BookGenre
{
    public long BookId { get; set; }
    public Book? Book { get; set; }
    public int GenreId { get; set; }
    public Genre? Genre { get; set; }
}

public class Series
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string SearchName { get; set; }
    public LangCode LangCode { get; set; } = LangCode.Other;
    public List<BookSeries> Books { get; set; } = [];
}

public class BookSeries
{
    public long BookId { get; set; }
    public Book? Book { get; set; }
    public long SeriesId { get; set; }
    public Series? Series { get; set; }
    public int SerNo { get; set; }
}
