using Microsoft.EntityFrameworkCore;
using Nopds.Domain.Entities;
using Nopds.Formats.Covers;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Data;

namespace Nopds.Web.Infrastructure;

/// <summary>Serves covers/thumbnails and remembers whether a book has a cover, so feeds stop advertising missing ones.</summary>
public sealed class Covers(CoverService covers, CatalogService catalog, ScopeFactory scopes, NopdsDbContext db)
{
    public async Task<IResult> ServeAsync(HttpContext http, long id, bool thumb, CancellationToken ct)
    {
        var book = await catalog.BookEntityAsync(scopes.Create(), id, ct);
        if (book?.Library is null)
        {
            return Results.NotFound();
        }

        var file = thumb ? await covers.GetThumbnailAsync(book.Library, book, ct) : await covers.GetCoverAsync(book.Library, book, ct);
        var state = file is null ? CoverState.None : CoverState.Present;
        if (book.Cover != state)
        {
            await db.Books.Where(b => b.Id == id).ExecuteUpdateAsync(s => s.SetProperty(b => b.Cover, state), ct);
        }

        if (file is null)
        {
            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.NotFound();
        }

        // Stable URL per book; a changed file gets a new server-side cache key.
        http.Response.Headers.CacheControl = "private, max-age=604800";
        return Results.File(file.Path, file.MediaType, lastModified: File.GetLastWriteTimeUtc(file.Path),
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{Path.GetFileNameWithoutExtension(file.Path)}\""));
    }
}
