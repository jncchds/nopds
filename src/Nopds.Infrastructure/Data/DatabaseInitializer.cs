using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Genres;
using Nopds.Infrastructure.Identity;
using Nopds.Infrastructure.Settings;

namespace Nopds.Infrastructure.Data;

/// <summary>Applies migrations, seeds genres and the main admin account.</summary>
public sealed class DatabaseInitializer(
    NopdsDbContext db,
    UserManager<AppUser> users,
    GenreCatalog genres,
    SettingsStore settings,
    ILogger<DatabaseInitializer> log)
{
    /// <param name="forceAdmin">Create or repair the main admin even when other users exist.</param>
    public async Task InitializeAsync(bool migrate, string? adminUser, string? adminPassword, bool forceAdmin = false, CancellationToken ct = default)
    {
        if (migrate)
        {
            log.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(ct);
        }

        await SeedGenresAsync(ct);
        await settings.LoadAsync(ct);

        if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPassword))
        {
            if (forceAdmin)
            {
                log.LogWarning("AdminForce is set but AdminUser / AdminPassword are empty; nothing to do");
            }

            return;
        }

        var existing = forceAdmin ? await users.FindByNameAsync(adminUser) : null;
        if (existing is not null)
        {
            await RepairAdminAsync(existing, adminPassword, ct);
        }
        else if (forceAdmin || !await users.Users.AnyAsync(ct))
        {
            var admin = new AppUser { UserName = adminUser, IsAdmin = true };
            Check(await users.CreateAsync(admin, adminPassword), "create admin");
            log.LogInformation("Created admin user {User}", adminUser);
        }
    }

    private async Task RepairAdminAsync(AppUser admin, string password, CancellationToken ct)
    {
        var passwordChanged = !await users.CheckPasswordAsync(admin, password);
        if (passwordChanged)
        {
            if (admin.PasswordHash is not null)
            {
                Check(await users.RemovePasswordAsync(admin), "reset admin password");
            }

            Check(await users.AddPasswordAsync(admin, password), "reset admin password");
        }

        admin.IsAdmin = true;
        admin.IsApproved = true;
        admin.LockoutEnd = null;
        admin.AccessFailedCount = 0;
        Check(await users.UpdateAsync(admin), "update admin");

        // Only a real password change ends existing sessions, so leaving the flag on does not log everyone out on restart.
        if (passwordChanged)
        {
            await db.RefreshTokens.Where(t => t.UserId == admin.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
        }

        log.LogWarning("AdminForce: restored admin user {User}{Password}", admin.UserName, passwordChanged ? " and reset its password" : "");
    }

    private static void Check(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Cannot {action}: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task SeedGenresAsync(CancellationToken ct)
    {
        var existing = await db.Genres.ToDictionaryAsync(g => g.Code, StringComparer.OrdinalIgnoreCase, ct);
        var added = 0;
        foreach (var entry in genres.Genres)
        {
            if (existing.TryGetValue(entry.Code, out var g))
            {
                // Genres first seen during a scan are "unknown" until the catalog learns them.
                if (g.Section != entry.Section)
                {
                    g.Section = entry.Section;
                }
            }
            else
            {
                db.Genres.Add(new Genre { Code = entry.Code, Section = entry.Section });
                added++;
            }
        }

        await db.SaveChangesAsync(ct);
        if (added > 0)
        {
            log.LogInformation("Seeded {Count} genres", added);
        }
    }
}
