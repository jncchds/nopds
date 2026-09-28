using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nopds.Conversion.Epub;

/// <summary>Small helpers for building the XHTML content that converters hand to <see cref="EpubWriter"/>.</summary>
public static partial class Xhtml
{
    public static readonly XNamespace Ns = "http://www.w3.org/1999/xhtml";
    public static readonly XNamespace Ops = "http://www.idpf.org/2007/ops";

    public static XElement El(string name, params object?[] content) => new(Ns + name, content);

    public static bool IsHeading(XElement e, out int level)
    {
        level = 0;
        var n = e.Name.LocalName;
        if (e.Name.Namespace == Ns && n.Length == 2 && n[0] == 'h' && n[1] is >= '1' and <= '6')
        {
            level = n[1] - '0';
            return true;
        }

        return false;
    }

    public static XElement Heading(int level, params object?[] content) => El("h" + Math.Clamp(level, 1, 6), content);

    /// <summary>A valid, reasonably readable XML id derived from arbitrary text (bookmark names, note ids).</summary>
    public static string SafeId(string raw)
    {
        var s = InvalidId().Replace(raw, "_");
        return s.Length == 0 || !char.IsLetter(s[0]) ? "x" + s : s;
    }

    /// <summary>Superscript note marker that reading systems turn into a popup footnote.</summary>
    public static XElement NoteRef(string noteId, string label) =>
        new(Ns + "a",
            new XAttribute("id", "ref-" + noteId),
            new XAttribute("href", "#" + noteId),
            new XAttribute("class", "noteref"),
            new XAttribute(Ops + "type", "noteref"),
            label);

    /// <summary>Note body for the notes chapter; the label links back to the reference.</summary>
    public static XElement Note(string noteId, string label, IEnumerable<XElement> paragraphs)
    {
        var note = new XElement(Ns + "div", new XAttribute("id", noteId), new XAttribute("class", "note"), new XAttribute(Ops + "type", "footnote"));
        var back = new XElement(Ns + "a", new XAttribute("href", "#ref-" + noteId), label);
        var first = true;
        foreach (var p in paragraphs)
        {
            if (first && p.Name == Ns + "p")
            {
                p.AddFirst(back, " ");
                first = false;
            }

            note.Add(p);
        }

        if (first)
        {
            note.AddFirst(El("p", back));
        }

        return note;
    }

    /// <summary>Collapsed plain text of an element, for titles.</summary>
    public static string Text(XElement e, int max = 120)
    {
        var sb = new StringBuilder();
        foreach (var t in e.DescendantNodes().OfType<XText>())
        {
            sb.Append(t.Value);
        }

        var s = Spaces().Replace(sb.ToString(), " ").Trim();
        return s.Length > max ? s[..max] + "…" : s;
    }

    /// <summary>True when the element has no text and no image.</summary>
    public static bool IsEmpty(XElement e) =>
        string.IsNullOrWhiteSpace(e.Value) && !e.Descendants(Ns + "img").Any();

    /// <summary>Removes characters that are not allowed in XML 1.0 text.</summary>
    public static string CleanText(string s) => InvalidXmlChars().Replace(s, string.Empty);

    [GeneratedRegex(@"[^\w\-.]")]
    private static partial Regex InvalidId();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\uFFFE\uFFFF]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]")]
    private static partial Regex InvalidXmlChars();
}
