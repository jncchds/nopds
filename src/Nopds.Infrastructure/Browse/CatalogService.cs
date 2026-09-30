using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Nopds.Domain.Entities;
using Nopds.Domain.Text;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Genres;

namespace Nopds.Infrastructure.Browse;

/// <summary>Read-side queries shared by the REST API, OPDS feeds and the Telegram bot.</summary>
public sealed class CatalogService(NopdsDbContext db, GenreCatalog genreCatalog, IMemoryCache cache)
{
    public const string StatsCacheKey = "catalog:stats";

    // ------------------------------------------------------------------ scoping

    private IQueryable<Book> Visible(Scope scope)
    {
        var q = db.Books.AsNoTracking().Where(b => b.DeletedAt == null);
        if (scope.AllowedLibraries is { } allowed)
        {
            q = q.Where(b => allowed.Contains(b.LibraryId));
        }

        if (scope.LibraryId is { } lib)
        {
            q = q.Where(b => b.LibraryId == lib);
        }

        if (!scope.IsAdmin)
        {
            // Private uploads: only the uploader sees them (anonymous callers never do).
            var uid = scope.UserId;
            q = q.Where(b => !db.Uploads.Any(u => u.LibraryId == b.LibraryId && u.RelPath == b.RelPath && u.IsPrivate
                                                  && (uid == null || u.UserId == null || u.UserId != uid)));
        }

        return q;
    }

    public bool CanAccessLibrary(Scope scope, int libraryId) =>
        scope.AllowedLibraries is null || scope.AllowedLibraries.Contains(libraryId);

    /// <summary>b => format == p[0] ? 0 : format == p[1] ? 1 : … : 99</summary>
    private static Expression<Func<Book, int>> FormatRank(string[] preferred)
    {
        var p = Expression.Parameter(typeof(Book), "b");
        var format = Expression.Property(p, nameof(Book.Format));
        Expression body = Expression.Constant(99);
        for (var i = preferred.Length - 1; i >= 0; i--)
        {
            body = Expression.Condition(Expression.Equal(format, Expression.Constant(preferred[i])), Expression.Constant(i), body);
        }

        return Expression.Lambda<Func<Book, int>>(body, p);
    }

    /// <summary>Keeps only the preferred edition of every duplicate group (format preference, then newest).</summary>
    private IQueryable<Book> PrimaryEditions(IQueryable<Book> q, Scope scope)
    {
        var visible = Visible(scope);
        var rank = FormatRank(scope.PreferredFormats);

        // Build: q.Where(b => b.Id == visible.Where(x => x.DupGroupKey == b.DupGroupKey).OrderBy(rank).ThenByDescending(x => x.RegisteredAt).Select(x => x.Id).First())
        var b = Expression.Parameter(typeof(Book), "b");
        var x = Expression.Parameter(typeof(Book), "x");
        var sameGroup = Expression.Lambda<Func<Book, bool>>(
            Expression.Equal(Expression.Property(x, nameof(Book.DupGroupKey)), Expression.Property(b, nameof(Book.DupGroupKey))), x);
        var group = Queryable.Where(visible, sameGroup);
        var ordered = Queryable.ThenBy(Queryable.ThenByDescending(Queryable.OrderBy(group, rank), (Expression<Func<Book, DateTimeOffset>>)(y => y.RegisteredAt)),
            (Expression<Func<Book, long>>)(y => y.Id));
        var firstId = Expression.Call(typeof(Queryable), nameof(Queryable.First), [typeof(long)],
            Expression.Call(typeof(Queryable), nameof(Queryable.Select), [typeof(Book), typeof(long)], ordered.Expression,
                Expression.Quote((Expression<Func<Book, long>>)(y => y.Id))));
        var predicate = Expression.Lambda<Func<Book, bool>>(Expression.Equal(Expression.Property(b, nameof(Book.Id)), firstId), b);
        return q.Where(predicate);
    }

    private static string Key(string? text) => TextNormalizer.SearchKey(text);

