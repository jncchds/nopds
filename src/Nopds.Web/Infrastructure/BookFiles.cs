using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Nopds.Conversion;
using Nopds.Domain.Entities;
using Nopds.Domain.Text;
using Nopds.Formats;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Settings;

namespace Nopds.Web.Infrastructure;

/// <summary>Serves book files (original, zipped or converted) and records downloads on the user's shelf.</summary>
public sealed class BookFiles(ConversionService conversion, SettingsStore settings, NopdsDbContext db, ILogger<BookFiles> log)
{
    public string DownloadName(Book book, string format)
    {
        var baseName = settings.Current.TitleAsFilename
            ? book.Title
            : Path.GetFileNameWithoutExtension(book.FileName);
        var authors = book.Authors.Count > 0 && settings.Current.TitleAsFilename && book.Authors[0].Author is { } a
            ? a.FullName.Split(' ')[0] + " - "
            : string.Empty;
        var name = (authors + baseName).Trim();
        if (name.Length > 120)
        {
            name = name[..120];
        }

        return name + "." + format;
    }

    public async Task<IResult> ServeAsync(HttpContext http, Book book, string? format, bool zip, bool inline, Guid? userId, CancellationToken ct)
    {
        var library = book.Library!;
        var target = string.IsNullOrWhiteSpace(format) ? book.Format : format.ToLowerInvariant();
        var name = DownloadName(book, target);

        if (userId is { } uid)
        {
            await TouchShelfAsync(uid, book.Id, ct);
        }

        Stream? stream;
        string contentType;
        string? physicalPath = null;
        if (target == book.Format)
        {
            contentType = MediaTypes.ForFormat(target);
            if (book.Container == BookContainer.File && !zip)
            {
                physicalPath = BookStorage.FullPath(library.RootPath, book.RelPath);
                if (!BookStorage.IsInside(library.RootPath, physicalPath) || !File.Exists(physicalPath))
                {
                    return Results.NotFound();
                }

                stream = null;
            }
            else
            {
                stream = BookStorage.Open(library, book);
                if (stream is null)
                {
                    return Results.NotFound();
                }
            }
        }
        else
        {
            var converted = await conversion.ConvertAsync(library, book, target, ct);
            if (converted is null)
            {
                log.LogInformation("Conversion of book {Book} to {Format} not available", book.Id, target);
                return Results.Problem($"Conversion to {target} is not available.", statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            contentType = converted.MediaType;
            physicalPath = zip ? null : converted.Path;
            stream = zip ? File.OpenRead(converted.Path) : null;
        }

        if (zip)
        {
            var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry(Translit.ToAscii(name), CompressionLevel.Optimal);
                await using var es = entry.Open();
                await using (stream)
                {
                    await stream!.CopyToAsync(es, ct);
                }
            }

            ms.Position = 0;
            name += ".zip";
            contentType = target == "fb2" ? MediaTypes.Fb2Zip : MediaTypes.Zip;
            stream = ms;
        }

        SetDisposition(http, name, inline);
        return physicalPath is not null
            ? Results.File(physicalPath, contentType, enableRangeProcessing: true, lastModified: File.GetLastWriteTimeUtc(physicalPath))
            : Results.Stream(stream!, contentType, enableRangeProcessing: stream!.CanSeek);
    }

    /// <summary>RFC 6266: ASCII fallback (transliterated) plus the real UTF-8 name.</summary>
    private static void SetDisposition(HttpContext http, string name, bool inline)
    {
        var cd = new ContentDispositionHeaderValue(inline ? "inline" : "attachment")
        {
            FileName = Translit.ToAscii(name).Replace("\"", ""),
            FileNameStar = name,
        };
        http.Response.Headers.ContentDisposition = cd.ToString();
    }

    public async Task TouchShelfAsync(Guid userId, long bookId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await db.ReadingStates.Where(r => r.UserId == userId && r.BookId == bookId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastOpenedAt, now), ct);
        if (updated == 0)
        {
            db.ReadingStates.Add(new ReadingState { UserId = userId, BookId = bookId, AddedAt = now, LastOpenedAt = now });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrent insert of the same shelf entry.
            }
        }
    }
}
