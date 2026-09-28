using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Nopds.Domain.Entities;
using Nopds.Domain.Text;
using Nopds.Formats;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Genres;

namespace Nopds.Scanner;

/// <summary>A parsed (or INPX-described) book ready to be stored.</summary>
public sealed record BookRecord(
    long? ExistingId,
    BookContainer Container,
    string RelPath,
    string? EntryName,
    string FileName,
    string Format,
    long FileSize,
    DateTimeOffset? FileMtime,
    string CatalogPath,
    CatalogType CatalogType,
    BookMetadata Meta,
    string? KoreaderHash,
    long? ContentHash);

/// <summary>
/// Single-writer persistence for one scan: creates catalogs, upserts authors/series/genres with
/// INSERT … ON CONFLICT (safe when several libraries scan concurrently) and stores books in batches.
/// </summary>
internal sealed class ScanWriter(IDbContextFactory<NopdsDbContext> dbFactory, GenreCatalog genreCatalog, int libraryId)
{
    private sealed record CatalogInfo(long Id, CatalogType Type, long Size, DateTimeOffset? Mtime);

    private readonly Dictionary<string, CatalogInfo> _catalogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _authors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _series = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _genres = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, long> CatalogIds => _catalogs.ToDictionary(kv => kv.Key, kv => kv.Value.Id);

