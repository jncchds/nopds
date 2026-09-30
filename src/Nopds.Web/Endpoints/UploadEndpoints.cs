using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nopds.Domain.Entities;
using Nopds.Formats;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Uploads;
using Nopds.Scanner;
using Nopds.Web.Auth;
using Nopds.Web.Infrastructure;

namespace Nopds.Web.Endpoints;

/// <summary>Book uploads into the shared upload library, and their privacy.</summary>
public static class UploadEndpoints
{
    /// <param name="BookId">The scanned book; null until the scan has picked the file up (or when the file is missing).</param>
    public sealed record UploadDto(long Id, string FileName, string RelPath, long FileSize, bool IsPrivate, DateTimeOffset UploadedAt,
        string? UploadedBy, bool Mine, long? BookId, string? Title, bool Missing);

    public sealed record PrivacyInput(bool IsPrivate);

    /// <param name="maxMegabytes">Upper bound for one request (all files of one upload together).</param>
    public static void MapUploadEndpoints(this IEndpointRouteBuilder api, int maxMegabytes)
    {
        var maxRequestBytes = Math.Max(1, maxMegabytes) * 1024L * 1024;
        var g = api.MapGroup("/uploads").RequireAuthorization(Policies.User);

        // Own uploads; admins may list everyone's with ?all=true.
        g.MapGet("", async (bool? all, CurrentUser user, NopdsDbContext db, CancellationToken ct) =>
        {
            var q = db.Uploads.AsNoTracking();
            if (!(all == true && user.IsAdmin))
            {
                q = q.Where(u => u.UserId == user.Id);
            }

            var rows = await q.OrderByDescending(u => u.UploadedAt).Take(500).ToListAsync(ct);
            return await ToDtosAsync(db, rows, user, ct);
        });

        g.MapPost("", async ([FromForm] IFormFileCollection files, [FromForm] bool? isPrivate, CurrentUser user, UploadLibrary uploads,
            NopdsDbContext db, ScanCoordinator scans, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (uploads.Id is not { } libraryId || uploads.RootPath is not { } root)
            {
                return Results.NotFound();
            }

            if (files.Count == 0)
            {
                return Results.BadRequest(new { error = "No file." });
            }

            var accepted = await AcceptedExtensionsAsync(db, libraryId, ct);
            foreach (var f in files)
            {
                var ext = Path.GetExtension(f.FileName).TrimStart('.').ToLowerInvariant();
                if (!accepted.Contains(ext))
                {
                    return Results.BadRequest(new { error = $"Unsupported file type: {f.FileName}" });
                }

                if (f.Length == 0)
                {
                    return Results.BadRequest(new { error = $"File is empty: {f.FileName}" });
                }
            }

            var log = loggers.CreateLogger(nameof(UploadEndpoints));
            var saved = new List<Upload>();
            foreach (var f in files)
            {
                saved.Add(await SaveAsync(db, libraryId, root, f, user.Id!.Value, isPrivate == true, ct));
                log.LogInformation("User {User} uploaded {File} ({Privacy})", user.Name, saved[^1].RelPath, isPrivate == true ? "private" : "public");
            }

            foreach (var u in saved)
            {
                scans.Enqueue(new ScanRequest(libraryId, u.RelPath, "upload"));
            }

            StatsInvalidation.Invalidate();
            return Results.Ok(await ToDtosAsync(db, saved, user, ct));
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(maxRequestBytes))
        .WithFormOptions(multipartBodyLengthLimit: maxRequestBytes);

        // Uploader or admin toggles who sees the book.
        g.MapPut("/{id:long}", async (long id, PrivacyInput input, CurrentUser user, NopdsDbContext db, CancellationToken ct) =>
        {
            var upload = await db.Uploads.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (upload is null || (!user.IsAdmin && upload.UserId != user.Id))
            {
                return Results.NotFound();
            }

            upload.IsPrivate = input.IsPrivate;
            await db.SaveChangesAsync(ct);
            StatsInvalidation.Invalidate();
            return Results.Ok((await ToDtosAsync(db, [upload], user, ct))[0]);
        });
    }

    /// <summary>File types the upload library indexes (plus ZIP archives when it scans them).</summary>
    public static async Task<string[]> AcceptedExtensionsAsync(NopdsDbContext db, int libraryId, CancellationToken ct)
    {
        var lib = await db.Libraries.AsNoTracking().Where(l => l.Id == libraryId).Select(l => new { l.Extensions, l.ScanZip }).FirstOrDefaultAsync(ct);
        if (lib is null)
        {
            return [];
        }

        var list = lib.Extensions.Select(e => e.TrimStart('.').ToLowerInvariant()).ToList();
        if (lib.ScanZip)
        {
            list.Add("zip");
        }

        return list.Distinct().ToArray();
    }

    private static async Task<Upload> SaveAsync(NopdsDbContext db, int libraryId, string root, IFormFile file, Guid userId, bool isPrivate, CancellationToken ct)
    {
        var name = SafeFileName(file.FileName);
        var temp = Path.Combine(root, $".upload-{Guid.NewGuid():N}.part");
        try
        {
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.CopyToAsync(target, ct);
            }

            for (var attempt = 1; ; attempt++)
            {
                var candidate = attempt == 1
                    ? name
                    : $"{Path.GetFileNameWithoutExtension(name)} ({attempt}){Path.GetExtension(name)}";
                var full = BookStorage.FullPath(root, candidate);
                if (File.Exists(full) || await db.Uploads.AnyAsync(u => u.LibraryId == libraryId && u.RelPath == candidate, ct))
                {
                    continue;
                }

                // Ownership is recorded before the file becomes visible, so no scan can see it as a public book first.
                var upload = new Upload
                {
                    LibraryId = libraryId, RelPath = candidate, OriginalName = Truncate(Path.GetFileName(file.FileName), 512), UserId = userId,
                    IsPrivate = isPrivate, FileSize = file.Length,
                };
                db.Uploads.Add(upload);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException) when (attempt < 100)
                {
                    db.Uploads.Entry(upload).State = EntityState.Detached;
                    continue;
                }

                try
                {
                    File.Move(temp, full);
                }
                catch (IOException) when (attempt < 100)
                {
                    db.Uploads.Remove(upload);
                    await db.SaveChangesAsync(CancellationToken.None);
                    continue;
                }

                return upload;
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>Keeps only the file name, without path parts, control or reserved characters.</summary>
    public static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var chars = name.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray();
        name = new string(chars).Trim().TrimStart('.', ' ');
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.Length == 0)
        {
            stem = "book";
        }

        if (stem.Length > 150)
        {
            stem = stem[..150].TrimEnd();
        }

        return stem + ext.ToLowerInvariant();
    }

