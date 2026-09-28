using System.Text;

namespace Nopds.Domain.Text;

/// <summary>Cyrillic (Russian + Ukrainian) to ASCII transliteration for download file names.</summary>
public static class Translit
{
    private static readonly Dictionary<char, string> Map = Build();

    private static Dictionary<char, string> Build()
    {
        var lower = new Dictionary<char, string>
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['ґ'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
            ['є'] = "je", ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['і'] = "i", ['ї'] = "ji", ['й'] = "j", ['к'] = "k",
            ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
            ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch",
            ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "ju", ['я'] = "ja",
        };
        var map = new Dictionary<char, string>(lower);
        foreach (var (k, v) in lower)
        {
            map[char.ToUpperInvariant(k)] = v.Length > 0 ? char.ToUpperInvariant(v[0]) + v[1..] : v;
        }

        map['«'] = map['»'] = map['"'] = map['\''] = map['`'] = "";
        map[' '] = map['\n'] = map[':'] = "_";
        map['№'] = "N";
        return map;
    }

    public static string ToAscii(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (Map.TryGetValue(ch, out var rep))
            {
                sb.Append(rep);
            }
            else if (ch < 128 && !char.IsControl(ch) && ch is not ('/' or '\\' or '?' or '*' or '<' or '>' or '|'))
            {
                sb.Append(ch);
            }
            else if (char.IsLetterOrDigit(ch))
            {
                // Latin letters with diacritics: strip marks.
                var d = ch.ToString().Normalize(NormalizationForm.FormD);
                sb.Append(d.Where(c => c < 128).ToArray());
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.ToString();
    }
}
