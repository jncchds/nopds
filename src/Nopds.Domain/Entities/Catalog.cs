namespace Nopds.Domain.Entities;

/// <summary>Node of the folder tree: directory, archive or INPX/INP index.</summary>
public class Catalog
{
    public long Id { get; set; }
    public int LibraryId { get; set; }
    public Library? Library { get; set; }
    public long? ParentId { get; set; }
    public Catalog? Parent { get; set; }
    public required string Name { get; set; }

    /// <summary>Path relative to the library root, '/'-separated; "." is the root.</summary>
    public required string Path { get; set; }

    public CatalogType Type { get; set; }
    public long Size { get; set; }
    public DateTimeOffset? Mtime { get; set; }
}
