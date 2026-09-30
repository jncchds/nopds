using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Data;

namespace Nopds.Infrastructure.Uploads;

/// <summary>The library users upload books to; resolved on start from Nopds:UploadPath (null when uploads are off).</summary>
public sealed class UploadLibrary
{
    public int? Id { get; private set; }

    public string? RootPath { get; private set; }

    public bool Enabled => Id is not null;

    /// <summary>Every user may browse the upload library, whatever their library restrictions.</summary>
    public int[]? Extend(int[]? allowedLibraries) =>
        allowedLibraries is null || Id is not { } id || allowedLibraries.Contains(id) ? allowedLibraries : [.. allowedLibraries, id];

    /// <summary>Finds or creates the upload library for <paramref name="path"/>; with no path, uploads are switched off.</summary>
    public async Task InitializeAsync(NopdsDbContext db, string? path, ILogger log, CancellationToken ct = default)
    {
        var current = await db.Libraries.Where(l => l.IsUploads).ToListAsync(ct);
        if (string.IsNullOrWhiteSpace(path))
        {
            // Private books stay private: ownership lives in the uploads table, not in this flag.
            current.ForEach(l => l.IsUploads = false);
            await db.SaveChangesAsync(ct);
            return;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        Directory.CreateDirectory(root);

        var library = current.FirstOrDefault()
                      ?? await db.Libraries.FirstOrDefaultAsync(l => l.RootPath == root, ct);
        if (library is null)
        {
            var name = "Uploads";
            for (var i = 2; await db.Libraries.AnyAsync(l => l.Name == name, ct); i++)
            {
                name = $"Uploads {i}";
            }

            library = new Library { Name = name, RootPath = root };
            db.Libraries.Add(library);
            log.LogInformation("Created upload library {Name} at {Root}", name, root);
        }
        else if (library.RootPath != root)
        {
            log.LogInformation("Upload library {Name} moved from {Old} to {Root}", library.Name, library.RootPath, root);
            library.RootPath = root;
        }

        library.IsUploads = true;
        library.DeleteLogical = true;
        current.Where(l => l != library).ToList().ForEach(l => l.IsUploads = false);
        await db.SaveChangesAsync(ct);

        Id = library.Id;
        RootPath = root;
    }
}