    private static string Like(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // ------------------------------------------------------------------ books

    public async Task<Page<BookSummary>> BooksAsync(Scope scope, BookQuery query, CancellationToken ct = default)
    {
        var q = Visible(scope);
        var key = Key(query.Text);
        if (key.Length > 0)
        {
            q = query.Match switch
            {
                TextMatch.Exact => q.Where(b => b.SearchTitle == key),
                TextMatch.Begins => q.Where(b => EF.Functions.Like(b.SearchTitle, Like(key) + "%")),
                _ => q.Where(b => EF.Functions.Like(b.SearchTitle, "%" + Like(key) + "%")),
            };
        }

        if (query.Lang is { } lang and not LangCode.All)
        {
            q = q.Where(b => b.LangCode == lang);
        }

        if (query.AuthorId is { } authorId)
        {
            q = q.Where(b => b.Authors.Any(a => a.AuthorId == authorId));
        }

        if (query.SeriesId is { } seriesId)
        {
            q = q.Where(b => b.Series.Any(s => s.SeriesId == seriesId));
        }

        if (query.WithoutSeries)
        {
            q = q.Where(b => !b.Series.Any());
        }

        if (query.GenreId is { } genreId)
        {
            q = q.Where(b => b.Genres.Any(g => g.GenreId == genreId));
        }

        if (query.GenreSection is { } section)
        {
            q = q.Where(b => b.Genres.Any(g => g.Genre!.Section == section));
        }

        if (query.CatalogId is { } catalogId)
        {
            q = q.Where(b => b.CatalogId == catalogId);
        }

        if (!string.IsNullOrEmpty(query.BookLanguage))
        {
            var bl = query.BookLanguage.ToLowerInvariant();
            q = q.Where(b => b.Lang == bl);
        }

        if (!string.IsNullOrEmpty(query.Format))
        {
            var f = query.Format.ToLowerInvariant();
            q = q.Where(b => b.Format == f);
        }

        if (query.EditionsOf is { } editionsOf)
        {
            var dup = await db.Books.Where(b => b.Id == editionsOf).Select(b => (long?)b.DupGroupKey).FirstOrDefaultAsync(ct);
            q = dup is null ? q.Where(_ => false) : q.Where(b => b.DupGroupKey == dup.Value);
        }
        else if (scope.HideDuplicates && query.ShelfOf is null)
        {
            q = PrimaryEditions(q, scope);
        }

        IOrderedQueryable<Book> ordered;
        if (query.ShelfOf is { } userId)
        {
            q = q.Where(b => db.ReadingStates.Any(r => r.UserId == userId && r.BookId == b.Id));
            ordered = q.OrderByDescending(b => db.ReadingStates.Where(r => r.UserId == userId && r.BookId == b.Id).Select(r => r.LastOpenedAt).First());
        }
        else if (query.Sort == BookSort.SeriesNumber && query.SeriesId is { } sid)
        {
            ordered = q.OrderBy(b => b.Series.Where(s => s.SeriesId == sid).Select(s => s.SerNo).First()).ThenBy(b => b.SearchTitle);
        }
        else if (query.Sort == BookSort.Added)
        {
            ordered = q.OrderByDescending(b => b.RegisteredAt);
        }
        else
        {
            ordered = q.OrderBy(b => b.SearchTitle).ThenByDescending(b => b.DocDate);
        }

        return await PageAsync(scope, ordered.ThenBy(b => b.Id), query.Page, query.PageSize, query.CountTotal ? q : null, ct);
    }

    private async Task<Page<BookSummary>> PageAsync(Scope scope, IQueryable<Book> ordered, int page, int pageSize, IQueryable<Book>? countQuery, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(b => new
            {
                b.Id, b.LibraryId, b.RelPath, b.Title, b.FileName, b.Format, b.FileSize, b.Lang, b.DocDate, b.RegisteredAt, b.Annotation, b.Cover, b.DupGroupKey,
            })
            .ToListAsync(ct);
        var hasNext = rows.Count > pageSize;
        if (hasNext)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var total = countQuery is null ? (long?)null : await countQuery.LongCountAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var related = await RelatedAsync(ids, scope.Lang, ct);

        var editions = new Dictionary<long, int>();
        if (rows.Count > 0)
        {
            var keys = rows.Select(r => r.DupGroupKey).Distinct().ToList();
            editions = await Visible(scope).Where(b => keys.Contains(b.DupGroupKey))
                .GroupBy(b => b.DupGroupKey).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        }

        var uploads = new Dictionary<(int, string), UploadInfo>();
        if (rows.Count > 0)
        {
            var libs = rows.Select(r => r.LibraryId).Distinct().ToList();
            var paths = rows.Select(r => r.RelPath).Distinct().ToList();
            var found = await db.Uploads.AsNoTracking().Where(u => libs.Contains(u.LibraryId) && paths.Contains(u.RelPath))
                .Select(u => new
                {
                    u.Id, u.LibraryId, u.RelPath, u.IsPrivate, u.UserId, u.UploadedAt,
                    UserName = db.Users.Where(x => x.Id == u.UserId).Select(x => x.UserName).FirstOrDefault(),
                })
                .ToListAsync(ct);
            uploads = found.ToDictionary(u => (u.LibraryId, u.RelPath),
                u => new UploadInfo(u.Id, u.IsPrivate, u.UserId != null && u.UserId == scope.UserId, u.UserName, u.UploadedAt));
        }

        var items = rows.Select(r => new BookSummary(
            r.Id, r.LibraryId, r.Title, r.FileName, r.Format, r.FileSize, r.Lang, r.DocDate, r.RegisteredAt, r.Annotation, r.Cover,
            related.Authors.GetValueOrDefault(r.Id) ?? [],
            related.Series.GetValueOrDefault(r.Id) ?? [],
            related.Genres.GetValueOrDefault(r.Id) ?? [],
            editions.GetValueOrDefault(r.DupGroupKey, 1),
            uploads.GetValueOrDefault((r.LibraryId, r.RelPath)))).ToList();
        return new Page<BookSummary>(items, page, pageSize, hasNext, total);
    }

    private sealed record Related(
        Dictionary<long, List<AuthorRef>> Authors,
        Dictionary<long, List<SeriesInfo>> Series,
        Dictionary<long, List<GenreRef>> Genres);

    private async Task<Related> RelatedAsync(List<long> ids, string lang, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Related([], [], []);
        }

        var authors = (await db.BookAuthors.AsNoTracking().Where(x => ids.Contains(x.BookId))
                .OrderBy(x => x.Position)
                .Select(x => new { x.BookId, x.AuthorId, x.Author!.FullName }).ToListAsync(ct))
            .GroupBy(x => x.BookId).ToDictionary(g => g.Key, g => g.Select(x => new AuthorRef(x.AuthorId, x.FullName)).ToList());
        var series = (await db.BookSeries.AsNoTracking().Where(x => ids.Contains(x.BookId))
                .Select(x => new { x.BookId, x.SeriesId, x.Series!.Name, x.SerNo }).ToListAsync(ct))
            .GroupBy(x => x.BookId).ToDictionary(g => g.Key, g => g.Select(x => new SeriesInfo(x.SeriesId, x.Name, x.SerNo)).ToList());
        var genres = (await db.BookGenres.AsNoTracking().Where(x => ids.Contains(x.BookId))
                .Select(x => new { x.BookId, x.GenreId, x.Genre!.Code, x.Genre.Section }).ToListAsync(ct))
            .GroupBy(x => x.BookId).ToDictionary(g => g.Key, g => g.Select(x => ToGenreRef(x.GenreId, x.Code, x.Section, lang)).ToList());
        return new Related(authors, series, genres);
    }

