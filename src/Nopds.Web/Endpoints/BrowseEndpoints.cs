using Microsoft.EntityFrameworkCore;
using Nopds.Conversion;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Settings;
using Nopds.Web.Auth;
using Nopds.Web.Infrastructure;

namespace Nopds.Web.Endpoints;

public static class BrowseEndpoints
{
    public sealed record BookDetails(BookSummary Book, IReadOnlyList<string> ConvertTargets, bool OnShelf, string? KoreaderHash);

    public sealed record SiteConfig(string Title, string Subtitle, string Version, AccessMode Access, bool AlphabetMenu, int SplitItems, int PageSize, bool ShowCovers, string[] Languages, string? Sso);

    public static void MapBrowseEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/config", (SettingsStore settings, Microsoft.Extensions.Options.IOptions<NopdsOptions> options) =>
        {
            var s = settings.Current;
            var version = typeof(BrowseEndpoints).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            return new SiteConfig(s.Title, s.Subtitle, version, s.Access, s.AlphabetMenu, s.SplitItems, s.MaxItems, s.ShowCovers, Domain.Text.UiLanguages.Supported,
                options.Value.Oidc.Enabled ? options.Value.Oidc.DisplayName : null);
        }).AllowAnonymous();

        var g = api.MapGroup("").RequireAuthorization(Policies.Reader);

        g.MapGet("/stats", (ScopeFactory scopes, CatalogService catalog, int? library, CancellationToken ct) =>
            catalog.StatsAsync(scopes.Create(library), ct));

        g.MapGet("/libraries", (ScopeFactory scopes, CatalogService catalog, CancellationToken ct) =>
            catalog.LibrariesAsync(scopes.Create(), ct));

        g.MapGet("/books", (ScopeFactory scopes, CatalogService catalog, [AsParameters] BookListQuery q, CancellationToken ct) =>
            catalog.BooksAsync(scopes.Create(q.Library, q.Dupes is null ? null : !q.Dupes), q.ToQuery(), ct));

        g.MapGet("/books/recent", (ScopeFactory scopes, CatalogService catalog, int? library, int? page, int? pageSize, CancellationToken ct) =>
            catalog.BooksAsync(scopes.Create(library), new BookQuery { Sort = BookSort.Added, Page = page ?? 1, PageSize = pageSize ?? 24 }, ct));

        g.MapGet("/books/random", async (ScopeFactory scopes, CatalogService catalog, int? library, CancellationToken ct) =>
            await catalog.RandomBookAsync(scopes.Create(library), ct) is { } b ? Results.Ok(b) : Results.NoContent());

        g.MapGet("/books/{id:long}", async (long id, ScopeFactory scopes, CatalogService catalog, ConversionService conversion, CurrentUser user,
            Nopds.Infrastructure.Data.NopdsDbContext db, CancellationToken ct) =>
        {
            var scope = scopes.Create();
            var book = await catalog.BookAsync(scope, id, ct);
            if (book is null)
            {
                return Results.NotFound();
            }

            var onShelf = user.Id is { } uid && await db.ReadingStates.AnyAsync(r => r.UserId == uid && r.BookId == id, ct);
            var ko = await db.Books.Where(b => b.Id == id).Select(b => b.KoreaderHash).FirstOrDefaultAsync(ct);
            return Results.Ok(new BookDetails(book, conversion.TargetsFor(book.Format), onShelf, ko));
        });

        g.MapGet("/books/{id:long}/editions", (long id, ScopeFactory scopes, CatalogService catalog, CancellationToken ct) =>
            catalog.BooksAsync(scopes.Create(hideDuplicates: false), new BookQuery { EditionsOf = id, PageSize = 100 }, ct));

        g.MapGet("/books/{id:long}/download", async (long id, string? format, bool? zip, HttpContext http, ScopeFactory scopes,
            CatalogService catalog, BookFiles files, CurrentUser user, CancellationToken ct) =>
        {
            var book = await catalog.BookEntityAsync(scopes.Create(), id, ct);
            return book is null ? Results.NotFound() : await files.ServeAsync(http, book, format, zip == true, inline: false, user.Id, ct);
        });

        // Raw book for the in-browser reader (foliate-js reads EPUB, FB2, MOBI/AZW3 and CBZ natively).
        g.MapGet("/books/{id:long}/content", async (long id, HttpContext http, ScopeFactory scopes, CatalogService catalog, BookFiles files,
            CurrentUser user, CancellationToken ct) =>
        {
            var book = await catalog.BookEntityAsync(scopes.Create(), id, ct);
            return book is null ? Results.NotFound() : await files.ServeAsync(http, book, null, zip: false, inline: true, user.Id, ct);
        });

        g.MapGet("/books/{id:long}/cover", (long id, HttpContext http, Covers covers, CancellationToken ct) => covers.ServeAsync(http, id, false, ct));

        g.MapGet("/books/{id:long}/thumb", (long id, HttpContext http, Covers covers, CancellationToken ct) => covers.ServeAsync(http, id, true, ct));

        g.MapGet("/authors", (ScopeFactory scopes, CatalogService catalog, [AsParameters] NameListQuery q, CancellationToken ct) =>
            catalog.AuthorsAsync(scopes.Create(q.Library), q.ToQuery(), ct));

        g.MapGet("/authors/{id:long}", async (long id, CatalogService catalog, CancellationToken ct) =>
            await catalog.AuthorAsync(id, ct) is { } a ? Results.Ok(a) : Results.NotFound());

        g.MapGet("/series", (ScopeFactory scopes, CatalogService catalog, [AsParameters] NameListQuery q, CancellationToken ct) =>
            catalog.SeriesAsync(scopes.Create(q.Library), q.ToQuery(), ct));

        g.MapGet("/series/{id:long}", async (long id, CatalogService catalog, CancellationToken ct) =>
            await catalog.SeriesNameAsync(id, ct) is { } s ? Results.Ok(s) : Results.NotFound());

        g.MapGet("/alphabet/{kind}", (string kind, ScopeFactory scopes, CatalogService catalog, int? library, string? lang, string? prefix, CancellationToken ct) =>
        {
            var k = kind switch
            {
                "authors" => CatalogService.AlphabetKind.Authors,
                "series" => CatalogService.AlphabetKind.Series,
                _ => CatalogService.AlphabetKind.Books,
            };
            return catalog.AlphabetAsync(scopes.Create(library), k, EnumParam.Parse<LangCode>(lang) ?? LangCode.All, prefix, ct);
        });

        g.MapGet("/genres", (ScopeFactory scopes, CatalogService catalog, int? library, CancellationToken ct) =>
            catalog.GenreSectionsAsync(scopes.Create(library), ct));

        g.MapGet("/genres/{section}", (string section, ScopeFactory scopes, CatalogService catalog, int? library, CancellationToken ct) =>
            catalog.GenresAsync(scopes.Create(library), section, ct));

        g.MapGet("/libraries/{libraryId:int}/catalog", async (int libraryId, long? id, int? page, ScopeFactory scopes, CatalogService catalog,
            SettingsStore settings, CancellationToken ct) =>
            await catalog.CatalogAsync(scopes.Create(), libraryId, id, page ?? 1, settings.Current.MaxItems, ct) is { } listing
                ? Results.Ok(listing)
                : Results.NotFound());
    }
}

