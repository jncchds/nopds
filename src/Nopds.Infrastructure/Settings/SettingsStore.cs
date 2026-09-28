using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Data;

namespace Nopds.Infrastructure.Settings;

/// <summary>Caches <see cref="AppSettings"/> stored in the settings table and notifies listeners on change.</summary>
public sealed class SettingsStore(IServiceScopeFactory scopes)
{
    private const string Key = "app";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private volatile AppSettings? _current;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event Action<AppSettings>? Changed;

    public AppSettings Current => _current ?? throw new InvalidOperationException("Settings not loaded yet.");

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NopdsDbContext>();
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        _current = row is null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(row.Value, Json) ?? new AppSettings();
        return _current;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NopdsDbContext>();
            var json = JsonSerializer.Serialize(settings, Json);
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, ct);
            if (row is null)
            {
                db.Settings.Add(new Setting { Key = Key, Value = json });
            }
            else
            {
                row.Value = json;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            _current = settings;
        }
        finally
        {
            _lock.Release();
        }

        Changed?.Invoke(settings);
    }
}
