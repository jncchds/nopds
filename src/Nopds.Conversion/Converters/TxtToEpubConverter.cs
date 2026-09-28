using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nopds.Conversion.Epub;
using Nopds.Formats;

namespace Nopds.Conversion.Converters;

/// <summary>
/// Plain text → EPUB. Detects the encoding, how paragraphs are delimited (blank lines, indented first
/// lines or one line each) and chapter headings ("Chapter 5", "Глава V", "Rozdział 3", lone numerals).
/// </summary>
public sealed partial class TxtToEpubConverter : IBookConverter
{
    public IReadOnlyCollection<string> Sources { get; } = ["txt"];

    public string Target => "epub";

    public void Convert(byte[] input, BookMetadata meta, Stream output)
    {
        var text = Xhtml.CleanText(TextDecoding.Decode(input)).Replace("\r\n", "\n").Replace('\r', '\n');
        var doc = new EpubDocument { Meta = MetadataMerge.Combine(meta, null) };
        ChapterBuilder.Build(doc, ToBlocks(text));
        EpubWriter.Write(doc, output);
    }

    public static IEnumerable<XElement> ToBlocks(string text)
    {
        foreach (var para in Paragraphs(text.Split('\n')))
        {
            var line = para.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (SceneBreak().IsMatch(line))
            {
                yield return Xhtml.El("p", new XAttribute("class", "scene-break"), line);
            }
            else if (line.Length <= 80 && ChapterHeading().IsMatch(line))
            {
                yield return Xhtml.Heading(1, line);
            }
            else
            {
                yield return Xhtml.El("p", line);
            }
        }
    }

    /// <summary>Groups lines into paragraphs according to the layout the file appears to use.</summary>
    private static IEnumerable<string> Paragraphs(string[] lines)
    {
        var nonBlank = lines.Count(l => l.Trim().Length > 0);
        if (nonBlank == 0)
        {
            yield break;
        }

        var blank = lines.Length - nonBlank;
        var indented = lines.Count(l => IsIndented(l) && l.Trim().Length > 0);

        // Hard-wrapped text: paragraphs separated by blank lines, most blocks span several lines.
        var multiLineBlocks = 0;
        var blocks = 0;
        var run = 0;
        foreach (var l in lines.Append(string.Empty))
        {
            if (l.Trim().Length == 0)
            {
                if (run > 0)
                {
                    blocks++;
                    multiLineBlocks += run > 1 ? 1 : 0;
                }

                run = 0;
            }
            else
            {
                run++;
            }
        }

        if (blank > 0 && multiLineBlocks * 10 >= blocks * 3)
        {
            // Within a block, an indented line, a heading or a scene break still starts a new paragraph.
            var current = new List<string>();
            var standalone = false;
            foreach (var l in lines.Append(string.Empty))
            {
                var trimmed = l.Trim();
                var special = trimmed.Length > 0 && (SceneBreak().IsMatch(trimmed) || (trimmed.Length <= 80 && ChapterHeading().IsMatch(trimmed)));
                if (current.Count > 0 && (trimmed.Length == 0 || IsIndented(l) || special || standalone))
                {
                    yield return string.Join(' ', current.Select(c => c.Trim()));
                    current.Clear();
                }

                if (trimmed.Length > 0)
                {
                    current.Add(l);
                }

                standalone = special;
            }

            yield break;
        }

        // Hard-wrapped text with indented first lines and no blank separators.
        if (indented * 10 >= nonBlank && indented * 10 <= nonBlank * 7 && blank * 10 < nonBlank)
        {
            var current = new List<string>();
            foreach (var l in lines)
            {
                if (l.Trim().Length == 0 || IsIndented(l) || ChapterHeading().IsMatch(l.Trim()))
                {
                    if (current.Count > 0)
                    {
                        yield return string.Join(' ', current.Select(c => c.Trim()));
                        current.Clear();
                    }
                }

                if (l.Trim().Length > 0)
                {
                    current.Add(l);
                }
            }

            if (current.Count > 0)
            {
                yield return string.Join(' ', current.Select(c => c.Trim()));
            }

            yield break;
        }

        // One paragraph per line.
        foreach (var l in lines)
        {
            yield return l;
        }
    }

    private static bool IsIndented(string line) => line.Length > 0 && line[0] is ' ' or '\t' or '\u3000';

    [GeneratedRegex(@"^(?:(?:chapter|part|book|prologue|epilogue|глава|часть|книга|пролог|эпилог|розділ|частина|епілог|rozdział|część|księga|kapitel|teil|buch)\b.{0,70}|[IVXLC]{1,8}\.?|\d{1,3}\.?)$", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterHeading();

    [GeneratedRegex(@"^(?:[*#~\-_=•·]\s*){3,}$")]
    private static partial Regex SceneBreak();
}
