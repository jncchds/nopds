using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nopds.Infrastructure.Data;

namespace Nopds.Scanner;

/// <summary>Triggers scans according to each library's cron expression (replaces APScheduler).</summary>
public sealed class ScanScheduler(
    IDbContextFactory<NopdsDbContext> dbFactory,
    ScanCoordinator coordinator,
    TimeProvider clock,
    ILogger<ScanScheduler> log) : BackgroundService
{
    private readonly Dictionary<int, (string Cron, DateTimeOffset? Next)> _plan = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Scan scheduler tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var libs = await db.Libraries.AsNoTracking().Where(l => l.Enabled && l.ScanCron != null)
            .Select(l => new { l.Id, l.ScanCron }).ToListAsync(ct);
        var now = clock.GetUtcNow();

        foreach (var id in _plan.Keys.Except(libs.Select(l => l.Id)).ToList())
        {
            _plan.Remove(id);
        }

        foreach (var lib in libs)
        {
            if (!_plan.TryGetValue(lib.Id, out var entry) || entry.Cron != lib.ScanCron)
            {
                entry = (lib.ScanCron!, NextAfter(lib.ScanCron!, now));
                _plan[lib.Id] = entry;
                continue;
            }

            if (entry.Next is { } due && due <= now)
            {
                coordinator.Enqueue(new ScanRequest(lib.Id, Reason: "schedule"));
                _plan[lib.Id] = (entry.Cron, NextAfter(entry.Cron, now));
            }
        }
    }

    private DateTimeOffset? NextAfter(string cron, DateTimeOffset now)
    {
        try
        {
            var expr = CronExpression.Parse(cron, cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
            return expr.GetNextOccurrence(now, TimeZoneInfo.Local);
        }
        catch (CronFormatException ex)
        {
            log.LogWarning("Invalid cron expression '{Cron}': {Error}", cron, ex.Message);
            return null;
        }
    }

    public static bool IsValidCron(string cron)
    {
        try
        {
            CronExpression.Parse(cron, cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