    private static async Task<List<UploadDto>> ToDtosAsync(NopdsDbContext db, List<Upload> rows, CurrentUser user, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var libs = rows.Select(r => r.LibraryId).Distinct().ToList();
        var paths = rows.Select(r => r.RelPath).Distinct().ToList();
        var books = await db.Books.AsNoTracking().Where(b => libs.Contains(b.LibraryId) && paths.Contains(b.RelPath))
            .OrderBy(b => b.Id)
            .Select(b => new { b.Id, b.LibraryId, b.RelPath, b.Title, Deleted = b.DeletedAt != null })
            .ToListAsync(ct);
        var byPath = books.GroupBy(b => (b.LibraryId, b.RelPath)).ToDictionary(x => x.Key, x => x.OrderBy(b => b.Deleted).First());
        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);

        return rows.Select(r =>
        {
            byPath.TryGetValue((r.LibraryId, r.RelPath), out var b);
            return new UploadDto(r.Id, r.OriginalName, r.RelPath, r.FileSize, r.IsPrivate, r.UploadedAt,
                r.UserId is { } uid ? names.GetValueOrDefault(uid) : null, r.UserId is not null && r.UserId == user.Id,
                b is { Deleted: false } ? b.Id : null, b?.Title, b?.Deleted == true);
        }).ToList();
    }
}