    private GenreRef ToGenreRef(int id, string code, string section, string lang) =>
        new(id, code, genreCatalog.GenreName(code, lang), section, genreCatalog.SectionName(section, lang));

    public async Task<BookSummary?> BookAsync(Scope scope, long id, CancellationToken ct = default)
    {
        var page = await PageAsync(scope with { HideDuplicates = false }, Visible(scope).Where(b => b.Id == id).OrderBy(b => b.Id), 1, 1, null, ct);
        return page.Items.FirstOrDefault();
    }

    /// <summary>Loads the entity with its library for file access, honoring the scope.</summary>
    public Task<Book?> BookEntityAsync(Scope scope, long id, CancellationToken ct = default) =>
        Visible(scope).Include(b => b.Library).Include(b => b.Authors.OrderBy(a => a.Position)).ThenInclude(a => a.Author)
            .Include(b => b.Series).ThenInclude(s => s.Series).Include(b => b.Genres).ThenInclude(g => g.Genre)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<BookSummary?> RandomBookAsync(Scope scope, CancellationToken ct = default)
    {
        var q = Visible(scope);
        var max = await q.MaxAsync(b => (long?)b.Id, ct);
        if (max is null)
        {
            return null;
        }

        var pivot = Random.Shared.NextInt64(1, max.Value + 1);
        var id = await q.Where(b => b.Id >= pivot).OrderBy(b => b.Id).Select(b => (long?)b.Id).FirstOrDefaultAsync(ct)
                 ?? await q.OrderBy(b => b.Id).Select(b => (long?)b.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await BookAsync(scope, id.Value, ct);
    }

    // ------------------------------------------------------------------ authors & series

    public async Task<Page<NamedCount>> AuthorsAsync(Scope scope, NameQuery query, CancellationToken ct = default)
    {
        var visible = Visible(scope);
        var q = db.Authors.AsNoTracking().Where(a => a.Books.Any(ba => visible.Any(b => b.Id == ba.BookId)));
        var key = Key(query.Text);
        if (key.Length > 0)
        {
            q = query.Match switch
            {
                TextMatch.Exact => q.Where(a => a.SearchName == key),
                TextMatch.Begins => q.Where(a => EF.Functions.Like(a.SearchName, Like(key) + "%")),
                _ => q.Where(a => EF.Functions.Like(a.SearchName, "%" + Like(key) + "%")),
            };
        }

        if (query.Lang is { } lang and not LangCode.All)
        {
            q = q.Where(a => a.LangCode == lang);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 500);
        var rows = await q.OrderBy(a => a.SearchName).ThenBy(a => a.Id).Skip((page - 1) * size).Take(size + 1)
            .Select(a => new { a.Id, a.FullName }).ToListAsync(ct);
        var hasNext = rows.Count > size;
        if (hasNext)
        {
            rows.RemoveAt(size);
        }

        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.BookAuthors.Where(x => ids.Contains(x.AuthorId) && visible.Any(b => b.Id == x.BookId))
            .GroupBy(x => x.AuthorId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var total = query.CountTotal ? await q.LongCountAsync(ct) : (long?)null;
        return new Page<NamedCount>(rows.Select(r => new NamedCount(r.Id, r.FullName, counts.GetValueOrDefault(r.Id))).ToList(), page, size, hasNext, total);
    }

    public async Task<Page<NamedCount>> SeriesAsync(Scope scope, NameQuery query, CancellationToken ct = default)
    {
        var visible = Visible(scope);
        if (query.AuthorId is { } authorId)
        {
            visible = visible.Where(b => b.Authors.Any(a => a.AuthorId == authorId));
        }

        var q = db.Series.AsNoTracking().Where(s => s.Books.Any(bs => visible.Any(b => b.Id == bs.BookId)));
        var key = Key(query.Text);
        if (key.Length > 0)
        {
            q = query.Match switch
            {
                TextMatch.Exact => q.Where(s => s.SearchName == key),
                TextMatch.Begins => q.Where(s => EF.Functions.Like(s.SearchName, Like(key) + "%")),
                _ => q.Where(s => EF.Functions.Like(s.SearchName, "%" + Like(key) + "%")),
            };
        }

        if (query.Lang is { } lang and not LangCode.All)
        {
            q = q.Where(s => s.LangCode == lang);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 500);
        var rows = await q.OrderBy(s => s.SearchName).ThenBy(s => s.Id).Skip((page - 1) * size).Take(size + 1)
            .Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var hasNext = rows.Count > size;
        if (hasNext)
        {
            rows.RemoveAt(size);
        }

        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.BookSeries.Where(x => ids.Contains(x.SeriesId) && visible.Any(b => b.Id == x.BookId))
            .GroupBy(x => x.SeriesId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var total = query.CountTotal ? await q.LongCountAsync(ct) : (long?)null;
        return new Page<NamedCount>(rows.Select(r => new NamedCount(r.Id, r.Name, counts.GetValueOrDefault(r.Id))).ToList(), page, size, hasNext, total);
    }

    public Task<AuthorRef?> AuthorAsync(long id, CancellationToken ct = default) =>
        db.Authors.AsNoTracking().Where(a => a.Id == id).Select(a => new AuthorRef(a.Id, a.FullName)).FirstOrDefaultAsync(ct);

    public Task<AuthorRef?> SeriesNameAsync(long id, CancellationToken ct = default) =>
        db.Series.AsNoTracking().Where(s => s.Id == id).Select(s => new AuthorRef(s.Id, s.Name)).FirstOrDefaultAsync(ct);

    // ------------------------------------------------------------------ alphabet

    public enum AlphabetKind
    {
        Books,
        Authors,
        Series,
    }

    /// <summary>Groups names by the next character after <paramref name="prefix"/> (SimpleOPDS alphabet navigation).</summary>
    public async Task<IReadOnlyList<AlphabetGroup>> AlphabetAsync(Scope scope, AlphabetKind kind, LangCode lang, string? prefix, CancellationToken ct = default)
    {
        var p = Key(prefix);
        var len = p.Length + 1;
        var like = Like(p) + "%";
        var visible = Visible(scope);
        IQueryable<string> names = kind switch
        {
            AlphabetKind.Books => (scope.HideDuplicates ? PrimaryEditions(visible, scope) : visible)
                .Where(b => lang == LangCode.All || b.LangCode == lang).Select(b => b.SearchTitle),
            AlphabetKind.Authors => db.Authors.Where(a => (lang == LangCode.All || a.LangCode == lang) && a.Books.Any(ba => visible.Any(b => b.Id == ba.BookId)))
                .Select(a => a.SearchName),
            _ => db.Series.Where(s => (lang == LangCode.All || s.LangCode == lang) && s.Books.Any(bs => visible.Any(b => b.Id == bs.BookId)))
                .Select(s => s.SearchName),
        };

        var groups = await names.Where(n => EF.Functions.Like(n, like) && n.Length >= len)
            .GroupBy(n => n.Substring(0, len))
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return groups.Select(g => new AlphabetGroup(g.Key, g.Count)).OrderBy(g => g.Prefix, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ genres

    public async Task<IReadOnlyList<GenreSectionInfo>> GenreSectionsAsync(Scope scope, CancellationToken ct = default)
    {
        var visible = Visible(scope);
        var counts = await db.BookGenres.Where(x => visible.Any(b => b.Id == x.BookId))
            .GroupBy(x => x.Genre!.Section).Select(g => new { g.Key, Count = g.Select(x => x.BookId).Distinct().Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var order = genreCatalog.SectionOrder.Select((s, i) => (s, i)).ToDictionary(t => t.s, t => t.i);
        return counts.Select(kv => new GenreSectionInfo(kv.Key, genreCatalog.SectionName(kv.Key, scope.Lang), kv.Value))
            .OrderBy(s => order.GetValueOrDefault(s.Key, int.MaxValue)).ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<IReadOnlyList<GenreInfo>> GenresAsync(Scope scope, string section, CancellationToken ct = default)
    {
        var visible = Visible(scope);
        var rows = await db.BookGenres.Where(x => x.Genre!.Section == section && visible.Any(b => b.Id == x.BookId))
            .GroupBy(x => new { x.GenreId, x.Genre!.Code, x.Genre.Section }).Select(g => new { g.Key.GenreId, g.Key.Code, g.Key.Section, Count = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r => new GenreInfo(r.GenreId, r.Code, genreCatalog.GenreName(r.Code, scope.Lang), r.Section, r.Count))
            .OrderBy(g => g.Name, StringComparer.CurrentCulture).ToList();
    }

    public async Task<GenreRef?> GenreAsync(int id, string lang, CancellationToken ct = default)
    {
        var g = await db.Genres.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return g is null ? null : ToGenreRef(g.Id, g.Code, g.Section, lang);
    }

    public string SectionName(string section, string lang) => genreCatalog.SectionName(section, lang);

    // ------------------------------------------------------------------ libraries & folders

    public async Task<IReadOnlyList<LibraryInfo>> LibrariesAsync(Scope scope, CancellationToken ct = default)
    {
        var q = db.Libraries.AsNoTracking().Where(l => l.Enabled);
        if (scope.AllowedLibraries is { } allowed)
        {
            q = q.Where(l => allowed.Contains(l.Id));
        }

        var libs = await q.OrderBy(l => l.Name).Select(l => new { l.Id, l.Name, l.LastScanFinishedAt, l.IsUploads }).ToListAsync(ct);
        var counts = await Visible(scope with { LibraryId = null }).GroupBy(b => b.LibraryId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return libs.Select(l => new LibraryInfo(l.Id, l.Name, counts.GetValueOrDefault(l.Id), l.LastScanFinishedAt, l.IsUploads)).ToList();
    }

    public async Task<CatalogListing?> CatalogAsync(Scope scope, int libraryId, long? catalogId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanAccessLibrary(scope, libraryId))
        {
            return null;
        }

        var current = catalogId is null
            ? await db.Catalogs.AsNoTracking().FirstOrDefaultAsync(c => c.LibraryId == libraryId && c.ParentId == null, ct)
            : await db.Catalogs.AsNoTracking().FirstOrDefaultAsync(c => c.LibraryId == libraryId && c.Id == catalogId, ct);
        if (current is null)
        {
            return catalogId is null
                ? new CatalogListing(null, [], [], new Page<BookSummary>([], 1, pageSize, false, 0))
                : null;
        }

        var breadcrumbs = new List<CatalogNode>();
        var cursor = current;
        while (cursor.ParentId is { } parentId)
        {
            cursor = await db.Catalogs.AsNoTracking().FirstAsync(c => c.Id == parentId, ct);
            breadcrumbs.Insert(0, Node(cursor));
        }

        // Archives holding only other users' private uploads are not listed.
        var visible = Visible(scope with { LibraryId = libraryId });
        var children = await db.Catalogs.AsNoTracking().Where(c => c.ParentId == current.Id)
            .Where(c => c.Type == CatalogType.Directory || visible.Any(b => b.CatalogId == c.Id))
            .OrderBy(c => c.Type == CatalogType.Directory ? 0 : 1).ThenBy(c => c.Name)
            .Select(c => new CatalogNode(c.Id, c.Name, c.Path, c.Type, c.LibraryId)).ToListAsync(ct);
        var books = await BooksAsync(scope with { HideDuplicates = false, LibraryId = libraryId },
            new BookQuery { CatalogId = current.Id, Page = page, PageSize = pageSize }, ct);
        return new CatalogListing(Node(current), breadcrumbs, children, books);
    }

    private static CatalogNode Node(Catalog c) => new(c.Id, c.Name, c.Path, c.Type, c.LibraryId);

    // ------------------------------------------------------------------ stats

    public async Task<Stats> StatsAsync(Scope scope, CancellationToken ct = default)
    {
        var viewer = scope.IsAdmin ? "admin" : scope.UserId?.ToString("N") ?? "anon";
        var key = $"{StatsCacheKey}:{viewer}:{(scope.AllowedLibraries is null ? "all" : string.Join(',', scope.AllowedLibraries))}:{scope.LibraryId}";
        if (cache.TryGetValue(key, out Stats? cached) && cached is not null)
        {
            return cached;
        }

        var visible = Visible(scope);
        var stats = new Stats(
            await visible.LongCountAsync(ct),
            await db.BookAuthors.Where(x => visible.Any(b => b.Id == x.BookId)).Select(x => x.AuthorId).Distinct().LongCountAsync(ct),
            await db.BookSeries.Where(x => visible.Any(b => b.Id == x.BookId)).Select(x => x.SeriesId).Distinct().LongCountAsync(ct),
            await db.BookGenres.Where(x => visible.Any(b => b.Id == x.BookId)).Select(x => x.GenreId).Distinct().LongCountAsync(ct),
            await db.Libraries.LongCountAsync(l => l.Enabled && (scope.AllowedLibraries == null || scope.AllowedLibraries.Contains(l.Id)), ct),
            await db.Libraries.MaxAsync(l => l.LastScanFinishedAt, ct));
        using (var entry = cache.CreateEntry(key))
        {
            entry.Value = stats;
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            entry.AddExpirationToken(StatsInvalidation.Token);
        }

        return stats;
    }
}

/// <summary>Cancels cached statistics after a scan changes the catalog.</summary>
public static class StatsInvalidation
{
    private static CancellationTokenSource _cts = new();

    public static Microsoft.Extensions.Primitives.IChangeToken Token => new Microsoft.Extensions.Primitives.CancellationChangeToken(_cts.Token);

    public static void Invalidate()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}
