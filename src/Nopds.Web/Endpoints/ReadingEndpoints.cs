using Microsoft.EntityFrameworkCore;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Data;
using Nopds.Web.Auth;
using Nopds.Web.Infrastructure;

namespace Nopds.Web.Endpoints;

/// <summary>Bookshelf and reading progress of the signed-in user.</summary>
public static class ReadingEndpoints
{
    public sealed record ProgressDto(long BookId, string? Location, double Progress, bool Finished, DateTimeOffset LastOpenedAt);

    public sealed record ProgressUpdate(string? Location, double? Progress, bool? Finished);

    public sealed record ShelfItem(BookSummary Book, double Progress, bool Finished, DateTimeOffset LastOpenedAt);

    public static void MapReadingEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/shelf").RequireAuthorization(Policies.User);

        g.MapGet("", async (CurrentUser user, ScopeFactory scopes, CatalogService catalog, NopdsDbContext db, int? page, bool? unfinished, CancellationToken ct) =>
        {
            var p = Math.Max(1, page ?? 1);
            const int size = 30;
            var q = db.ReadingStates.AsNoTracking().Where(r => r.UserId == user.Id);
            if (unfinished == true)
            {
                q = q.Where(r => !r.Finished);
            }

            var states = await q.OrderByDescending(r => r.LastOpenedAt).Skip((p - 1) * size).Take(size + 1).ToListAsync(ct);
            var hasNext = states.Count > size;
            var scope = scopes.Create(hideDuplicates: false);
            var items = new List<ShelfItem>();
            foreach (var s in states.Take(size))
            {
                if (await catalog.BookAsync(scope, s.BookId, ct) is { } book)
                {
                    items.Add(new ShelfItem(book, s.Progress, s.Finished, s.LastOpenedAt));
                }
            }

            return new Page<ShelfItem>(items, p, size, hasNext, null);
        });

        g.MapPut("/{bookId:long}", async (long bookId, CurrentUser user, BookFiles files, CancellationToken ct) =>
        {
            await files.TouchShelfAsync(user.Id!.Value, bookId, ct);
            return Results.NoContent();
        });

        g.MapDelete("/{bookId:long}", async (long bookId, CurrentUser user, NopdsDbContext db, CancellationToken ct) =>
        {
            await db.ReadingStates.Where(r => r.UserId == user.Id && r.BookId == bookId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("", async (CurrentUser user, NopdsDbContext db, CancellationToken ct) =>
        {
            await db.ReadingStates.Where(r => r.UserId == user.Id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        var progress = api.MapGroup("/progress").RequireAuthorization(Policies.User);

        progress.MapGet("/{bookId:long}", async (long bookId, CurrentUser user, NopdsDbContext db, CancellationToken ct) =>
        {
            var s = await db.ReadingStates.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == user.Id && r.BookId == bookId, ct);
            if (s is not null)
            {
                return Results.Ok(new ProgressDto(bookId, s.Location, s.Progress, s.Finished, s.LastOpenedAt));
            }

            // Fall back to KOReader progress synced for the same document.
            var hash = await db.Books.Where(b => b.Id == bookId).Select(b => b.KoreaderHash).FirstOrDefaultAsync(ct);
            var ko = hash is null ? null : await db.KoreaderProgress.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == user.Id && k.Document == hash, ct);
            return ko is null
                ? Results.NoContent()
                : Results.Ok(new ProgressDto(bookId, null, ko.Percentage, ko.Percentage >= 0.999, ko.UpdatedAt));
        });

        progress.MapPut("/{bookId:long}", async (long bookId, ProgressUpdate req, CurrentUser user, NopdsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Books.AnyAsync(b => b.Id == bookId, ct))
            {
                return Results.NotFound();
            }

            var s = await db.ReadingStates.FirstOrDefaultAsync(r => r.UserId == user.Id && r.BookId == bookId, ct);
            if (s is null)
            {
                s = new ReadingState { UserId = user.Id!.Value, BookId = bookId };
                db.ReadingStates.Add(s);
            }

            if (req.Location is not null)
            {
                s.Location = req.Location.Length > 2048 ? req.Location[..2048] : req.Location;
            }

            if (req.Progress is { } p)
            {
                s.Progress = Math.Clamp(p, 0, 1);
            }

            if (req.Finished is { } f)
            {
                s.Finished = f;
            }

            s.LastOpenedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ProgressDto(bookId, s.Location, s.Progress, s.Finished, s.LastOpenedAt));
        });
    }
}
