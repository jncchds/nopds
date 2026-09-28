namespace Nopds.Domain.Text;

/// <summary>
/// Supported UI languages: uk, en, pl, de. Russian and Belarusian fall back to Ukrainian,
/// everything else falls back to English.
/// </summary>
public static class UiLanguages
{
    public const string Default = "en";
    public static readonly string[] Supported = ["en", "uk", "pl", "de"];

    /// <summary>Maps one language tag (e.g. "de-AT", "ru") to a supported language, or null when unknown.</summary>
    public static string? Match(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var primary = tag.Trim().Split('-', '_')[0].ToLowerInvariant();
        return primary switch
        {
            "en" or "uk" or "pl" or "de" => primary,
            "ua" or "ru" or "be" => "uk",
            _ => null,
        };
    }

    /// <summary>Picks the first supported language from a preference list, English if none matches.</summary>
    public static string Resolve(IEnumerable<string?> preferred)
    {
        foreach (var tag in preferred)
        {
            if (Match(tag) is { } lang)
            {
                return lang;
            }
        }

        return Default;
    }

    /// <summary>Resolves an HTTP Accept-Language header value (quality values respected).</summary>
    public static string FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return Default;
        }

        var tags = header.Split(',')
            .Select((part, index) =>
            {
                var bits = part.Split(';');
                var q = 1.0;
                foreach (var b in bits.Skip(1))
                {
                    var kv = b.Trim();
                    if (kv.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(kv[2..], System.Globalization.CultureInfo.InvariantCulture, out var v))
                    {
                        q = v;
                    }
                }

                return (tag: bits[0].Trim(), q, index);
            })
            .Where(t => t.q > 0)
            .OrderByDescending(t => t.q)
            .ThenBy(t => t.index)
            .Select(t => t.tag);
        return Resolve(tags);
    }
}
