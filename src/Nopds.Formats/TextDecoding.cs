using System.Text;

namespace Nopds.Formats;

/// <summary>Decodes plain-text files of unknown encoding: BOM, strict UTF-8, then the best-scoring legacy code page.</summary>
public static class TextDecoding
{
    static TextDecoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Encoding by name or code page number, including legacy code pages; null when unknown.</summary>
    public static Encoding? TryGetEncoding(string nameOrCodePage)
    {
        try
        {
            return int.TryParse(nameOrCodePage, out var cp) ? Encoding.GetEncoding(cp) : Encoding.GetEncoding(nameOrCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static string Decode(byte[] bytes) => Detect(bytes, out var bom).GetString(bytes, bom, bytes.Length - bom);

    public static Encoding Detect(byte[] bytes, out int bomLength)
    {
        bomLength = 0;
        switch (bytes)
        {
            case [0xEF, 0xBB, 0xBF, ..]:
                bomLength = 3;
                return Encoding.UTF8;
            case [0xFF, 0xFE, ..]:
                bomLength = 2;
                return Encoding.Unicode;
            case [0xFE, 0xFF, ..]:
                bomLength = 2;
                return Encoding.BigEndianUnicode;
        }

        try
        {
            // Probe the first megabyte, cutting back to a character boundary so a split sequence is not an error.
            var probe = bytes.AsSpan();
            if (probe.Length > 1 << 20)
            {
                var end = 1 << 20;
                while (end > (1 << 20) - 4 && (probe[end] & 0xC0) == 0x80)
                {
                    end--;
                }

                probe = probe[..end];
            }

            StrictUtf8.GetCharCount(probe);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
        }

        return BestLegacy(bytes);
    }

    /// <summary>
    /// Scores Cyrillic code pages by how many decoded letters are lowercase Cyrillic (the common case in
    /// running text); falls back to Windows-1252 when the text is not Cyrillic.
    /// </summary>
    private static Encoding BestLegacy(byte[] bytes)
    {
        var sample = bytes.Length > 64 * 1024 ? bytes.AsSpan(0, 64 * 1024).ToArray() : bytes;
        var high = sample.Count(b => b >= 0x80);
        Encoding best = Encoding.GetEncoding(1252);
        var bestScore = high / 2;
        foreach (var cp in new[] { 1251, 20866, 866 })
        {
            var enc = Encoding.GetEncoding(cp);
            var score = 0;
            foreach (var ch in enc.GetString(sample))
            {
                if (ch is >= 'а' and <= 'я' or 'ё' or 'і' or 'ї' or 'є' or 'ґ' or 'ў')
                {
                    score++;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = enc;
            }
        }

        return best;
    }
}
