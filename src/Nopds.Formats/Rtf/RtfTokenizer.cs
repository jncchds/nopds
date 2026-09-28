namespace Nopds.Formats.Rtf;

public enum RtfTokenType
{
    GroupStart,
    GroupEnd,

    /// <summary>Control word, e.g. \b0 → Word "b", Param 0.</summary>
    Word,

    /// <summary>Control symbol, e.g. \~ or \*.</summary>
    Symbol,

    /// <summary>\'hh escaped byte; the value is in Param.</summary>
    Hex,

    /// <summary>Plain text bytes (Start/Length into the source).</summary>
    Text,

    /// <summary>\binN payload (Start/Length into the source).</summary>
    Binary,
}

public readonly record struct RtfToken(RtfTokenType Type, string? Word = null, int? Param = null, char Symbol = '\0', int Start = 0, int Length = 0);

/// <summary>Splits RTF bytes into groups, control words/symbols and text runs. Decoding is left to the caller.</summary>
public static class RtfTokenizer
{
    public static IEnumerable<RtfToken> Tokenize(byte[] data, int length = -1)
    {
        var n = length < 0 ? data.Length : Math.Min(length, data.Length);
        var i = 0;
        while (i < n)
        {
            var b = data[i];
            switch (b)
            {
                case (byte)'{':
                    i++;
                    yield return new RtfToken(RtfTokenType.GroupStart);
                    break;
                case (byte)'}':
                    i++;
                    yield return new RtfToken(RtfTokenType.GroupEnd);
                    break;
                case (byte)'\r' or (byte)'\n':
                    i++;
                    break;
                case (byte)'\\':
                    i++;
                    if (i >= n)
                    {
                        yield break;
                    }

                    var c = data[i];
                    if (IsLetter(c))
                    {
                        var start = i;
                        while (i < n && IsLetter(data[i]) && i - start < 32)
                        {
                            i++;
                        }

                        var word = System.Text.Encoding.ASCII.GetString(data, start, i - start);
                        int? param = null;
                        var negative = i < n && data[i] == '-';
                        var digitsStart = negative ? i + 1 : i;
                        var j = digitsStart;
                        long value = 0;
                        while (j < n && data[j] is >= (byte)'0' and <= (byte)'9' && j - digitsStart < 10)
                        {
                            value = value * 10 + (data[j] - '0');
                            j++;
                        }

                        if (j > digitsStart)
                        {
                            param = (int)Math.Clamp(negative ? -value : value, int.MinValue, int.MaxValue);
                            i = j;
                        }

                        if (i < n && data[i] == ' ')
                        {
                            i++;
                        }

                        if (word == "bin" && param > 0)
                        {
                            var len = Math.Min(param.Value, n - i);
                            yield return new RtfToken(RtfTokenType.Binary, Start: i, Length: len);
                            i += len;
                        }
                        else
                        {
                            yield return new RtfToken(RtfTokenType.Word, word, param);
                        }
                    }
                    else if (c == '\'')
                    {
                        if (i + 2 < n && Hex(data[i + 1]) is >= 0 and var hi && Hex(data[i + 2]) is >= 0 and var lo)
                        {
                            i += 3;
                            yield return new RtfToken(RtfTokenType.Hex, Param: hi * 16 + lo);
                        }
                        else
                        {
                            i++;
                        }
                    }
                    else if (c is (byte)'\r' or (byte)'\n')
                    {
                        // A backslash before a line break is a paragraph mark.
                        i++;
                        yield return new RtfToken(RtfTokenType.Word, "par");
                    }
                    else
                    {
                        i++;
                        yield return new RtfToken(RtfTokenType.Symbol, Symbol: (char)c);
                    }

                    break;
                default:
                    var textStart = i;
                    while (i < n && data[i] is not ((byte)'{' or (byte)'}' or (byte)'\\' or (byte)'\r' or (byte)'\n'))
                    {
                        i++;
                    }

                    yield return new RtfToken(RtfTokenType.Text, Start: textStart, Length: i - textStart);
                    break;
            }
        }
    }

    /// <summary>Windows code page for an RTF \fcharset value, or null when it has none of its own.</summary>
    public static int? CodePageForCharset(int charset) => charset switch
    {
        0 => 1252,
        77 => 10000,
        128 => 932,
        129 => 949,
        134 => 936,
        136 => 950,
        161 => 1253,
        162 => 1254,
        163 => 1258,
        177 => 1255,
        178 => 1256,
        186 => 1257,
        204 => 1251,
        222 => 874,
        238 => 1250,
        _ => null,
    };

    private static bool IsLetter(byte b) => b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';

    private static int Hex(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };
}
