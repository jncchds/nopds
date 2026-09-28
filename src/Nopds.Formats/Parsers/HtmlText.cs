using System.Net;
using System.Text.RegularExpressions;

namespace Nopds.Formats.Parsers;

internal static partial class HtmlText
{
    /// <summary>Converts a small HTML fragment (EPUB/MOBI description) to plain text with line breaks.</summary>
    public static string ToPlain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var s = BlockEnd().Replace(html, "\n");
        s = Tags().Replace(s, string.Empty);
        s = WebUtility.HtmlDecode(s);
        var lines = s.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"</(p|div|li|h\d)>|<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex Spaces();
}
