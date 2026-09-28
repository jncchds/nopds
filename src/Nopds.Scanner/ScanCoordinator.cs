using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nopds.Infrastructure.Data;

namespace Nopds.Scanner;

public sealed record ScanRequest(int LibraryId, string? SubPath = null, string Reason = "manual");

/// <summary>
/// Queues scan requests and runs them: one scan per library at a time, a few libraries in parallel.
/// Requests for a library that is already scanning are coalesced into one follow-up run.
/// </summary>
public sealed class ScanCoordinator(
    IServiceScopeFactory scopes,
    IEnumerable<IScanObserver> observers,
    ILogger<ScanCoordinator> log) : BackgroundService
{
    private const int MaxParallelLibraries = 2;

    private readonly Channel<ScanRequest> _queue = Channel.CreateUnbounded<ScanRequest>();
    private readonly ConcurrentDictionary<int, ScanProgress> _status = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<int, ScanRequest> _pending = new();
    private readonly SemaphoreSlim _slots = new(MaxParallelLibraries, MaxParallelLibraries);
    private readonly IScanObserver[] _observers = observers.ToArray();

    public IReadOnlyCollection<ScanProgress> Status => _status.Values.Select(p => p.Snapshot()).ToList();

    public ScanProgress? StatusOf(int libraryId) => _status.TryGetValue(libraryId, out var p) ? p.Snapshot() : null;

    public bool IsRunning(int libraryId) => _running.ContainsKey(libraryId);

    public void Enqueue(ScanRequest request)
    {
        if (_running.ContainsKey(request.LibraryId))
        {
            // Coalesce: identical requests merge; differing ones widen to a full scan.
            _pending.AddOrUpdate(request.LibraryId, request, (_, old) => old.SubPath == request.SubPath ? old : old with { SubPath = null });
            return;
        }

        _status[request.LibraryId] = new ScanProgress { LibraryId = request.LibraryId, SubPath = request.SubPath, State = ScanState.Queued };
        Notify(_status[request.LibraryId], completed: false);
        _queue.Writer.TryWrite(request);
    }

    public bool Cancel(int libraryId)
    {
        _pending.TryRemove(libraryId, out _);
        if (_running.TryGetValue(libraryId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (_running.ContainsKey(request.LibraryId))
            {
                _pending[request.LibraryId] = request;
                continue;
            }

            await _slots.WaitAsync(stoppingToken);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            if (!_running.TryAdd(request.LibraryId, cts))
            {
                _slots.Release();
                cts.Dispose();
                _pending[request.LibraryId] = request;
                continue;
            }

            _ = Task.Run(() => RunOneAsync(request, cts), CancellationToken.None);
        }
    }

    private async Task RunOneAsync(ScanRequest request, CancellationTokenSource cts)
    {
        var progress = new ScanProgress
        {
            LibraryId = request.LibraryId,
            SubPath = request.SubPath,
            State = ScanState.Running,
            StartedAt = DateTimeOffset.UtcNow,
        };
        _status[request.LibraryId] = progress;
        Notify(progress, completed: false);
        log.LogInformation("Scan of library {Library} started ({Reason}{Sub})", request.LibraryId, request.Reason,
            request.SubPath is null ? "" : ", " + request.SubPath);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
            await scanner.RunAsync(request.LibraryId, request.SubPath, progress, p => Notify(p, completed: false), cts.Token);
            progress.State = ScanState.Completed;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            progress.State = ScanState.Cancelled;
            progress.Message = "Cancelled";
        }
        catch (Exception ex)
        {
            progress.State = ScanState.Failed;
            progress.Message = ex.Message;
            log.LogError(ex, "Scan of library {Library} failed", request.LibraryId);
        }
        finally
        {
            progress.FinishedAt = DateTimeOffset.UtcNow;
            progress.CurrentPath = null;
            _running.TryRemove(request.LibraryId, out _);
            _slots.Release();
            cts.Dispose();
        }

        log.LogInformation("Scan of library {Library} {State} in {Elapsed:c}: {Summary}", request.LibraryId, progress.State,
            progress.FinishedAt - progress.StartedAt, progress.Summary());
        await SaveSummaryAsync(progress);
        Notify(progress, completed: true);

        if (_pending.TryRemove(request.LibraryId, out var next))
        {
            Enqueue(next);
        }
    }

    private async Task SaveSummaryAsync(ScanProgress progress)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NopdsDbContext>();
            // Stored as JSON counters so the UI can localize it.
            var summary = System.Text.Json.JsonSerializer.Serialize(new
            {
                state = progress.State.ToString().ToLowerInvariant(),
                added = progress.BooksAdded,
                updated = progress.BooksUpdated,
                skipped = progress.BooksSkipped,
                deleted = progress.BooksDeleted,
                restored = progress.BooksRestored,
                archivesScanned = progress.ArchivesScanned,
                archivesSkipped = progress.ArchivesSkipped,
                errors = progress.Errors,
                message = progress.Message,
            });
            await db.Libraries.Where(l => l.Id == progress.LibraryId).ExecuteUpdateAsync(s => s
                .SetProperty(l => l.LastScanFinishedAt, progress.FinishedAt)
                .SetProperty(l => l.LastScanSummary, summary.Length > 1000 ? summary[..1000] : summary));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cannot store scan summary for library {Library}", progress.LibraryId);
        }
    }

    private void Notify(ScanProgress progress, bool completed)
    {
        var snapshot = progress.Snapshot();
        foreach (var o in _observers)
        {
            try
            {
                if (completed)
                {
                    o.OnCompleted(snapshot);
                }
                else
                {
                    o.OnProgress(snapshot);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Scan observer failed");
            }
        }
    }
}
