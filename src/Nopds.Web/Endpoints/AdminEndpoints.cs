using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Identity;
using Nopds.Infrastructure.Settings;
using Nopds.Scanner;
using Nopds.Web.Auth;
using Nopds.Web.Infrastructure;

namespace Nopds.Web.Endpoints;

public static class AdminEndpoints
{
    public sealed record LibraryDto(
        int Id, string Name, string RootPath, bool Enabled, string[] Extensions, bool ScanZip, string ZipCodepage,
        bool InpxEnabled, bool InpxSkipUnchanged, bool InpxTestZip, bool InpxTestFiles, bool WatchEnabled, string? ScanCron,
        bool DeleteLogical, bool HashContent, DateTimeOffset? LastScanStartedAt, DateTimeOffset? LastScanFinishedAt, string? LastScanSummary,
        int Books, bool RootExists);

    public sealed record LibraryInput(
        string Name, string RootPath, bool? Enabled, string[]? Extensions, bool? ScanZip, string? ZipCodepage,
        bool? InpxEnabled, bool? InpxSkipUnchanged, bool? InpxTestZip, bool? InpxTestFiles, bool? WatchEnabled, string? ScanCron,
        bool? DeleteLogical, bool? HashContent);

    public sealed record UserInput(string? UserName, string? Password, bool? IsAdmin, int[]? AllowedLibraryIds, bool? AllLibraries, bool? Locked, bool? Approved);

    /// <param name="Sso">Provider name when the account is linked to single sign-on.</param>
    public sealed record AdminUserDto(Guid Id, string UserName, bool IsAdmin, int[]? AllowedLibraryIds, bool Locked, bool Approved, DateTimeOffset CreatedAt, string? TelegramUsername, string? Email, string? Sso);

    public sealed record DirEntry(string Name, string Path);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin").RequireAuthorization(Policies.Admin);

        // ---- libraries
        g.MapGet("/libraries", async (NopdsDbContext db, CancellationToken ct) =>
        {
            var counts = await db.Books.Where(b => b.DeletedAt == null).GroupBy(b => b.LibraryId)
                .Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var libs = await db.Libraries.AsNoTracking().OrderBy(l => l.Name).ToListAsync(ct);
            return libs.Select(l => ToDto(l, counts.GetValueOrDefault(l.Id)));
        });

