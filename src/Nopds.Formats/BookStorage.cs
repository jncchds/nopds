using System.IO.Compression;
using System.Text;
using Nopds.Domain.Entities;

namespace Nopds.Formats;

/// <summary>Resolves and opens book files on disk or inside archives.</summary>
public static class BookStorage
{
    static BookStorage() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Encoding for ZIP entry names that lack the UTF-8 flag ("cp866", "cp1251", "utf-8", ...).</summary>
    public static Encoding ZipEncoding(string? codepage)
    {
        if (string.IsNullOrWhiteSpace(codepage))
        {
            return Encoding.UTF8;
        }

        var cp = codepage.Trim().ToLowerInvariant();
        try
        {
            if (cp.StartsWith("cp", StringComparison.Ordinal) && int.TryParse(cp[2..], out var n))
            {
                return Encoding.GetEncoding(n);
            }

            return Encoding.GetEncoding(cp);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    public static string FullPath(string root, string relPath) =>
        Path.GetFullPath(Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar)));

    public static ZipArchive OpenZip(string path, string? codepage) =>
        new(File.OpenRead(path), ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: ZipEncoding(codepage));

    /// <summary>
    /// Opens the book bytes as a seekable stream. Archive entries are buffered in memory (books are small);
    /// plain files are returned as a FileStream. Returns null when the file or entry is missing.
    /// </summary>
    public static Stream? Open(Library library, Book book)
    {
        var path = FullPath(library.RootPath, book.RelPath);
        if (!IsInside(library.RootPath, path) || !File.Exists(path))
        {
            return null;
        }

        if (book.Container == BookContainer.File)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        using var zip = OpenZip(path, library.ZipCodepage);
        var entry = zip.GetEntry(book.EntryName!) ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals(book.EntryName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var ms = new MemoryStream(checked((int)entry.Length));
        using (var es = entry.Open())
        {
            es.CopyTo(ms);
        }

        ms.Position = 0;
        return ms;
    }

    public static bool IsInside(string root, string fullPath)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(r, StringComparison.Ordinal);
    }
}
