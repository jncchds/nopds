using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Nopds.Infrastructure.Browse;
using Nopds.Scanner;
using Nopds.Web.Auth;

namespace Nopds.Web.Infrastructure;

/// <summary>Pushes live scan progress to admin clients.</summary>
[Authorize(Policy = Policies.Admin)]
public sealed class ScanHub(ScanCoordinator coordinator) : Hub
{
    public IReadOnlyCollection<ScanProgress> GetStatus() => coordinator.Status;
}

public sealed class ScanHubObserver(IHubContext<ScanHub> hub) : IScanObserver
{
    public void OnProgress(ScanProgress progress) =>
        _ = hub.Clients.All.SendAsync("progress", ToDto(progress));

    public void OnCompleted(ScanProgress progress)
    {
        StatsInvalidation.Invalidate();
        _ = hub.Clients.All.SendAsync("completed", ToDto(progress));
    }

    public static object ToDto(ScanProgress p) => new
    {
        p.LibraryId,
        p.SubPath,
        State = p.State.ToString().ToLowerInvariant(),
        p.StartedAt,
        p.FinishedAt,
        p.FilesSeen,
        p.BooksAdded,
        p.BooksUpdated,
        p.BooksSkipped,
        p.BooksDeleted,
        p.BooksRestored,
        p.ArchivesScanned,
        p.ArchivesSkipped,
        p.Errors,
        p.CurrentPath,
        p.Message,
    };
}
