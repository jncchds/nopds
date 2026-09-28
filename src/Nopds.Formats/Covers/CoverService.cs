using System.Collections.Concurrent;
using Nopds.Domain.Entities;
using SkiaSharp;

namespace Nopds.Formats.Covers;

public sealed record CoverFile(string Path, string MediaType);

/// <summary>
/// Extracts covers on first request and caches originals and WebP thumbnails on disk.
/// Cache keys include the file size so a changed book gets a fresh cover.
/// </summary>
public sealed class CoverService
{
    public const int ThumbWidth = 240;
    public const int ThumbHeight = 360;

    private readonly string _dir;
    private readonly BookParsers _parsers;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public CoverService(string cacheDir, BookParsers parsers)
    {
        _dir = Path.Combine(cacheDir, "covers");
        Directory.CreateDirectory(_dir);
        _parsers = parsers;
    }

    private string Key(Book book) => $"{book.Id}_{book.FileSize}";

    private string ShardDir(Book book)
    {
        var d = Path.Combine(_dir, (book.Id % 256).ToString("x2"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Returns the cached original cover, extracting it if needed; null when the book has none.</summary>
    public async Task<CoverFile?> GetCoverAsync(Library library, Book book, CancellationToken ct = default)
    {
        var dir = ShardDir(book);
        var key = Key(book);
        var none = Path.Combine(dir, key + ".none");
        if (File.Exists(none))
        {
            return null;
        }

        var existing = Directory.EnumerateFiles(dir, key + ".cover.*").FirstOrDefault();
        if (existing is not null)
        {
            return new CoverFile(existing, MediaTypes.ForImage(existing));
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            existing = Directory.EnumerateFiles(dir, key + ".cover.*").FirstOrDefault();
            if (existing is not null)
            {
                return new CoverFile(existing, MediaTypes.ForImage(existing));
            }

            CoverImage? cover = null;
            try
            {
                await using var stream = BookStorage.Open(library, book);
                if (stream is not null)
                {
                    Stream seekable = stream;
                    if (!stream.CanSeek)
                    {
                        var ms = new MemoryStream();
                        await stream.CopyToAsync(ms, ct);
                        ms.Position = 0;
                        seekable = ms;
                    }

                    cover = _parsers.For(book.Format).Parse(seekable, book.FileName, includeCover: true).Cover;
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or System.Xml.XmlException)
            {
                cover = null;
            }

            if (cover is null || cover.Data.Length == 0)
            {
                await File.WriteAllBytesAsync(none, [], ct);
                return null;
            }

            var ext = cover.MediaType switch
            {
                "image/png" => "png",
                "image/gif" => "gif",
                "image/webp" => "webp",
                _ => "jpg",
            };
            var path = Path.Combine(dir, $"{key}.cover.{ext}");
            await File.WriteAllBytesAsync(path, cover.Data, ct);
            return new CoverFile(path, MediaTypes.ForImage(path));
        }
        finally
        {
            gate.Release();
            _locks.TryRemove(key, out _);
        }
    }

    /// <summary>Returns a WebP thumbnail (max 240×360), generating it from the cover when missing.</summary>
    public async Task<CoverFile?> GetThumbnailAsync(Library library, Book book, CancellationToken ct = default)
    {
        var dir = ShardDir(book);
        var thumb = Path.Combine(dir, Key(book) + ".thumb.webp");
        if (File.Exists(thumb))
        {
            return new CoverFile(thumb, "image/webp");
        }

        var cover = await GetCoverAsync(library, book, ct);
        if (cover is null)
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(cover.Path, ct);
        var data = MakeThumbnail(bytes, ThumbWidth, ThumbHeight);
        if (data is null)
        {
            return cover;
        }

        var tmp = thumb + "." + Guid.NewGuid().ToString("N")[..8];
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, thumb, overwrite: true);
        return new CoverFile(thumb, "image/webp");
    }

    public static byte[]? MakeThumbnail(byte[] image, int maxWidth, int maxHeight)
    {
        using var bitmap = SKBitmap.Decode(image);
        if (bitmap is null)
        {
            return null;
        }

        var scale = Math.Min(1.0, Math.Min((double)maxWidth / bitmap.Width, (double)maxHeight / bitmap.Height));
        var w = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var h = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var resized = scale < 1.0 ? bitmap.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell)) : bitmap;
        using var img = SKImage.FromBitmap(resized ?? bitmap);
        using var encoded = img.Encode(SKEncodedImageFormat.Webp, 80);
        return encoded?.ToArray();
    }

    /// <summary>Deletes cached covers (e.g. after a library is removed).</summary>
    public void Clear()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }

        Directory.CreateDirectory(_dir);
    }
}