    public async Task LoadAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var c in await db.Catalogs.AsNoTracking().Where(c => c.LibraryId == libraryId)
                     .Select(c => new { c.Path, c.Id, c.Type, c.Size, c.Mtime }).ToListAsync(ct))
        {
            _catalogs[c.Path] = new CatalogInfo(c.Id, c.Type, c.Size, c.Mtime);
        }

        foreach (var g in await db.Genres.AsNoTracking().Select(g => new { g.Code, g.Id }).ToListAsync(ct))
        {
            _genres[g.Code] = g.Id;
        }
    }

    /// <summary>Returns true when the catalog exists with the same size and mtime (unchanged archive/index).</summary>
    public bool IsUnchanged(string path, long size, DateTimeOffset? mtime) =>
        _catalogs.TryGetValue(path, out var c) && c.Size == size && (mtime is null || c.Mtime is null || Math.Abs((c.Mtime.Value - mtime.Value).TotalSeconds) < 2);

    public bool HasCatalog(string path) => _catalogs.ContainsKey(path);

    public async Task<long> EnsureCatalogAsync(string path, CatalogType type, long size = 0, DateTimeOffset? mtime = null, CancellationToken ct = default)
    {
        if (_catalogs.TryGetValue(path, out var existing))
        {
            if (type != CatalogType.Directory && (existing.Size != size || existing.Mtime != mtime))
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await db.Catalogs.Where(c => c.Id == existing.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Size, size).SetProperty(c => c.Mtime, mtime).SetProperty(c => c.Type, type), ct);
                _catalogs[path] = existing with { Size = size, Mtime = mtime, Type = type };
            }

            return existing.Id;
        }

        long? parentId = null;
        if (path != ".")
        {
            var slash = path.LastIndexOf('/');
            var parentPath = slash < 0 ? "." : path[..slash];
            var parentType = _catalogs.TryGetValue(parentPath, out var p) ? p.Type : CatalogType.Directory;
            parentId = await EnsureCatalogAsync(parentPath, parentType, ct: ct);
        }

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var name = path == "." ? "." : path[(path.LastIndexOf('/') + 1)..];
            var catalog = new Catalog
            {
                LibraryId = libraryId,
                ParentId = parentId,
                Name = TextNormalizer.Truncate(name, 512),
                Path = path,
                Type = type,
                Size = size,
                Mtime = mtime,
            };
            db.Catalogs.Add(catalog);
            await db.SaveChangesAsync(ct);
            _catalogs[path] = new CatalogInfo(catalog.Id, type, size, mtime);
            return catalog.Id;
        }
    }

    public async Task WriteAsync(IReadOnlyList<BookRecord> batch, ScanProgress progress, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var catalogIds = new Dictionary<string, long>();
        foreach (var r in batch)
        {
            if (!catalogIds.ContainsKey(r.CatalogPath))
            {
                catalogIds[r.CatalogPath] = await EnsureCatalogAsync(r.CatalogPath, r.CatalogType, ct: ct);
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureAuthorsAsync(db, batch.SelectMany(r => r.Meta.Authors), ct);
        await EnsureSeriesAsync(db, batch.SelectMany(r => r.Meta.Series.Select(s => s.Name)), ct);
        await EnsureGenresAsync(db, batch.Where(AcceptsUnknownGenres).SelectMany(r => r.Meta.Genres), ct);

        var updateIds = batch.Where(r => r.ExistingId is not null).Select(r => r.ExistingId!.Value).ToList();
        var existing = new Dictionary<long, Book>();
        if (updateIds.Count > 0)
        {
            await db.BookAuthors.Where(x => updateIds.Contains(x.BookId)).ExecuteDeleteAsync(ct);
            await db.BookGenres.Where(x => updateIds.Contains(x.BookId)).ExecuteDeleteAsync(ct);
            await db.BookSeries.Where(x => updateIds.Contains(x.BookId)).ExecuteDeleteAsync(ct);
            existing = await db.Books.Where(b => updateIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        }

        int added = 0, updated = 0;
        foreach (var r in batch)
        {
            Book book;
            if (r.ExistingId is { } id && existing.TryGetValue(id, out var found))
            {
                book = found;
                updated++;
            }
            else
            {
                book = new Book { RelPath = r.RelPath, FileName = r.FileName, Format = r.Format, Title = "", SearchTitle = "" };
                db.Books.Add(book);
                added++;
            }

            Fill(book, r, catalogIds[r.CatalogPath]);
        }

        await db.SaveChangesAsync(ct);
        Interlocked.Add(ref progress.BooksAdded, added);
        Interlocked.Add(ref progress.BooksUpdated, updated);
    }

    private void Fill(Book book, BookRecord r, long catalogId)
    {
        var m = r.Meta;
        var title = TextNormalizer.Strip(m.Title);
        if (title.Length == 0)
        {
            title = Path.GetFileNameWithoutExtension(r.FileName);
        }

        title = TextNormalizer.Truncate(title, 512);
        var authors = m.Authors.Select(a => TextNormalizer.Truncate(a, 256)).Distinct().ToList();

        book.LibraryId = libraryId;
        book.CatalogId = catalogId;
        book.Container = r.Container;
        book.RelPath = r.RelPath;
        book.EntryName = r.EntryName;
        book.FileName = TextNormalizer.Truncate(r.FileName, 512);
        book.Format = TextNormalizer.Truncate(r.Format, 16);
        book.FileSize = r.FileSize;
        book.FileMtime = r.FileMtime;
        book.Title = title;
        book.SearchTitle = TextNormalizer.Truncate(TextNormalizer.SearchKey(title), 512);
        book.Annotation = string.IsNullOrEmpty(m.Annotation) ? null : TextNormalizer.Truncate(m.Annotation, 10000);
        book.DocDate = string.IsNullOrEmpty(m.DocDate) ? null : TextNormalizer.Truncate(m.DocDate, 32);
        book.Lang = string.IsNullOrEmpty(m.Lang) ? null : TextNormalizer.Truncate(m.Lang.ToLowerInvariant(), 16);
        book.LangCode = LangCodes.Detect(title);
        book.DupGroupKey = DocumentHashes.DuplicateKey(title, authors);
        book.ContentHash = r.ContentHash;
        book.KoreaderHash = r.KoreaderHash;
        book.Cover = CoverState.Unknown;
        book.DeletedAt = null;

        short pos = 0;
        foreach (var a in authors)
        {
            if (_authors.TryGetValue(TextNormalizer.Truncate(TextNormalizer.SearchKey(a), 256), out var authorId))
            {
                book.Authors.Add(new BookAuthor { AuthorId = authorId, Position = pos++ });
            }
        }

        foreach (var s in m.Series.DistinctBy(s => s.Name))
        {
            if (_series.TryGetValue(TextNormalizer.Truncate(TextNormalizer.SearchKey(s.Name), 256), out var seriesId)
                && book.Series.All(x => x.SeriesId != seriesId))
            {
                book.Series.Add(new BookSeries { SeriesId = seriesId, SerNo = s.Number });
            }
        }

        foreach (var g in m.Genres.Select(NormalizeGenre).Distinct())
        {
            if (_genres.TryGetValue(g, out var genreId))
            {
                book.Genres.Add(new BookGenre { GenreId = genreId });
            }
        }
    }

    /// <summary>
    /// FB2/INPX genres are codes and are kept even when unknown; EPUB/MOBI subjects are free text,
    /// so they only count when they happen to match a known genre code.
    /// </summary>
    private static bool AcceptsUnknownGenres(BookRecord r) => r.Format == "fb2" || r.Container == BookContainer.Inpx;

    private static string NormalizeGenre(string g) => TextNormalizer.Truncate(g.Trim().ToLowerInvariant(), 64);

    private async Task EnsureAuthorsAsync(NopdsDbContext db, IEnumerable<string> names, CancellationToken ct)
    {
        var missing = names.Select(n => TextNormalizer.Truncate(n, 256))
            .Select(n => (Name: n, Key: TextNormalizer.Truncate(TextNormalizer.SearchKey(n), 256)))
            .Where(x => x.Key.Length > 0 && !_authors.ContainsKey(x.Key))
            .DistinctBy(x => x.Key).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO authors (full_name, search_name, lang_code) SELECT * FROM unnest(@n, @k, @c) ON CONFLICT (search_name) DO NOTHING",
            [TextArray("n", missing.Select(m => m.Name)), TextArray("k", missing.Select(m => m.Key)), IntArray("c", missing.Select(m => (int)LangCodes.Detect(m.Name)))],
            ct);
        var keys = missing.Select(m => m.Key).ToList();
        foreach (var a in await db.Authors.Where(a => keys.Contains(a.SearchName)).Select(a => new { a.Id, a.SearchName }).ToListAsync(ct))
        {
            _authors[a.SearchName] = a.Id;
        }
    }

    private async Task EnsureSeriesAsync(NopdsDbContext db, IEnumerable<string> names, CancellationToken ct)
    {
        var missing = names.Select(n => TextNormalizer.Truncate(n, 256))
            .Select(n => (Name: n, Key: TextNormalizer.Truncate(TextNormalizer.SearchKey(n), 256)))
            .Where(x => x.Key.Length > 0 && !_series.ContainsKey(x.Key))
            .DistinctBy(x => x.Key).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO series (name, search_name, lang_code) SELECT * FROM unnest(@n, @k, @c) ON CONFLICT (search_name) DO NOTHING",
            [TextArray("n", missing.Select(m => m.Name)), TextArray("k", missing.Select(m => m.Key)), IntArray("c", missing.Select(m => (int)LangCodes.Detect(m.Name)))],
            ct);
        var keys = missing.Select(m => m.Key).ToList();
        foreach (var s in await db.Series.Where(s => keys.Contains(s.SearchName)).Select(s => new { s.Id, s.SearchName }).ToListAsync(ct))
        {
            _series[s.SearchName] = s.Id;
        }
    }

    private async Task EnsureGenresAsync(NopdsDbContext db, IEnumerable<string> codes, CancellationToken ct)
    {
        var missing = codes.Select(NormalizeGenre).Where(c => c.Length > 0 && !_genres.ContainsKey(c)).Distinct().ToList();
        if (missing.Count == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO genres (code, section) SELECT * FROM unnest(@k, @s) ON CONFLICT (code) DO NOTHING",
            [TextArray("k", missing), TextArray("s", missing.Select(c => genreCatalog.SectionOf(c) ?? GenreCatalog.UnknownSection))],
            ct);
        foreach (var g in await db.Genres.Where(g => missing.Contains(g.Code)).Select(g => new { g.Id, g.Code }).ToListAsync(ct))
        {
            _genres[g.Code] = g.Id;
        }
    }

    private static NpgsqlParameter TextArray(string name, IEnumerable<string> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() };

    private static NpgsqlParameter IntArray(string name, IEnumerable<int> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values.ToArray() };
}
