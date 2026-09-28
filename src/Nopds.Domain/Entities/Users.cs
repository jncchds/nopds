namespace Nopds.Domain.Entities;

/// <summary>Per-user reading state: bookshelf membership and reading position.</summary>
public class ReadingState
{
    public Guid UserId { get; set; }
    public long BookId { get; set; }
    public Book? Book { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastOpenedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Reader location (EPUB CFI or KOReader xpointer).</summary>
    public string? Location { get; set; }

    /// <summary>0..1 fraction read.</summary>
    public double Progress { get; set; }

    public bool Finished { get; set; }
}

/// <summary>KOReader sync record, keyed by document hash as the device sends it.</summary>
public class KoreaderProgress
{
    public Guid UserId { get; set; }
    public required string Document { get; set; }
    public required string Progress { get; set; }
    public double Percentage { get; set; }
    public string? Device { get; set; }
    public string? DeviceId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>Runtime setting stored as JSON (replaces SimpleOPDS constance).</summary>
public class Setting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
