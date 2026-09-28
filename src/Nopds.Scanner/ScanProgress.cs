namespace Nopds.Scanner;

public enum ScanState
{
    Idle,
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Live counters of a library scan (pushed to the admin UI).</summary>
public sealed class ScanProgress
{
    public int LibraryId { get; init; }
    public string? SubPath { get; init; }
    public ScanState State { get; set; } = ScanState.Queued;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public int FilesSeen;
    public int BooksAdded;
    public int BooksUpdated;
    public int BooksSkipped;
    public int BooksDeleted;
    public int BooksRestored;
    public int ArchivesScanned;
    public int ArchivesSkipped;
    public int Errors;

    public string? CurrentPath { get; set; }
    public string? Message { get; set; }

    public string Summary() =>
        $"added {BooksAdded}, updated {BooksUpdated}, skipped {BooksSkipped}, deleted {BooksDeleted}, restored {BooksRestored}, " +
        $"archives {ArchivesScanned} scanned / {ArchivesSkipped} skipped, errors {Errors}";

    public ScanProgress Snapshot() => (ScanProgress)MemberwiseClone();
}

/// <summary>Receives scan progress; implemented by the web host (SignalR, caches).</summary>
public interface IScanObserver
{
    void OnProgress(ScanProgress progress);

    void OnCompleted(ScanProgress progress);
}
