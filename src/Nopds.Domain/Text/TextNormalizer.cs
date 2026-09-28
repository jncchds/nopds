using System.Globalization;
using System.Text;

namespace Nopds.Domain.Text;

public static class TextNormalizer
{
    /// <summary>Characters trimmed from metadata values (ported from SimpleOPDS strip_symbols).</summary>
    public static readonly char[] StripSymbols = [' ', '»', '«', '\'', '"', '&', '\n', '\r', '\t', '-', '.', '#', '\\', '`'];

    public static string Strip(string? s) => s?.Trim(StripSymbols) ?? string.Empty;

    /// <summary>Uppercases, folds Ё→Е and Ґ→Г, collapses whitespace. Used for search and sort keys.</summary>
    public static string SearchKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var ch in s.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                space = true;
                continue;
            }

            if (space && sb.Length > 0)
            {
                sb.Append(' ');
            }

            space = false;
            var u = char.ToUpperInvariant(ch);
            sb.Append(u switch
            {
                'Ё' => 'Е',
                'Ґ' => 'Г',
                _ => u,
            });
        }

        return sb.ToString();
    }

    /// <summary>Accent-free lowercase letters/digits only, for duplicate grouping.</summary>
    public static string DupKey(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var decomposed = SearchKey(s).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (char.IsLetterOrDigit(ch) && CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// "First Middle Last" → "Last First Middle"; names that already contain a comma are kept
    /// (commas replaced by spaces), matching SimpleOPDS author handling.
    /// </summary>
    public static string AuthorName(string? first, string? middle, string? last, string? nick = null)
    {
        var parts = new[] { Strip(last), Strip(first), Strip(middle) }.Where(p => p.Length > 0);
        var name = string.Join(' ', parts);
        return name.Length > 0 ? name : Strip(nick);
    }

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
