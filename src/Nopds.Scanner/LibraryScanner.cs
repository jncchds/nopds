using System.IO.Compression;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nopds.Domain.Entities;
using Nopds.Formats;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Genres;

namespace Nopds.Scanner;

/// <summary>
/// Incremental scan of one library. Unchanged files and archives (same size + mtime) are skipped without
/// touching the database; new or changed books are parsed in parallel and written by a single writer.
/// Books whose files disappeared are soft- or hard-deleted depending on the library settings.
/// </summary>
public sealed class LibraryScanner(
    IDbContextFactory<NopdsDbContext> dbFactory,
    BookParsers parsers,
    GenreCatalog genres,
    ILogger<LibraryScanner> log)
{
    /// <summary>Archive entries larger than this are indexed from their file name only.</summary>
    private const long MaxParseBytes = 64L * 1024 * 1024;

    private const int BatchSize = 250;

    private sealed record Existing(long Id, BookContainer Container, string RelPath, string? EntryName, long Size, DateTimeOffset? Mtime, bool Deleted, long CatalogId);

    private sealed record CatalogState(long Id, string Path, CatalogType Type, long Size, DateTimeOffset? Mtime);

    private sealed record ParseJob(
        long? ExistingId, BookContainer Container, string RelPath, string? EntryName, string FileName, string Format,
        long Size, DateTimeOffset? Mtime, string CatalogPath, CatalogType CatalogType, byte[]? Data, string? FullPath);

    private abstract record Item;

    private sealed record BookItem(BookRecord Record) : Item;

    private sealed record CatalogItem(string Path, CatalogType Type, long Size, DateTimeOffset? Mtime) : Item;

    private sealed class Run(Library library, ScanProgress progress, string? subPath)
    {
        public Library Library { get; } = library;
        public ScanProgress Progress { get; } = progress;
        public string? SubPath { get; } = subPath;
        public Dictionary<string, Existing> Files { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, Existing>> Archives { get; } = new(StringComparer.Ordinal);
        public Dictionary<long, List<Existing>> ByCatalog { get; } = [];
        public Dictionary<string, CatalogState> Catalogs { get; } = new(StringComparer.Ordinal);
        public HashSet<long> Seen { get; } = [];
        public List<long> Restored { get; } = [];
        public List<CatalogItem> DeferredCatalogs { get; } = [];
        public HashSet<(string RelPath, string? EntryName)> Written { get; } = [];
        public int Duplicates { get; set; }
        public HashSet<string> Extensions { get; } = new(library.Extensions.Select(e => e.TrimStart('.').ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        public HashSet<string> VisitedDirs { get; } = new(StringComparer.Ordinal);
        public Channel<ParseJob> Jobs { get; } = Channel.CreateBounded<ParseJob>(new BoundedChannelOptions(256) { SingleWriter = true });
        public Channel<Item> Items { get; } = Channel.CreateBounded<Item>(new BoundedChannelOptions(2048) { SingleReader = true });
    }

    public async Task RunAsync(int libraryId, string? subPath, ScanProgress progress, Action<ScanProgress> report, CancellationToken ct)
    {
        Library library;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            library = await db.Libraries.AsNoTracking().FirstAsync(l => l.Id == libraryId, ct);

            // Uploaded books are only hidden when their file is missing, so owner and privacy survive a remount.
            library.DeleteLogical |= library.IsUploads;
            await db.Libraries.Where(l => l.Id == libraryId)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.LastScanStartedAt, DateTimeOffset.UtcNow), ct);
        }

        var root = Path.GetFullPath(library.RootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Library root '{root}' does not exist.");
        }

        subPath = NormalizeSubPath(subPath);
        var run = new Run(library, progress, subPath);
        await LoadExistingAsync(run, ct);

        var writer = new ScanWriter(dbFactory, genres, libraryId);
        await writer.LoadAsync(ct);

        using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(750));
        using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reporter = Task.Run(async () =>
        {
            try
            {
                while (await ticker.WaitForNextTickAsync(tickCts.Token))
                {
                    report(progress);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);

        var writerTask = Task.Run(() => WriterLoopAsync(run, writer, ct), ct);
        var workersTask = Task.Run(() => WorkersAsync(run, ct), ct);

        try
        {
            var start = subPath is null ? root : BookStorage.FullPath(root, subPath);
            if (Directory.Exists(start))
            {
                await WalkAsync(run, root, start, ct);
            }
            else if (File.Exists(start))
            {
                await ProcessFileEntryAsync(run, root, new FileInfo(start), ct);
            }
        }
        finally
        {
            run.Jobs.Writer.TryComplete();
        }

        await workersTask;
        run.Items.Writer.TryComplete();
        await writerTask;

        foreach (var c in run.DeferredCatalogs)
        {
            await writer.EnsureCatalogAsync(c.Path, c.Type, c.Size, c.Mtime, ct);
        }

        await FinishAsync(run, ct);
        if (library.IsUploads)
        {
            await ClaimUnownedAsync(library.Id, ct);
        }
        await tickCts.CancelAsync();
        await reporter;
    }

    private static string? NormalizeSubPath(string? subPath)
    {
        if (string.IsNullOrWhiteSpace(subPath))
        {
            return null;
        }

        var s = subPath.Replace('\\', '/').Trim('/');
        return s is "" or "." ? null : s;
    }

    private static bool InScope(string relPath, string? subPath) =>
        subPath is null || relPath == subPath || relPath.StartsWith(subPath + "/", StringComparison.Ordinal);

    private async Task LoadExistingAsync(Run run, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var libraryId = run.Library.Id;
        var rows = db.Books.AsNoTracking().Where(b => b.LibraryId == libraryId)
            .Select(b => new Existing(b.Id, b.Container, b.RelPath, b.EntryName, b.FileSize, b.FileMtime, b.DeletedAt != null, b.CatalogId))
            .AsAsyncEnumerable();
        await foreach (var b in rows.WithCancellation(ct))
        {
            if (!InScope(b.RelPath, run.SubPath))
            {
                continue;
            }

            if (b.Container == BookContainer.File)
            {
                run.Files[b.RelPath] = b;
            }
            else
            {
                if (!run.Archives.TryGetValue(b.RelPath, out var entries))
                {
                    run.Archives[b.RelPath] = entries = new Dictionary<string, Existing>(StringComparer.Ordinal);
                }

                entries[b.EntryName ?? string.Empty] = b;
                if (!run.ByCatalog.TryGetValue(b.CatalogId, out var list))
                {
                    run.ByCatalog[b.CatalogId] = list = [];
                }

                list.Add(b);
            }
        }

        foreach (var c in await db.Catalogs.AsNoTracking().Where(c => c.LibraryId == libraryId)
                     .Select(c => new CatalogState(c.Id, c.Path, c.Type, c.Size, c.Mtime)).ToListAsync(ct))
        {
            run.Catalogs[c.Path] = c;
        }
    }

    private static bool Unchanged(Run run, string catalogPath, long size, DateTimeOffset? mtime) =>
        run.Catalogs.TryGetValue(catalogPath, out var c) && c.Size == size
        && (mtime is null || c.Mtime is null || Math.Abs((c.Mtime.Value - mtime.Value).TotalSeconds) < 2);

    private static bool SameFile(Existing e, long size, DateTimeOffset? mtime) =>
        e.Size == size && (mtime is null || e.Mtime is null || Math.Abs((e.Mtime.Value - mtime.Value).TotalSeconds) < 2);

    private static void MarkSeen(Run run, Existing e)
    {
        if (run.Seen.Add(e.Id))
        {
            run.Progress.BooksSkipped++;
            if (e.Deleted)
            {
                run.Restored.Add(e.Id);
            }
        }
    }

    private static string Rel(string root, string full)
    {
        var r = Path.GetRelativePath(root, full).Replace('\\', '/');
        return r.Length == 0 ? "." : r;
    }

    private static string ParentOf(string relPath)
    {
        var i = relPath.LastIndexOf('/');
        return i < 0 ? "." : relPath[..i];
    }

    private static string Join(string dir, string name) => dir == "." ? name : dir + "/" + name;

    // ------------------------------------------------------------------ walking

    private async Task WalkAsync(Run run, string root, string dir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = new DirectoryInfo(dir);
        var real = info.LinkTarget is not null ? info.ResolveLinkTarget(true)?.FullName ?? dir : dir;
        if (!run.VisitedDirs.Add(Path.GetFullPath(real)))
        {
            return;
        }

        List<FileInfo> files;
        List<DirectoryInfo> dirs;
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System, RecurseSubdirectories = false };
            files = info.EnumerateFiles("*", options).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
            dirs = info.EnumerateDirectories("*", options).Where(d => !d.Name.StartsWith('.')).OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Cannot read directory {Dir}", dir);
            run.Progress.Errors++;
            return;
        }

        run.Progress.CurrentPath = Rel(root, dir);
        var inpx = run.Library.InpxEnabled
            ? files.Where(f => f.Extension.Equals(".inpx", StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

        if (inpx.Count > 0)
        {
            // Like SimpleOPDS: an INPX index describes the archives next to it; other files there are not scanned.
            foreach (var f in inpx)
            {
                await ProcessInpxAsync(run, root, f, ct);
            }
        }
        else
        {
            foreach (var f in files)
            {
                await ProcessFileEntryAsync(run, root, f, ct);
            }
        }

        foreach (var d in dirs)
        {
            await WalkAsync(run, root, d.FullName, ct);
        }
    }

    private async Task ProcessFileEntryAsync(Run run, string root, FileInfo f, CancellationToken ct)
    {
        run.Progress.FilesSeen++;
        var ext = f.Extension.TrimStart('.').ToLowerInvariant();
        if (ext == "zip")
        {
            if (run.Library.ScanZip)
            {
                await ProcessZipAsync(run, root, f, ct);
            }

            return;
        }

        if (!run.Extensions.Contains(ext))
        {
            return;
        }

        var rel = Rel(root, f.FullName);
        var mtime = new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero);
        run.Files.TryGetValue(rel, out var existing);
        if (existing is not null)
        {
            if (SameFile(existing, f.Length, mtime))
            {
                MarkSeen(run, existing);
                return;
            }

            run.Seen.Add(existing.Id);
        }

        await run.Jobs.Writer.WriteAsync(new ParseJob(existing?.Id, BookContainer.File, rel, null, f.Name, ext, f.Length, mtime,
            ParentOf(rel), CatalogType.Directory, null, f.FullName), ct);
    }

    private async Task ProcessZipAsync(Run run, string root, FileInfo f, CancellationToken ct)
    {
        var rel = Rel(root, f.FullName);
        var mtime = new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero);
        run.Archives.TryGetValue(rel, out var existing);

        if (existing is { Count: > 0 } && Unchanged(run, rel, f.Length, mtime))
        {
            foreach (var e in existing.Values)
            {
                MarkSeen(run, e);
            }

            run.Progress.ArchivesSkipped++;
            return;
        }

        try
        {
            using var zip = BookStorage.OpenZip(f.FullName, run.Library.ZipCodepage);
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                var ext = Path.GetExtension(entry.Name).TrimStart('.').ToLowerInvariant();
                if (!run.Extensions.Contains(ext))
                {
                    continue;
                }

                Existing? old = null;
                if (existing?.TryGetValue(entry.FullName, out old) == true)
                {
                    if (old.Size == entry.Length)
                    {
                        MarkSeen(run, old);
                        continue;
                    }

                    run.Seen.Add(old.Id);
                }

                byte[]? data = null;
                if (entry.Length <= MaxParseBytes)
                {
                    data = new byte[entry.Length];
                    await using var es = entry.Open();
                    await es.ReadExactlyAsync(data, ct);
                }

                await run.Jobs.Writer.WriteAsync(new ParseJob(old?.Id, BookContainer.Zip, rel, entry.FullName, entry.Name, ext, entry.Length,
                    entry.LastWriteTime.ToUniversalTime(), rel, CatalogType.Zip, data, null), ct);
            }

            run.Progress.ArchivesScanned++;
            run.DeferredCatalogs.Add(new CatalogItem(rel, CatalogType.Zip, f.Length, mtime));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            log.LogWarning("Bad ZIP archive {Path}: {Error}", rel, ex.Message);
            run.Progress.Errors++;
            // Keep what we knew about this archive rather than deleting on a read error.
            foreach (var e in existing?.Values ?? Enumerable.Empty<Existing>())
            {
                MarkSeen(run, e);
            }
        }
    }

    private void MarkCatalogTreeSeen(Run run, string prefix)
    {
        var p = prefix + "/";
        foreach (var c in run.Catalogs.Values)
        {
            if (c.Path.StartsWith(p, StringComparison.Ordinal) && run.ByCatalog.TryGetValue(c.Id, out var books))
            {
                foreach (var b in books)
                {
                    MarkSeen(run, b);
                }
            }
        }
    }

    private async Task ProcessInpxAsync(Run run, string root, FileInfo f, CancellationToken ct)
    {
        run.Progress.FilesSeen++;
        var rel = Rel(root, f.FullName);
        var dirRel = ParentOf(rel);
        var mtime = new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero);

        if (run.Library.InpxSkipUnchanged && Unchanged(run, rel, f.Length, mtime))
        {
            MarkCatalogTreeSeen(run, rel);
            run.Progress.ArchivesSkipped++;
            log.LogInformation("Skip unchanged INPX {Path}", rel);
            return;
        }

        log.LogInformation("Processing INPX {Path}", rel);
        await run.Items.Writer.WriteAsync(new CatalogItem(rel, CatalogType.Inpx, 0, null), ct);
        var zipEntries = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);

        try
        {
            foreach (var inp in new InpxReader(f.FullName).ReadInpFiles())
            {
                var inpCatalog = rel + "/" + inp.Name;
                if (run.Library.InpxSkipUnchanged && Unchanged(run, inpCatalog, inp.Size, null))
                {
                    MarkCatalogTreeSeen(run, inpCatalog);
                    run.Progress.ArchivesSkipped++;
                    continue;
                }

                await run.Items.Writer.WriteAsync(new CatalogItem(inpCatalog, CatalogType.Inp, 0, null), ct);
                foreach (var r in inp.Records)
                {
                    ct.ThrowIfCancellationRequested();
                    var zipRel = Join(dirRel, r.Folder);
                    var entryName = r.File + "." + r.Ext;
                    if (!run.Extensions.Contains(r.Ext))
                    {
                        continue;
                    }

                    if (run.Archives.TryGetValue(zipRel, out var entries) && entries.TryGetValue(entryName, out var old))
                    {
                        MarkSeen(run, old);
                        continue;
                    }

                    if ((run.Library.InpxTestZip || run.Library.InpxTestFiles) && !EntryExists(run, root, zipRel, entryName, zipEntries))
                    {
                        continue;
                    }

                    var meta = new BookMetadata { Title = r.Title, Lang = r.Lang, DocDate = r.Date };
                    meta.Authors.AddRange(r.Authors);
                    meta.Genres.AddRange(r.Genres.Select(g => g.ToLowerInvariant()));
                    if (r.Series.Length > 0)
                    {
                        meta.Series.Add(new SeriesRef(r.Series, r.SerNo));
                    }

                    await run.Items.Writer.WriteAsync(new BookItem(new BookRecord(null, BookContainer.Inpx, zipRel, entryName, entryName, r.Ext,
                        r.Size, null, inpCatalog + "/" + r.Folder, CatalogType.Zip, meta, null, null)), ct);
                }

                run.DeferredCatalogs.Add(new CatalogItem(inpCatalog, CatalogType.Inp, inp.Size, null));
                run.Progress.ArchivesScanned++;
            }

            run.DeferredCatalogs.Add(new CatalogItem(rel, CatalogType.Inpx, f.Length, mtime));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            log.LogWarning("Bad INPX {Path}: {Error}", rel, ex.Message);
            run.Progress.Errors++;
            MarkCatalogTreeSeen(run, rel);
        }
    }

    private bool EntryExists(Run run, string root, string zipRel, string entryName, Dictionary<string, HashSet<string>?> cache)
    {
        if (!cache.TryGetValue(zipRel, out var names))
        {
            var full = BookStorage.FullPath(root, zipRel);
            names = null;
            if (File.Exists(full))
            {
                names = [];
                if (run.Library.InpxTestFiles)
                {
                    try
                    {
                        using var zip = BookStorage.OpenZip(full, run.Library.ZipCodepage);
                        names.UnionWith(zip.Entries.Select(e => e.FullName));
                    }
                    catch (InvalidDataException)
                    {
                        names = null;
                    }
                }
            }

            cache[zipRel] = names;
        }

        return names is not null && (!run.Library.InpxTestFiles || names.Contains(entryName));
    }

    // ------------------------------------------------------------------ parsing & writing

    private async Task WorkersAsync(Run run, CancellationToken ct)
    {
        var dop = Math.Clamp(Environment.ProcessorCount, 2, 8);
        await Parallel.ForEachAsync(run.Jobs.Reader.ReadAllAsync(ct), new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
            async (job, token) =>
            {
                var record = Parse(run, job);
                if (record is not null)
                {
                    await run.Items.Writer.WriteAsync(new BookItem(record), token);
                }
            });
    }

    private BookRecord? Parse(Run run, ParseJob job)
    {
        try
        {
            using Stream? stream = job.Data is not null
                ? new MemoryStream(job.Data, writable: false)
                : job.FullPath is not null ? File.OpenRead(job.FullPath) : null;

            BookMetadata meta;
            string? koHash = null;
            long? contentHash = null;
            if (stream is null)
            {
                meta = new BookMetadata { Title = Path.GetFileNameWithoutExtension(job.FileName) };
            }
            else
            {
                koHash = DocumentHashes.KoreaderPartialMd5(stream);
                if (run.Library.HashContent)
                {
                    contentHash = DocumentHashes.ContentHash(stream);
                }

                meta = parsers.For(job.Format).Parse(stream, job.FileName, includeCover: false);
            }

            return new BookRecord(job.ExistingId, job.Container, job.RelPath, job.EntryName, job.FileName, job.Format, job.Size, job.Mtime,
                job.CatalogPath, job.CatalogType, meta, koHash, contentHash);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Increment(ref run.Progress.Errors);
            log.LogWarning("Cannot parse {Path}{Entry}: {Error}", job.RelPath, job.EntryName is null ? "" : "!" + job.EntryName, ex.Message);
            return null;
        }
    }

    private async Task WriterLoopAsync(Run run, ScanWriter writer, CancellationToken ct)
    {
        var batch = new List<BookRecord>(BatchSize);
        await foreach (var item in run.Items.Reader.ReadAllAsync(ct))
        {
            switch (item)
            {
                case BookItem b:
                    // INPX indexes often list one file several times (and a ZIP may repeat an entry name);
                    // only the first record per (path, entry) is stored, the rest would violate the unique index.
                    if (!run.Written.Add((b.Record.RelPath, b.Record.EntryName)))
                    {
                        run.Duplicates++;
                        break;
                    }

                    batch.Add(b.Record);
                    if (batch.Count >= BatchSize)
                    {
                        await FlushAsync(run, writer, batch, ct);
                    }

                    break;
                case CatalogItem c:
                    await FlushAsync(run, writer, batch, ct);
                    await writer.EnsureCatalogAsync(c.Path, c.Type, c.Size, c.Mtime, ct);
                    break;
            }
        }

        await FlushAsync(run, writer, batch, ct);
        if (run.Duplicates > 0)
        {
            log.LogInformation("Ignored {Count} duplicate book records in library {Library}", run.Duplicates, run.Library.Name);
        }
    }

    /// <summary>Writes a batch; if it fails, retries record by record so one bad book cannot fail the scan.</summary>
    private async Task FlushAsync(Run run, ScanWriter writer, List<BookRecord> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await writer.WriteAsync(batch, run.Progress, ct);
        }
        catch (DbUpdateException ex) when (batch.Count > 1)
        {
            log.LogWarning("Batch write failed ({Error}); retrying {Count} books individually", ex.InnerException?.Message ?? ex.Message, batch.Count);
            foreach (var record in batch)
            {
                try
                {
                    await writer.WriteAsync([record], run.Progress, ct);
                }
                catch (DbUpdateException single)
                {
                    Interlocked.Increment(ref run.Progress.Errors);
                    log.LogWarning("Cannot store {Path}{Entry}: {Error}", record.RelPath, record.EntryName is null ? "" : "!" + record.EntryName,
                        single.InnerException?.Message ?? single.Message);
                }
            }
        }
        catch (DbUpdateException ex)
        {
            Interlocked.Increment(ref run.Progress.Errors);
            log.LogWarning("Cannot store {Path}: {Error}", batch[0].RelPath, ex.InnerException?.Message ?? ex.Message);
        }

        batch.Clear();
    }

    private async Task FinishAsync(Run run, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var chunk in run.Restored.Chunk(5000))
        {
            await db.Books.Where(b => chunk.Contains(b.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => b.DeletedAt, (DateTimeOffset?)null), ct);
        }

        run.Progress.BooksRestored = run.Restored.Count;

        var missing = run.Files.Values.Concat(run.Archives.Values.SelectMany(a => a.Values))
            .Where(b => !b.Deleted && !run.Seen.Contains(b.Id))
            .Select(b => b.Id)
            .ToList();
        foreach (var chunk in missing.Chunk(5000))
        {
            if (run.Library.DeleteLogical)
            {
                await db.Books.Where(b => chunk.Contains(b.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => b.DeletedAt, now), ct);
            }
            else
            {
                await db.Books.Where(b => chunk.Contains(b.Id)).ExecuteDeleteAsync(ct);
            }
        }

        if (!run.Library.DeleteLogical)
        {
            // Soft-deleted rows left over from an earlier logical-delete setting.
            var stale = run.Files.Values.Concat(run.Archives.Values.SelectMany(a => a.Values))
                .Where(b => b.Deleted && !run.Seen.Contains(b.Id)).Select(b => b.Id).ToList();
            foreach (var chunk in stale.Chunk(5000))
            {
                await db.Books.Where(b => chunk.Contains(b.Id)).ExecuteDeleteAsync(ct);
            }
        }

        run.Progress.BooksDeleted = missing.Count;

        if (missing.Count > 0 && !run.Library.DeleteLogical)
        {
            await CleanupAsync(db, run.Library.Id, ct);
        }
    }

    /// <summary>
    /// Files put into the upload folder by other means (copied in by hand, left from before) count as public uploads
    /// of the first user, so every book there has an owner who can manage it.
    /// </summary>
    private async Task ClaimUnownedAsync(int libraryId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO uploads (library_id, rel_path, original_name, user_id, is_private, file_size, uploaded_at)
            SELECT DISTINCT ON (b.rel_path) b.library_id, b.rel_path, left(regexp_replace(b.rel_path, '^.*/', ''), 512), owner.id, FALSE,
                   b.file_size, b.registered_at
            FROM books b
            CROSS JOIN (SELECT id FROM users ORDER BY created_at, id LIMIT 1) owner
            WHERE b.library_id = {libraryId}
              AND NOT EXISTS (SELECT 1 FROM uploads u WHERE u.library_id = b.library_id AND u.rel_path = b.rel_path)
            ORDER BY b.rel_path, b.id
            ON CONFLICT (library_id, rel_path) DO NOTHING
            """, ct);
        if (claimed > 0)
        {
            log.LogInformation("Upload library {Library}: {Count} files without an uploader assigned to the first user", libraryId, claimed);
        }
    }

    /// <summary>Removes catalogs, authors and series that no longer have any books.</summary>
    public static async Task CleanupAsync(NopdsDbContext db, int libraryId, CancellationToken ct)
    {
        for (var i = 0; i < 64; i++)
        {
            var removed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM catalogs c
                WHERE c.library_id = {libraryId} AND c.parent_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM books b WHERE b.catalog_id = c.id)
                  AND NOT EXISTS (SELECT 1 FROM catalogs ch WHERE ch.parent_id = c.id)
                """, ct);
            if (removed == 0)
            {
                break;
            }
        }

        await db.Database.ExecuteSqlRawAsync("DELETE FROM authors a WHERE NOT EXISTS (SELECT 1 FROM book_authors x WHERE x.author_id = a.id)", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM series s WHERE NOT EXISTS (SELECT 1 FROM book_series x WHERE x.series_id = s.id)", ct);
    }
}
