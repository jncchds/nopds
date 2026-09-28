using Nopds.Formats.Covers;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Settings;
using Nopds.Opds;
using Nopds.Web.Auth;
using Nopds.Web.Infrastructure;
using L = Nopds.Infrastructure.Localization.ServerStrings;

namespace Nopds.Web.Opds;

public static class OpdsEndpoints
{
    public static void MapOpds(this IEndpointRouteBuilder app)
    {
        app.MapGet("/opds/{**path}", HandleAsync).RequireAuthorization(Policies.OpdsReader).WithName("opds");
    }

    private static async Task<IResult> HandleAsync(
        string? path,
        HttpContext http,
        OpdsCatalog feeds,
        CatalogService catalog,
        ScopeFactory scopes,
        BookFiles files,
        CoverService covers,
        SettingsStore settings,
        CurrentUser user,
        CancellationToken ct)
    {
        var r = OpdsRequest.Parse(http.Request, path);
        switch (r.Seg(0))
        {
            case "download" when r.Long(1) is { } id:
                return await ServeBookAsync(id, null, r.Seg(2) == "1");
            case "convert" when r.Long(1) is { } id && r.Seg(2).Length > 0:
                return await ServeBookAsync(id, r.Seg(2), false);
            case "cover" when r.Long(1) is { } id:
                return await ServeCoverAsync(id, thumb: false);
            case "thumb" when r.Long(1) is { } id:
                return await ServeCoverAsync(id, thumb: true);
            case "search.xml":
                var lang = scopes.Language;
                var template = r.Origin + r.Prefix + "/search/{searchTerms}/";
                return Results.Bytes(AtomWriter.OpenSearch(settings.Current.Title, L.Get(lang, "search.books"), template, lang), OpdsTypes.OpenSearch);
        }

        var feed = await feeds.BuildAsync(r, http.Request, ct);
        if (feed is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.CacheControl = "private, no-cache";
        http.Response.Headers.Vary = "Accept-Language, Authorization";
        return r.V2
            ? Results.Bytes(Opds2Writer.Write(feed), OpdsTypes.Opds2 + "; charset=utf-8")
            : Results.Bytes(AtomWriter.Write(feed), (feed.IsAcquisition ? OpdsTypes.Acquisition : OpdsTypes.Navigation) + "; charset=utf-8");

        async Task<IResult> ServeBookAsync(long id, string? format, bool zip)
        {
            var book = await catalog.BookEntityAsync(scopes.Create(), id, ct);
            return book is null ? Results.NotFound() : await files.ServeAsync(http, book, format, zip, inline: false, user.Id, ct);
        }

        async Task<IResult> ServeCoverAsync(long id, bool thumb)
        {
            var book = await catalog.BookEntityAsync(scopes.Create(), id, ct);
            if (book?.Library is null)
            {
                return Results.NotFound();
            }

            var file = thumb ? await covers.GetThumbnailAsync(book.Library, book, ct) : await covers.GetCoverAsync(book.Library, book, ct);
            if (file is null)
            {
                return Results.NotFound();
            }

            http.Response.Headers.CacheControl = "private, max-age=604800";
            return Results.File(file.Path, file.MediaType);
        }
    }
}