public sealed record BookListQuery(
    int? Library,
    string? Q,
    string? Match,
    string? Lang,
    long? Author,
    long? Series,
    int? Genre,
    string? Section,
    string? Language,
    string? Format,
    string? Sort,
    bool? Dupes,
    bool? Shelf,
    int? Page,
    int? PageSize,
    bool? Total,
    CurrentUser User)
{
    public BookQuery ToQuery() => new()
    {
        Text = Q,
        Match = EnumParam.Parse<TextMatch>(Match) ?? TextMatch.Contains,
        Lang = EnumParam.Parse<LangCode>(Lang),
        AuthorId = Author,
        SeriesId = Series,
        GenreId = Genre,
        GenreSection = Section,
        BookLanguage = Language,
        Format = Format,
        Sort = EnumParam.Parse<BookSort>(Sort) ?? (Series is not null ? BookSort.SeriesNumber : BookSort.Title),
        ShelfOf = Shelf == true ? User.Id ?? Guid.Empty : null,
        Page = Page ?? 1,
        PageSize = PageSize ?? 60,
        CountTotal = Total == true,
    };
}

public sealed record NameListQuery(int? Library, string? Q, string? Match, string? Lang, long? Author, int? Page, int? PageSize, bool? Total)
{
    public NameQuery ToQuery() => new()
    {
        Text = Q,
        Match = EnumParam.Parse<TextMatch>(Match) ?? TextMatch.Begins,
        Lang = EnumParam.Parse<LangCode>(Lang),
        AuthorId = Author,
        Page = Page ?? 1,
        PageSize = PageSize ?? 60,
        CountTotal = Total == true,
    };
}

/// <summary>Case-insensitive enum parsing for query strings (JSON uses camelCase names).</summary>
public static class EnumParam
{
    public static T? Parse<T>(string? value)
        where T : struct, Enum =>
        !string.IsNullOrWhiteSpace(value) && Enum.TryParse<T>(value, ignoreCase: true, out var v) && Enum.IsDefined(v) ? v : null;
}
