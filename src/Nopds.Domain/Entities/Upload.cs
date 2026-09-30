namespace Nopds.Domain.Entities;

/// <summary>A file a user put into the upload library. Books scanned from it inherit its owner and privacy.</summary>
public class Upload
{
    public long Id { get; set; }
    public int LibraryId { get; set; }
    public Library? Library { get; set; }

    /// <summary>Library-relative path of the uploaded file (matches <see cref="Book.RelPath"/>).</summary>
    public required string RelPath { get; set; }

    /// <summary>File name as sent by the browser.</summary>
    public required string OriginalName { get; set; }

    /// <summary>Uploader; null once that account is deleted (a private book then stays visible to admins only).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Only the uploader and admins see private books.</summary>
    public bool IsPrivate { get; set; }

    public long FileSize { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
