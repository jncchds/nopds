namespace Nopds.Domain.Entities;

/// <summary>A book collection rooted at its own folder, scanned with its own options.</summary>
public class Library
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string RootPath { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Extensions (without dot) that are indexed.</summary>
    public string[] Extensions { get; set; } = ["fb2", "epub", "mobi", "azw3", "pdf", "djvu", "txt", "rtf", "doc", "docx", "odt", "cbz"];

    public bool ScanZip { get; set; } = true;

    /// <summary>Codepage used for ZIP entry names without the UTF-8 flag (e.g. cp866, cp1251, cp437).</summary>
    public string ZipCodepage { get; set; } = "cp866";

    public bool InpxEnabled { get; set; } = true;
    public bool InpxSkipUnchanged { get; set; } = true;
    public bool InpxTestZip { get; set; }
    public bool InpxTestFiles { get; set; }

    /// <summary>Watch the folder for changes and rescan affected subtrees.</summary>
    public bool WatchEnabled { get; set; }

    /// <summary>Cron expression (5 fields) for scheduled scans; null disables scheduling.</summary>
    public string? ScanCron { get; set; } = "0 0,12 * * *";

    /// <summary>When true, missing books are soft-deleted instead of removed.</summary>
    public bool DeleteLogical { get; set; }

    /// <summary>Hash content of each book file for exact duplicate detection (slower scans).</summary>
    public bool HashContent { get; set; }

    public DateTimeOffset? LastScanStartedAt { get; set; }
    public DateTimeOffset? LastScanFinishedAt { get; set; }
    public string? LastScanSummary { get; set; }
}
