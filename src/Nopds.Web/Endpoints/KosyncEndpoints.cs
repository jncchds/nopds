using Microsoft.EntityFrameworkCore;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Identity;

namespace Nopds.Web.Endpoints;

/// <summary>
/// KOReader progress sync (kosync) protocol. Configure KOReader's "Progress sync" with this server's
/// /kosync URL, your .NET OPDS user name and the sync password set in the profile page.
/// Account registration through KOReader is disabled: accounts are managed in .NET OPDS.
/// </summary>
public static class KosyncEndpoints
{
    public sealed record ProgressBody(string? Document, string? Progress, double? Percentage, string? Device, string? Device_id);

    public static void MapKosync(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/kosync");

        g.MapPost("/users/create", () => Results.Json(new { message = "Registration is disabled. Create the account in .NET OPDS." }, statusCode: 402));

        g.MapGet("/users/auth", async (HttpContext http, NopdsDbContext db, CancellationToken ct) =>
            await AuthAsync(http, db, ct) is null
                ? Results.Json(new { message = "Unauthorized" }, statusCode: 401)
                : Results.Ok(new { authorized = "OK" }));

        g.MapPut("/syncs/progress", async (ProgressBody body, HttpContext http, NopdsDbContext db, CancellationToken ct) =>
        {
            var user = await AuthAsync(http, db, ct);
            if (user is null)
            {
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            }

            if (string.IsNullOrEmpty(body.Document) || body.Document.Length > 64 || body.Progress is null)
            {
                return Results.Json(new { message = "Invalid request" }, statusCode: 403);
            }

            var now = DateTimeOffset.UtcNow;
            var row = await db.KoreaderProgress.FirstOrDefaultAsync(p => p.UserId == user.Id && p.Document == body.Document, ct);
            if (row is null)
            {
                row = new KoreaderProgress { UserId = user.Id, Document = body.Document, Progress = body.Progress };
                db.KoreaderProgress.Add(row);
            }

            row.Progress = body.Progress.Length > 2048 ? body.Progress[..2048] : body.Progress;
            row.Percentage = Math.Clamp(body.Percentage ?? 0, 0, 1);
            row.Device = body.Device;
            row.DeviceId = body.Device_id;
            row.UpdatedAt = now;

            // Mirror into the shelf/progress of the matching book, if we know the document hash.
            var bookId = await db.Books.Where(b => b.KoreaderHash == body.Document).Select(b => (long?)b.Id).FirstOrDefaultAsync(ct);
            if (bookId is { } id)
            {
                var state = await db.ReadingStates.FirstOrDefaultAsync(r => r.UserId == user.Id && r.BookId == id, ct);
                if (state is null)
                {
                    db.ReadingStates.Add(new ReadingState { UserId = user.Id, BookId = id, Progress = row.Percentage, LastOpenedAt = now, Finished = row.Percentage >= 0.999 });
                }
                else
                {
                    state.Progress = row.Percentage;
                    state.LastOpenedAt = now;
                    state.Finished = state.Finished || row.Percentage >= 0.999;
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { document = body.Document, timestamp = now.ToUnixTimeSeconds() });
        });

        g.MapGet("/syncs/progress/{document}", async (string document, HttpContext http, NopdsDbContext db, CancellationToken ct) =>
        {
            var user = await AuthAsync(http, db, ct);
            if (user is null)
            {
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            }

            var row = await db.KoreaderProgress.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id && p.Document == document, ct);
            return row is null
                ? Results.Ok(new { })
                : Results.Ok(new
                {
                    document = row.Document,
                    progress = row.Progress,
                    percentage = row.Percentage,
                    device = row.Device,
                    device_id = row.DeviceId,
                    timestamp = row.UpdatedAt.ToUnixTimeSeconds(),
                });
        });

        g.MapGet("/healthcheck", () => Results.Ok(new { state = "OK" }));
    }

    private static async Task<AppUser?> AuthAsync(HttpContext http, NopdsDbContext db, CancellationToken ct)
    {
        var name = http.Request.Headers["x-auth-user"].ToString();
        var key = http.Request.Headers["x-auth-key"].ToString();
        if (name.Length == 0 || key.Length == 0)
        {
            return null;
        }

        var normalized = name.ToUpperInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
        if (user?.KosyncKeyHash is null || (user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow))
        {
            return null;
        }

        var hash = KosyncKeys.HashKey(key);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(hash), System.Text.Encoding.ASCII.GetBytes(user.KosyncKeyHash))
            ? user
            : null;
    }
}
