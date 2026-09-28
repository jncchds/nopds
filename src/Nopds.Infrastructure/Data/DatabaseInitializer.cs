using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Genres;
using Nopds.Infrastructure.Identity;
using Nopds.Infrastructure.Settings;

namespace Nopds.Infrastructure.Data;

/// <summary>Applies migrations, seeds genres and the first admin account.</summary>
public sealed class DatabaseInitializer(
    NopdsDbContext db,
    UserManager<AppUser> users,
    GenreCatalog genres,
    SettingsStore settings,
    ILogger<DatabaseInitializer> log)
{
    public async Task InitializeAsync(bool migrate, string? adminUser, string? adminPassword, CancellationToken ct = default)
    {
        if (migrate)
        {
            log.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(ct);
        }

        await SeedGenresAsync(ct);
        await settings.LoadAsync(ct);

        if (!string.IsNullOrWhiteSpace(adminUser) && !string.IsNullOrWhiteSpace(adminPassword)
            && !await users.Users.AnyAsync(ct))
        {
            var admin = new AppUser { UserName = adminUser, IsAdmin = true };
            var result = await users.CreateAsync(admin, adminPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Cannot create admin: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            log.LogInformation("Created initial admin user {User}", adminUser);
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
