using Nopds.Domain.Entities;

namespace Nopds.Domain.Text;

public static class LangCodes
{
    /// <summary>Classifies a string by its first letter (Cyrillic, Latin incl. diacritics, digits, other).</summary>
    public static LangCode Detect(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return LangCode.Other;
        }

        var c = s[0];
        if (c is >= '0' and <= '9')
        {
            return LangCode.Digits;
        }

        if (c is (>= 'Ѐ' and <= 'ӿ') or (>= 'Ԁ' and <= 'ԯ'))
        {
            return LangCode.Cyrillic;
        }

        if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= 'À' and <= 'ɏ' and not '×' and not '÷'))
        {
            return LangCode.Latin;
        }

        return LangCode.Other;
    }
}