        g.MapPost("/libraries", async (LibraryInput input, NopdsDbContext db, CancellationToken ct) =>
        {
            var lib = new Library { Name = input.Name.Trim(), RootPath = NormalizeRoot(input.RootPath) };
            if (Validate(input, lib.RootPath) is { } error)
            {
                return error;
            }

            Apply(lib, input);
            db.Libraries.Add(lib);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "A library with this name already exists." });
            }

            return Results.Created($"/api/v1/admin/libraries/{lib.Id}", ToDto(lib, 0));
        });

        g.MapPut("/libraries/{id:int}", async (int id, LibraryInput input, NopdsDbContext db, CancellationToken ct) =>
        {
            var lib = await db.Libraries.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (lib is null)
            {
                return Results.NotFound();
            }

            var root = NormalizeRoot(input.RootPath);
            if (Validate(input, root) is { } error)
            {
                return error;
            }

            lib.Name = input.Name.Trim();
            lib.RootPath = root;
            Apply(lib, input);
            await db.SaveChangesAsync(ct);
            StatsInvalidation.Invalidate();
            return Results.Ok(ToDto(lib, await db.Books.CountAsync(b => b.LibraryId == id && b.DeletedAt == null, ct)));
        });

        g.MapDelete("/libraries/{id:int}", async (int id, NopdsDbContext db, ScanCoordinator scans, CancellationToken ct) =>
        {
            scans.Cancel(id);
            // Bulk deletes: much faster than cascading through the change tracker.
            await db.Books.Where(b => b.LibraryId == id).ExecuteDeleteAsync(ct);
            await db.Catalogs.Where(c => c.LibraryId == id).ExecuteDeleteAsync(ct);
            await db.Libraries.Where(l => l.Id == id).ExecuteDeleteAsync(ct);
            await LibraryScanner.CleanupAsync(db, id, ct);
            StatsInvalidation.Invalidate();
            return Results.NoContent();
        });

        // ---- scanning
        g.MapGet("/scan", (ScanCoordinator scans) => scans.Status.Select(ScanHubObserver.ToDto));

        g.MapPost("/libraries/{id:int}/scan", async (int id, string? path, NopdsDbContext db, ScanCoordinator scans, CancellationToken ct) =>
        {
            if (!await db.Libraries.AnyAsync(l => l.Id == id, ct))
            {
                return Results.NotFound();
            }

            scans.Enqueue(new ScanRequest(id, path, "admin"));
            return Results.Accepted();
        });

        g.MapDelete("/libraries/{id:int}/scan", (int id, ScanCoordinator scans) =>
            scans.Cancel(id) ? Results.NoContent() : Results.NotFound());

        // ---- settings
        g.MapGet("/settings", (SettingsStore settings) => settings.Current);

        g.MapPut("/settings", async (AppSettings input, SettingsStore settings, CancellationToken ct) =>
        {
            var s = input with
            {
                MaxItems = Math.Clamp(input.MaxItems, 10, 500),
                SplitItems = Math.Clamp(input.SplitItems, 10, 10000),
                PreferredFormats = input.PreferredFormats.Select(f => f.Trim().ToLowerInvariant()).Where(f => f.Length > 0).Distinct().ToArray(),
                Telegram = input.Telegram with { MaxItems = Math.Clamp(input.Telegram.MaxItems, 1, 50) },
            };
            await settings.SaveAsync(s, ct);
            return Results.Ok(s);
        });

        // ---- users
        g.MapGet("/users", async (NopdsDbContext db, CancellationToken ct) =>
        {
            var logins = await db.UserLogins.AsNoTracking().GroupBy(l => l.UserId)
                .Select(x => new { x.Key, Name = x.Select(l => l.ProviderDisplayName ?? l.LoginProvider).First() })
                .ToDictionaryAsync(x => x.Key, x => x.Name, ct);
            // Pending accounts first so they are not missed.
            return (await db.Users.AsNoTracking().OrderBy(u => u.IsApproved).ThenBy(u => u.UserName).ToListAsync(ct))
                .Select(u => ToDto(u, logins.GetValueOrDefault(u.Id)));
        });

        g.MapPost("/users", async (UserInput input, UserManager<AppUser> users) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["userName"] = ["User name and password are required."] });
            }

            var u = new AppUser
            {
                UserName = input.UserName.Trim(),
                IsAdmin = input.IsAdmin == true,
                AllowedLibraryIds = input.AllLibraries == false ? input.AllowedLibraryIds ?? [] : null,
            };
            var r = await users.CreateAsync(u, input.Password);
            return r.Succeeded ? Results.Ok(ToDto(u)) : Problem(r);
        });

        g.MapPut("/users/{id:guid}", async (Guid id, UserInput input, UserManager<AppUser> users, CurrentUser me, TokenService tokens, CancellationToken ct) =>
        {
            var u = await users.FindByIdAsync(id.ToString());
            if (u is null)
            {
                return Results.NotFound();
            }

            if (input.UserName is { Length: > 0 } name && name != u.UserName)
            {
                var r = await users.SetUserNameAsync(u, name.Trim());
                if (!r.Succeeded)
                {
                    return Problem(r);
                }
            }

            if (input.IsAdmin is { } admin && !(u.Id == me.Id && !admin))
            {
                u.IsAdmin = admin;
            }

            if (input.Approved is { } approved && u.Id != me.Id)
            {
                u.IsApproved = approved;
                if (!approved)
                {
                    await tokens.RevokeAllAsync(u.Id, ct);
                }
            }

            if (input.AllLibraries is { } all)
            {
                u.AllowedLibraryIds = all ? null : input.AllowedLibraryIds ?? [];
            }

            if (input.Locked is { } locked && u.Id != me.Id)
            {
                u.LockoutEnabled = true;
                u.LockoutEnd = locked ? DateTimeOffset.MaxValue : null;
                if (locked)
                {
                    await tokens.RevokeAllAsync(u.Id, ct);
                }
            }

            if (!string.IsNullOrEmpty(input.Password))
            {
                await users.RemovePasswordAsync(u);
                var r = await users.AddPasswordAsync(u, input.Password);
                if (!r.Succeeded)
                {
                    return Problem(r);
                }

                await tokens.RevokeAllAsync(u.Id, ct);
            }

            await users.UpdateAsync(u);
            return Results.Ok(ToDto(u));
        });

        g.MapDelete("/users/{id:guid}", async (Guid id, UserManager<AppUser> users, CurrentUser me) =>
        {
            if (id == me.Id)
            {
                return Results.BadRequest(new { error = "You cannot delete your own account." });
            }

            var u = await users.FindByIdAsync(id.ToString());
            if (u is null)
            {
                return Results.NotFound();
            }

            await users.DeleteAsync(u);
            return Results.NoContent();
        });

        // ---- server file system (to pick library folders)
        g.MapGet("/fs", (string? path) =>
        {
            var dir = string.IsNullOrWhiteSpace(path) ? "/" : Path.GetFullPath(path);
            if (!Directory.Exists(dir))
            {
                return Results.NotFound();
            }

            try
            {
                var entries = new DirectoryInfo(dir)
                    .EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true })
                    .Where(d => !d.Name.StartsWith('.'))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(500)
                    .Select(d => new DirEntry(d.Name, d.FullName))
                    .ToList();
                return Results.Ok(new { path = dir, parent = Path.GetDirectoryName(dir), entries });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        });

        // ---- logs
        g.MapGet("/logs", async (IOptions<NopdsOptions> options, int? lines, CancellationToken ct) =>
        {
            var dir = Path.Combine(options.Value.DataDir, "logs");
            var file = Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
            if (file is null)
            {
                return Results.Ok(Array.Empty<string>());
            }

            // Serilog keeps the file open; share it for reading.
            await using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var all = (await reader.ReadToEndAsync(ct)).Split('\n');
            var n = Math.Clamp(lines ?? 300, 10, 5000);
            return Results.Ok(all.Skip(Math.Max(0, all.Length - n)).Where(l => l.Length > 0));
        });

        g.MapPost("/maintenance/cleanup", async (NopdsDbContext db, CancellationToken ct) =>
        {
            foreach (var id in await db.Libraries.Select(l => l.Id).ToListAsync(ct))
            {
                await LibraryScanner.CleanupAsync(db, id, ct);
            }

            await db.RefreshTokens.Where(t => t.ExpiresAt < DateTimeOffset.UtcNow || t.RevokedAt != null).ExecuteDeleteAsync(ct);
            StatsInvalidation.Invalidate();
            return Results.NoContent();
        });
    }

    private static string NormalizeRoot(string path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path.Trim());

    private static IResult? Validate(LibraryInput input, string root)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors["name"] = ["Name is required."];
        }

        if (root.Length == 0 || !Directory.Exists(root))
        {
            errors["rootPath"] = ["Folder does not exist on the server."];
        }

        if (!string.IsNullOrWhiteSpace(input.ScanCron) && !ScanScheduler.IsValidCron(input.ScanCron))
        {
            errors["scanCron"] = ["Invalid cron expression."];
        }

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private static void Apply(Library l, LibraryInput i)
    {
        l.Enabled = i.Enabled ?? l.Enabled;
        if (i.Extensions is { Length: > 0 } ext)
        {
            l.Extensions = ext.Select(e => e.Trim().TrimStart('.').ToLowerInvariant()).Where(e => e.Length > 0).Distinct().ToArray();
        }

        l.ScanZip = i.ScanZip ?? l.ScanZip;
        l.ZipCodepage = string.IsNullOrWhiteSpace(i.ZipCodepage) ? l.ZipCodepage : i.ZipCodepage.Trim();
        l.InpxEnabled = i.InpxEnabled ?? l.InpxEnabled;
        l.InpxSkipUnchanged = i.InpxSkipUnchanged ?? l.InpxSkipUnchanged;
        l.InpxTestZip = i.InpxTestZip ?? l.InpxTestZip;
        l.InpxTestFiles = i.InpxTestFiles ?? l.InpxTestFiles;
        l.WatchEnabled = i.WatchEnabled ?? l.WatchEnabled;
        l.ScanCron = string.IsNullOrWhiteSpace(i.ScanCron) ? null : i.ScanCron.Trim();
        l.DeleteLogical = i.DeleteLogical ?? l.DeleteLogical;
        l.HashContent = i.HashContent ?? l.HashContent;
    }

    private static LibraryDto ToDto(Library l, int books) => new(
        l.Id, l.Name, l.RootPath, l.Enabled, l.Extensions, l.ScanZip, l.ZipCodepage, l.InpxEnabled, l.InpxSkipUnchanged, l.InpxTestZip,
        l.InpxTestFiles, l.WatchEnabled, l.ScanCron, l.DeleteLogical, l.HashContent, l.LastScanStartedAt, l.LastScanFinishedAt, l.LastScanSummary,
        books, Directory.Exists(l.RootPath));

    private static AdminUserDto ToDto(AppUser u, string? sso = null) => new(
        u.Id, u.UserName ?? "", u.IsAdmin, u.AllowedLibraryIds, u.LockoutEnd is { } e && e > DateTimeOffset.UtcNow, u.IsApproved, u.CreatedAt,
        u.TelegramUsername, u.Email, sso);

    private static IResult Problem(IdentityResult r) =>
        Results.ValidationProblem(r.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}
