using System.Buffers.Binary;
using System.Text;
using Nopds.Domain.Text;

namespace Nopds.Formats.Parsers;

/// <summary>MOBI / AZW3 (KF8) metadata reader: PalmDB → MOBI header → EXTH records.</summary>
public sealed class MobiParser : IBookParser
{
    public IReadOnlyCollection<string> Formats { get; } = ["mobi", "azw", "azw3", "prc"];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover)
    {
        var header = ReadExact(stream, 0, 78);
        var type = Encoding.ASCII.GetString(header, 60, 8);
        if (type is not ("BOOKMOBI" or "TEXtREAd"))
        {
            throw new FormatException("Not a MOBI/PalmDOC file.");
        }

        int recordCount = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(76));
        var list = ReadExact(stream, 78, recordCount * 8);
        var offsets = new long[recordCount];
        for (var i = 0; i < recordCount; i++)
        {
            offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(i * 8));
        }

        var rec0End = recordCount > 1 ? offsets[1] : stream.Length;
        var rec0 = ReadExact(stream, offsets[0], (int)Math.Min(rec0End - offsets[0], 1 << 20));

        var meta = new BookMetadata { Title = Path.GetFileNameWithoutExtension(fileName) };
        if (rec0.Length < 24 || Encoding.ASCII.GetString(rec0, 16, 4) != "MOBI")
        {
            meta.Title = TextNormalizer.Strip(Encoding.Latin1.GetString(header, 0, 32).TrimEnd('\0'));
            return meta;
        }

        var mobiLen = (int)U32(rec0, 20);
        var encoding = U32(rec0, 28) == 65001 ? Encoding.UTF8 : Encoding.GetEncoding(1252);
        var fullNameOffset = (int)U32(rec0, 84);
        var fullNameLength = (int)U32(rec0, 88);
        if (fullNameOffset > 0 && fullNameOffset + fullNameLength <= rec0.Length)
        {
            meta.Title = TextNormalizer.Strip(encoding.GetString(rec0, fullNameOffset, fullNameLength));
        }

        var firstImage = rec0.Length >= 112 ? U32(rec0, 108) : uint.MaxValue;
        uint? coverOffset = null;
        var exthFlags = rec0.Length >= 132 ? U32(rec0, 128) : 0;
        var exthStart = 16 + mobiLen;
        if ((exthFlags & 0x40) != 0 && exthStart + 12 <= rec0.Length && Encoding.ASCII.GetString(rec0, exthStart, 4) == "EXTH")
        {
            var count = U32(rec0, exthStart + 8);
            var pos = exthStart + 12;
            for (var i = 0; i < count && pos + 8 <= rec0.Length; i++)
            {
                var recType = U32(rec0, pos);
                var len = (int)U32(rec0, pos + 4);
                if (len < 8 || pos + len > rec0.Length)
                {
                    break;
                }

                var data = rec0.AsSpan(pos + 8, len - 8);
                switch (recType)
                {
                    case 100:
                        AddAuthor(meta, encoding.GetString(data));
                        break;
                    case 103:
                        meta.Annotation = HtmlText.ToPlain(encoding.GetString(data));
                        break;
                    case 105:
                        var g = TextNormalizer.Strip(encoding.GetString(data)).ToLowerInvariant();
                        if (g.Length is > 0 and <= 64 && !meta.Genres.Contains(g))
                        {
                            meta.Genres.Add(g);
                        }

                        break;
                    case 106:
                        meta.DocDate = TextNormalizer.Strip(encoding.GetString(data));
                        break;
                    case 201 when data.Length >= 4:
                        coverOffset = BinaryPrimitives.ReadUInt32BigEndian(data);
                        break;
                    case 503:
                        meta.Title = TextNormalizer.Strip(encoding.GetString(data));
                        break;
                    case 524:
                        meta.Lang = TextNormalizer.Strip(encoding.GetString(data));
                        break;
                }

                pos += len;
            }
        }

        meta.DocDate ??= PdbDate(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(40)));

        if (includeCover && coverOffset is { } co && co != uint.MaxValue && firstImage != uint.MaxValue)
        {
            var index = (int)(firstImage + co);
            if (index < recordCount)
            {
                var end = index + 1 < recordCount ? offsets[index + 1] : stream.Length;
                var img = ReadExact(stream, offsets[index], (int)(end - offsets[index]));
                meta.Cover = new CoverImage(img, SniffImage(img));
            }
        }

        return meta;
    }

    /// <summary>PalmDB timestamps count seconds from 1904 (high bit set) or from the Unix epoch.</summary>
    private static string? PdbDate(uint value)
    {
        if (value == 0)
        {
            return null;
        }

        var origin = (value & 0x80000000) != 0 ? new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UnixEpoch;
        return origin.AddSeconds(value).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddAuthor(BookMetadata meta, string raw)
    {
        foreach (var a in raw.Split('&', ';'))
        {
            var s = TextNormalizer.Strip(a);
            if (s.Length == 0)
            {
                continue;
            }

            string name;
            if (s.Contains(','))
            {
                name = s.Replace(",", " ").Replace("  ", " ").Trim();
            }
            else
            {
                var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                name = parts.Length > 1 ? parts[^1] + " " + string.Join(' ', parts[..^1]) : s;
            }

            if (!meta.Authors.Contains(name))
            {
                meta.Authors.Add(name);
            }
        }
    }

    internal static string SniffImage(byte[] b) =>
        b.Length > 4 && b[0] == 0x89 && b[1] == 0x50 ? "image/png"
        : b.Length > 3 && b[0] == 0x47 && b[1] == 0x49 ? "image/gif"
        : "image/jpeg";

    private static uint U32(byte[] b, int offset) =>
        offset + 4 <= b.Length ? BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset)) : 0;

    private static byte[] ReadExact(Stream s, long offset, int count)
    {
        if (count < 0 || offset < 0 || offset > s.Length)
        {
            throw new FormatException("Corrupt MOBI record table.");
        }

        count = (int)Math.Min(count, s.Length - offset);
        var buf = new byte[count];
        s.Seek(offset, SeekOrigin.Begin);
        s.ReadExactly(buf);
        return buf;
    }
}
