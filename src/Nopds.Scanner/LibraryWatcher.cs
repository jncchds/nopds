using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Data;

namespace Nopds.Scanner;

/// <summary>
/// Watches library folders (when enabled) and queues partial rescans of changed subtrees after a
/// 5-second quiet period. Network mounts may not deliver events; scheduled scans remain the safety net.
/// </summary>
public sealed class LibraryWatcher(
    IDbContextFactory<NopdsDbContext> dbFactory,
    ScanCoordinator coordinator,
    ILogger<LibraryWatcher> log) : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);

    private sealed class Watch(Library library, FileSystemWatcher watcher) : IDisposable
    {
        public Library Library { get; } = library;
        public FileSystemWatcher Watcher { get; } = watcher;
        public ConcurrentDictionary<string, DateTimeOffset> Dirty { get; } = new(StringComparer.Ordinal);

        public void Dispose() => Watcher.Dispose();
    }

    private readonly Dictionary<int, Watch> _watches = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var lastSync = DateTimeOffset.MinValue;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (DateTimeOffset.UtcNow - lastSync > TimeSpan.FromSeconds(30))
                {
                    await SyncAsync(stoppingToken);
                    lastSync = DateTimeOffset.UtcNow;
                }

                Flush();
            }
        }
        finally
        {
            foreach (var w in _watches.Values)
            {
                w.Dispose();
            }
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        List<Library> libs;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            libs = await db.Libraries.AsNoTracking().Where(l => l.Enabled && l.WatchEnabled).ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogDebug(ex, "Watcher sync skipped");
            return;
        }

        foreach (var id in _watches.Keys.Except(libs.Select(l => l.Id)).ToList())
        {
            _watches[id].Dispose();
            _watches.Remove(id);
        }

        foreach (var lib in libs)
        {
            if (_watches.TryGetValue(lib.Id, out var existing) && existing.Library.RootPath == lib.RootPath)
            {
                continue;
            }

            existing?.Dispose();
            _watches.Remove(lib.Id);
            if (!Directory.Exists(lib.RootPath))
            {
                continue;
            }

            try
            {
                var fsw = new FileSystemWatcher(lib.RootPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };
                var watch = new Watch(lib, fsw);
                void OnChange(string path) => watch.Dirty[DirtyKey(lib.RootPath, path)] = DateTimeOffset.UtcNow;
                fsw.Created += (_, e) => OnChange(e.FullPath);
                fsw.Changed += (_, e) => OnChange(e.FullPath);
                fsw.Deleted += (_, e) => OnChange(e.FullPath);
                fsw.Renamed += (_, e) =>
                {
                    OnChange(e.OldFullPath);
                    OnChange(e.FullPath);
                };
                fsw.Error += (_, e) =>
                {
                    log.LogWarning(e.GetException(), "Watcher overflow for library {Library}; queuing a full scan", lib.Id);
                    watch.Dirty["."] = DateTimeOffset.UtcNow;
                };
                fsw.EnableRaisingEvents = true;
                _watches[lib.Id] = watch;
                log.LogInformation("Watching library {Library} at {Root}", lib.Id, lib.RootPath);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
            {
                log.LogWarning(ex, "Cannot watch library {Library}", lib.Id);
            }
        }
    }

    /// <summary>Changes are grouped by the parent directory of the changed file.</summary>
    private static string DirtyKey(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var slash = rel.LastIndexOf('/');
        return slash < 0 ? "." : rel[..slash];
    }

    private void Flush()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var watch in _watches.Values)
        {
            var ready = watch.Dirty.Where(kv => now - kv.Value >= Debounce).Select(kv => kv.Key).ToList();
            if (ready.Count == 0)
            {
                continue;
            }

            foreach (var key in ready)
            {
                watch.Dirty.TryRemove(key, out _);
            }

            // Collapse to the common ancestor: one partial scan per quiet period.
            var sub = CommonPrefix(ready);
            coordinator.Enqueue(new ScanRequest(watch.Library.Id, sub == "." ? null : sub, "watcher"));
        }
    }

    private static string CommonPrefix(List<string> paths)
    {
        if (paths.Contains("."))
        {
            return ".";
        }

        var parts = paths[0].Split('/');
        var len = parts.Length;
        foreach (var p in paths.Skip(1))
        {
            var q = p.Split('/');
            len = Math.Min(len, q.Length);
            for (var i = 0; i < len; i++)
            {
                if (q[i] != parts[i])
                {
                    len = i;
                    break;
                }
            }
        }

        return len == 0 ? "." : string.Join('/', parts[..len]);
    }
}
