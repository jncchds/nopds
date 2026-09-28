using System.IO.Compression;
using System.Text;

namespace Nopds.Scanner;

/// <summary>One book line from an INP file inside an INPX index.</summary>
public sealed record InpRecord(
    string[] Authors,
    string[] Genres,
    string Title,
    string Series,
    int SerNo,
    string File,
    long Size,
    string Ext,
    string Date,
    string Lang,
    string Folder);

/// <summary>
/// Reads INPX library indexes (MyHomeLib / LibRusEc format). Ported from SimpleOPDS inpx_parser.py:
/// fields are separated by 0x04, lists by ':', and the field order comes from structure.info when present.
/// </summary>
public sealed class InpxReader(string inpxPath)
{
    private static readonly string[] DefaultFormat = ["AUTHOR", "GENRE", "TITLE", "SERIES", "SERNO", "FILE", "SIZE", "LIBID", "DEL", "EXT", "DATE", "LANG"];

    public sealed record InpFile(string Name, long Size, IEnumerable<InpRecord> Records);

    public IEnumerable<InpFile> ReadInpFiles()
    {
        using var zip = ZipFile.OpenRead(inpxPath);
        var format = DefaultFormat;
        var structure = zip.GetEntry("structure.info");
        if (structure is not null)
        {
            using var sr = new StreamReader(structure.Open(), Encoding.UTF8);
            format = sr.ReadToEnd().Trim().TrimEnd(';').Split(';').Select(f => f.Trim().ToUpperInvariant()).ToArray();
        }

        var hasFolder = format.Contains("FOLDER");
        foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".inp", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var inpName = Path.GetFileNameWithoutExtension(entry.Name);
            yield return new InpFile(entry.Name, entry.Length, ReadRecords(entry, format, hasFolder ? null : inpName + ".zip"));
        }
    }

    private static IEnumerable<InpRecord> ReadRecords(ZipArchiveEntry entry, string[] format, string? defaultFolder)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\u0004');
            string Get(string key)
            {
                var i = Array.IndexOf(format, key);
                return i >= 0 && i < fields.Length ? fields[i] : string.Empty;
            }

            var del = Get("DEL").Trim();
            if (del is not ("" or "0"))
            {
                continue;
            }

            var file = Get("FILE").Trim();
            var ext = Get("EXT").Trim().TrimStart('.');
            if (file.Length == 0 || ext.Length == 0)
            {
                continue;
            }

            yield return new InpRecord(
                Authors: List(Get("AUTHOR")).Select(a => string.Join(' ', a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))).Where(a => a.Length > 0).ToArray(),
                Genres: List(Get("GENRE")),
                Title: Get("TITLE"),
                Series: Get("SERIES").Trim(),
                SerNo: int.TryParse(Get("SERNO").Trim(), out var n) ? n : 0,
                File: file,
                Size: long.TryParse(Get("SIZE").Trim(), out var size) ? size : 0,
                Ext: ext.ToLowerInvariant(),
                Date: Get("DATE").Trim(),
                Lang: Get("LANG").Trim(),
                Folder: defaultFolder ?? Get("FOLDER").Trim());
        }
    }

    private static string[] List(string value) =>
        value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
