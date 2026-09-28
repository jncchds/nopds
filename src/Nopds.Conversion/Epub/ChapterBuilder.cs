using System.Xml.Linq;

namespace Nopds.Conversion.Epub;

/// <summary>
/// Turns a flat run of XHTML blocks into spine files: splits at the top heading level present (h1–h3),
/// lists the next level as nested TOC entries, cuts very long chapters into continuation files and
/// appends collected notes as a final chapter.
/// </summary>
public static class ChapterBuilder
{
    /// <summary>Approximate text size per file; large XHTML documents make readers slow to paginate.</summary>
    private const int MaxChars = 150_000;

    private static readonly HashSet<string> Wrappers = ["div", "section", "article", "main", "body", "header", "footer"];

    public static void Build(EpubDocument doc, IEnumerable<XElement> blocks, IReadOnlyList<XElement>? notes = null, string notesTitle = "Notes")
    {
        var flat = new List<XElement>();
        foreach (var b in Flatten(blocks))
        {
            var empty = Xhtml.IsEmpty(b);
            var isP = b.Name == Xhtml.Ns + "p";
            if (empty && ((isP && b.Attribute("class") is null) || Xhtml.IsHeading(b, out _)))
            {
                continue;
            }

            // Spacer paragraphs: at most one in a row, never at the start.
            if (empty && isP && (flat.Count == 0 || (flat[^1].Name == Xhtml.Ns + "p" && (string?)flat[^1].Attribute("class") == "empty-line")))
            {
                continue;
            }

            flat.Add(b);
        }

        var title = string.IsNullOrWhiteSpace(doc.Meta.Title) ? "Untitled" : doc.Meta.Title;

        var splitLevel = 0;
        for (var l = 1; l <= 3 && splitLevel == 0; l++)
        {
            if (flat.Any(b => Xhtml.IsHeading(b, out var h) && h == l && !Xhtml.IsEmpty(b)))
            {
                splitLevel = l;
            }
        }

        var fileNo = doc.Chapters.Count;
        string NextFile() => $"text/ch{++fileNo:D3}.xhtml";
        var tocId = 0;

        var groups = new List<(string Title, List<XElement> Blocks)>();
        foreach (var b in flat)
        {
            if (splitLevel > 0 && Xhtml.IsHeading(b, out var level) && level == splitLevel && !Xhtml.IsEmpty(b))
            {
                groups.Add((Xhtml.Text(b), [b]));
            }
            else
            {
                if (groups.Count == 0)
                {
                    groups.Add((title, []));
                }

                groups[^1].Blocks.Add(b);
            }
        }

        foreach (var (groupTitle, groupBlocks) in groups)
        {
            if (groupBlocks.Count == 0 || groupBlocks.All(Xhtml.IsEmpty))
            {
                continue;
            }

            EpubChapter? first = null;
            EpubChapter? current = null;
            var size = 0;
            foreach (var b in groupBlocks)
            {
                var len = b.Value.Length;
                if (current is null || (size > 0 && size + len > MaxChars))
                {
                    current = new EpubChapter(NextFile(), groupTitle) { InToc = first is null };
                    first ??= current;
                    doc.Chapters.Add(current);
                    size = 0;
                }

                if (splitLevel > 0 && Xhtml.IsHeading(b, out var level) && level == splitLevel + 1 && !Xhtml.IsEmpty(b))
                {
                    var id = (string?)b.Attribute("id");
                    if (id is null)
                    {
                        id = $"toc{++tocId}";
                        b.SetAttributeValue("id", id);
                    }

                    first!.Children.Add((Xhtml.Text(b), current.File + "#" + id));
                }

                current.Body.Add(b);
                size += len;
            }
        }

        if (notes is { Count: > 0 })
        {
            var ch = new EpubChapter(NextFile(), notesTitle);
            ch.Body.Add(Xhtml.Heading(1, notesTitle));
            ch.Body.Add(notes);
            doc.Chapters.Add(ch);
        }
    }

    /// <summary>Unwraps container elements that hold headings, so chapters can be split at them.</summary>
    private static IEnumerable<XElement> Flatten(IEnumerable<XElement> blocks)
    {
        foreach (var b in blocks)
        {
            if (b.Name.Namespace == Xhtml.Ns && Wrappers.Contains(b.Name.LocalName)
                && b.Descendants().Any(d => Xhtml.IsHeading(d, out var l) && l <= 3))
            {
                // Loose text directly inside the wrapper becomes a paragraph.
                foreach (var node in b.Nodes().ToList())
                {
                    if (node is XText t && !string.IsNullOrWhiteSpace(t.Value))
                    {
                        node.ReplaceWith(Xhtml.El("p", t.Value.Trim()));
                    }
                }

                // Keep an id on the wrapper reachable: move it to the first child.
                var children = b.Elements().ToList();
                if ((string?)b.Attribute("id") is { } id && children.Count > 0 && children[0].Attribute("id") is null)
                {
                    children[0].SetAttributeValue("id", id);
                }

                foreach (var c in Flatten(children))
                {
                    yield return c;
                }
            }
            else
            {
                yield return b;
            }
        }
    }
}
