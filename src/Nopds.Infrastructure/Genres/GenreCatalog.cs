using System.Collections.Frozen;
using Nopds.Domain.Text;

namespace Nopds.Infrastructure.Genres;

/// <summary>Localized FB2 genre and section names (en/uk/pl/de), loaded from the embedded genres.txt.</summary>
public sealed class GenreCatalog
{
    public const string UnknownSection = "unknown";

    public sealed record Entry(string Code, string Section, IReadOnlyDictionary<string, string> Names);

    private readonly FrozenDictionary<string, Entry> _genres;
    private readonly FrozenDictionary<string, IReadOnlyDictionary<string, string>> _sections;

    public IReadOnlyList<string> SectionOrder { get; }
    public IEnumerable<Entry> Genres => _genres.Values;

    private GenreCatalog(Dictionary<string, Entry> genres, Dictionary<string, IReadOnlyDictionary<string, string>> sections, List<string> order)
    {
        _genres = genres.ToFrozenDictionary();
        _sections = sections.ToFrozenDictionary();
        SectionOrder = order;
    }

    public static GenreCatalog Load()
    {
        using var stream = typeof(GenreCatalog).Assembly.GetManifestResourceStream("Nopds.genres.txt")
                           ?? throw new InvalidOperationException("genres.txt resource missing");
        using var reader = new StreamReader(stream);
        var genres = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var sections = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var order = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var p = line.Split('|');
            if (line[0] == '@')
            {
                var key = p[0][1..];
                sections[key] = Names(p, 1);
                order.Add(key);
            }
            else
            {
                genres[p[0]] = new Entry(p[0], p[1], Names(p, 2));
            }
        }

        return new GenreCatalog(genres, sections, order);
    }

    private static Dictionary<string, string> Names(string[] p, int start) => new()
    {
        ["en"] = p[start],
        ["uk"] = p[start + 1],
        ["pl"] = p[start + 2],
        ["de"] = p[start + 3],
    };

    public string? SectionOf(string code) => _genres.TryGetValue(code, out var e) ? e.Section : null;

    public string GenreName(string code, string lang) =>
        _genres.TryGetValue(code, out var e) ? Pick(e.Names, lang) : code;

    public string SectionName(string section, string lang) =>
        _sections.TryGetValue(section, out var names) ? Pick(names, lang) : section;

    private static string Pick(IReadOnlyDictionary<string, string> names, string lang) =>
        names.TryGetValue(UiLanguages.Match(lang) ?? UiLanguages.Default, out var n) ? n : names[UiLanguages.Default];
}
