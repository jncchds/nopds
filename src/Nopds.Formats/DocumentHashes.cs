using System.IO.Hashing;
using System.Security.Cryptography;

namespace Nopds.Formats;

public static class DocumentHashes
{
    /// <summary>
    /// KOReader "partial MD5": 1 KiB samples at offsets 0 and 1024·4^i (i = 0..10), stopping at EOF.
    /// Used by KOReader sync to identify documents.
    /// </summary>
    public static string KoreaderPartialMd5(Stream s)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buf = new byte[1024];
        for (var i = -1; i <= 10; i++)
        {
            long offset = i < 0 ? 0 : 1024L << (2 * i);
            if (offset >= s.Length)
            {
                break;
            }

            s.Seek(offset, SeekOrigin.Begin);
            var read = s.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            if (read == 0)
            {
                break;
            }

            md5.AppendData(buf, 0, read);
        }

        s.Seek(0, SeekOrigin.Begin);
        return Convert.ToHexStringLower(md5.GetHashAndReset());
    }

    public static long ContentHash(Stream s)
    {
        var h = new XxHash64();
        s.Seek(0, SeekOrigin.Begin);
        h.Append(s);
        s.Seek(0, SeekOrigin.Begin);
        return (long)h.GetCurrentHashAsUInt64();
    }

    /// <summary>Stable 64-bit key of normalized title + sorted authors; editions of a work share it.</summary>
    public static long DuplicateKey(string title, IEnumerable<string> authors)
    {
        var key = Domain.Text.TextNormalizer.DupKey(title) + "|" +
                  string.Join('|', authors.Select(Domain.Text.TextNormalizer.DupKey).Order(StringComparer.Ordinal));
        return (long)XxHash64.HashToUInt64(System.Text.Encoding.UTF8.GetBytes(key));
    }
}
