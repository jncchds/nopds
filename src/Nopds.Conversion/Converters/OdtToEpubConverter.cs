using System.IO.Compression;
using System.Xml.Linq;
using Nopds.Conversion.Epub;
using Nopds.Formats;
using Nopds.Formats.Parsers;

namespace Nopds.Conversion.Converters;

/// <summary>
/// OpenDocument text (ODT) → EPUB. Headings come from text:h outline levels; bold/italic/underline/
/// strike/super/subscript are resolved from automatic and named styles; lists, tables, links,
/// bookmarks, embedded images and notes are kept.
/// </summary>
public sealed class OdtToEpubConverter : IBookConverter
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    public IReadOnlyCollection<string> Sources { get; } = ["odt"];

    public string Target => "epub";

    [Flags]
    private enum Fmt
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Underline = 4,
        Strike = 8,
        Super = 16,
        Sub = 32,
        Center = 64,
        Right = 128,
    }

    private sealed class Context(ZipArchive zip, EpubDocument doc)
    {
        public ZipArchive Zip { get; } = zip;
        public EpubDocument Doc { get; } = doc;
        public Dictionary<string, Fmt> Styles { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Parents { get; } = new(StringComparer.Ordinal);
        public List<XElement> Notes { get; } = [];
    }

    public void Convert(byte[] input, BookMetadata meta, Stream output)
    {
        using var zip = new ZipArchive(new MemoryStream(input), ZipArchiveMode.Read);
        var doc = new EpubDocument { Meta = MetadataMerge.Combine(meta, OdtParser.ReadMeta(zip)) };
        var ctx = new Context(zip, doc);

        var content = DocxToEpubConverter.Load(zip, "content.xml") ?? throw new FormatException("ODT without content.xml.");
        LoadStyles(ctx, DocxToEpubConverter.Load(zip, "styles.xml")?.Root?.Element(Office + "styles"));
        LoadStyles(ctx, DocxToEpubConverter.Load(zip, "styles.xml")?.Root?.Element(Office + "automatic-styles"));
        LoadStyles(ctx, content.Root?.Element(Office + "automatic-styles"));

        var text = content.Root?.Element(Office + "body")?.Element(Office + "text");
        var blocks = text is null ? [] : Blocks(text.Elements(), ctx).ToList();
        ChapterBuilder.Build(doc, blocks, ctx.Notes);
        EpubWriter.Write(doc, output);
    }

    private static void LoadStyles(Context ctx, XElement? styles)
    {
        foreach (var s in styles?.Elements(Style + "style") ?? [])
        {
            var name = (string?)s.Attribute(Style + "name");
            if (name is null)
            {
                continue;
            }

            if ((string?)s.Attribute(Style + "parent-style-name") is { } parent)
            {
                ctx.Parents[name] = parent;
            }

            var f = Fmt.None;
            var tp = s.Element(Style + "text-properties");
            if (tp is not null)
            {
                if ((string?)tp.Attribute(Fo + "font-weight") is "bold" or "700" or "800" or "900")
                {
                    f |= Fmt.Bold;
                }

                if ((string?)tp.Attribute(Fo + "font-style") is "italic" or "oblique")
                {
                    f |= Fmt.Italic;
                }

                if ((string?)tp.Attribute(Style + "text-underline-style") is { } u && u != "none")
                {
                    f |= Fmt.Underline;
                }

                if ((string?)tp.Attribute(Style + "text-line-through-style") is { } lt && lt != "none")
                {
                    f |= Fmt.Strike;
                }

                var pos = (string?)tp.Attribute(Style + "text-position") ?? "";
                if (pos.StartsWith("super", StringComparison.Ordinal) || (pos.Length > 0 && char.IsDigit(pos[0]) && pos[0] != '0'))
                {
                    f |= Fmt.Super;
                }
                else if (pos.StartsWith("sub", StringComparison.Ordinal) || pos.StartsWith('-'))
                {
                    f |= Fmt.Sub;
                }
            }

            switch ((string?)s.Element(Style + "paragraph-properties")?.Attribute(Fo + "text-align"))
            {
                case "center":
                    f |= Fmt.Center;
                    break;
                case "end" or "right":
                    f |= Fmt.Right;
                    break;
            }

            ctx.Styles[name] = f;
        }
    }

    /// <summary>Formatting of a style including what it inherits from its parents.</summary>
    private static Fmt Resolve(Context ctx, string? style)
    {
        var f = Fmt.None;
        for (var depth = 0; style is not null && depth < 10; depth++)
        {
            f |= ctx.Styles.GetValueOrDefault(style);
            style = ctx.Parents.GetValueOrDefault(style);
        }

        return f;
    }

    private static IEnumerable<XElement> Blocks(IEnumerable<XElement> elements, Context ctx)
    {
        foreach (var el in elements)
        {
            var n = el.Name;
            if (n == Text + "h")
            {
                var level = (int?)el.Attribute(Text + "outline-level") ?? 1;
                yield return Xhtml.Heading(level, Inlines(el.Nodes(), ctx, Fmt.None));
            }
            else if (n == Text + "p")
            {
                var f = Resolve(ctx, (string?)el.Attribute(Text + "style-name"));
                var p = Xhtml.El("p", Inlines(el.Nodes(), ctx, f & ~(Fmt.Center | Fmt.Right)));
                if (Xhtml.IsEmpty(p))
                {
                    p = Xhtml.El("p", new XAttribute("class", "empty-line"), " ");
                }
                else if (f.HasFlag(Fmt.Center))
                {
                    p.SetAttributeValue("class", "center");
                }
                else if (f.HasFlag(Fmt.Right))
                {
                    p.SetAttributeValue("class", "right");
                }

                yield return p;
            }
            else if (n == Text + "list")
            {
                var list = Xhtml.El("ul");
                foreach (var item in el.Elements().Where(e => e.Name == Text + "list-item" || e.Name == Text + "list-header"))
                {
                    list.Add(Xhtml.El("li", Blocks(item.Elements(), ctx)));
                }

                yield return list;
            }
            else if (n == Table + "table")
            {
                yield return TableElement(el, ctx);
            }
            else if (n == Text + "section" || n == Text + "index-body")
            {
                foreach (var b in Blocks(el.Elements(), ctx))
                {
                    yield return b;
                }
            }
            else if (el.Element(Text + "index-body") is { } index)
            {
                // Table of contents and other generated indexes.
                foreach (var b in Blocks(index.Elements(), ctx))
                {
                    yield return b;
                }
            }
            else if (n == Draw + "frame")
            {
                var imgs = Inlines([el], ctx, Fmt.None).ToList();
                if (imgs.Count > 0)
                {
                    yield return Xhtml.El("div", new XAttribute("class", "image"), imgs);
                }
            }
        }
    }

    private static XElement TableElement(XElement tbl, Context ctx)
    {
        var table = Xhtml.El("table");
        var rows = tbl.Elements(Table + "table-header-rows").SelectMany(h => h.Elements(Table + "table-row"))
            .Concat(tbl.Elements(Table + "table-row"))
            .Concat(tbl.Elements(Table + "table-rows").SelectMany(r => r.Elements(Table + "table-row")));
        foreach (var tr in rows)
        {
            var row = Xhtml.El("tr");
            foreach (var tc in tr.Elements(Table + "table-cell"))
            {
                var cell = Xhtml.El("td", Blocks(tc.Elements(), ctx).Where(b => !Xhtml.IsEmpty(b)));
                if ((int?)tc.Attribute(Table + "number-columns-spanned") is > 1 and var cs)
                {
                    cell.SetAttributeValue("colspan", cs);
                }

                if ((int?)tc.Attribute(Table + "number-rows-spanned") is > 1 and var rs)
                {
                    cell.SetAttributeValue("rowspan", rs);
                }

                row.Add(cell);
            }

            table.Add(row);
        }

        return table;
    }

    private static IEnumerable<XNode> Inlines(IEnumerable<XNode> nodes, Context ctx, Fmt inherited)
    {
        foreach (var node in nodes)
        {
            if (node is XText t)
            {
                yield return Wrap(new XText(Xhtml.CleanText(t.Value)), inherited);
                continue;
            }

            if (node is not XElement el)
            {
                continue;
            }

            var n = el.Name;
            if (n == Text + "span")
            {
                var f = inherited | Resolve(ctx, (string?)el.Attribute(Text + "style-name"));
                foreach (var c in Inlines(el.Nodes(), ctx, f & ~(Fmt.Center | Fmt.Right)))
                {
                    yield return c;
                }
            }
            else if (n == Text + "s")
            {
                yield return new XText(new string(' ', Math.Clamp((int?)el.Attribute(Text + "c") ?? 1, 1, 100)));
            }
            else if (n == Text + "tab")
            {
                yield return new XText(" ");
            }
            else if (n == Text + "line-break")
            {
                yield return Xhtml.El("br");
            }
            else if (n == Text + "a")
            {
                var a = Xhtml.El("a", Inlines(el.Nodes(), ctx, inherited));
                var href = (string?)el.Attribute(XLink + "href") ?? "";
                if (href.StartsWith('#'))
                {
                    a.SetAttributeValue("href", "#" + Xhtml.SafeId(href[1..]));
                }
                else if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    a.SetAttributeValue("href", href);
                }

                yield return a;
            }
            else if (n == Text + "bookmark" || n == Text + "bookmark-start")
            {
                if ((string?)el.Attribute(Text + "name") is { } name)
                {
                    yield return Xhtml.El("span", new XAttribute("id", Xhtml.SafeId(name)));
                }
            }
            else if (n == Text + "note")
            {
                var body = el.Element(Text + "note-body");
                var label = (ctx.Notes.Count + 1).ToString();
                var id = "note" + label;
                ctx.Notes.Add(Xhtml.Note(id, label, body is null ? [] : Blocks(body.Elements(), ctx).Where(b => !Xhtml.IsEmpty(b)).ToList()));
                yield return Xhtml.NoteRef(id, label);
            }
            else if (n == Draw + "frame")
            {
                var image = el.Element(Draw + "image");
                if ((string?)image?.Attribute(XLink + "href") is { } path && ctx.Zip.GetEntry(path) is { } entry)
                {
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    if (ctx.Doc.AddImage(path, ms.ToArray()) is { } href)
                    {
                        var alt = (string?)el.Element(Svg("title")) ?? (string?)el.Attribute(Draw + "name") ?? "";
                        yield return Xhtml.El("img", new XAttribute("src", href), new XAttribute("alt", alt));
                    }
                }
                else if (el.Element(Draw + "text-box") is { } box)
                {
                    foreach (var c in Blocks(box.Elements(), ctx).SelectMany(b => b.Nodes()))
                    {
                        yield return c;
                    }
                }
            }
            else if (n == Draw + "a")
            {
                foreach (var c in Inlines(el.Nodes(), ctx, inherited))
                {
                    yield return c;
                }
            }
            else if (n == Text + "soft-page-break" || n.Namespace == Office || n == Text + "bookmark-end" || n == Text + "reference-mark-end")
            {
                // Layout hints and annotations.
            }
            else
            {
                // Fields (page numbers, dates, references) and unknown inline elements: keep their text.
                foreach (var c in Inlines(el.Nodes(), ctx, inherited))
                {
                    yield return c;
                }
            }
        }
    }

    private static XName Svg(string local) => XName.Get(local, "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0");

    private static XNode Wrap(XNode node, Fmt f)
    {
        if (f.HasFlag(Fmt.Super))
        {
            node = Xhtml.El("sup", node);
        }
        else if (f.HasFlag(Fmt.Sub))
        {
            node = Xhtml.El("sub", node);
        }

        if (f.HasFlag(Fmt.Strike))
        {
            node = Xhtml.El("s", node);
        }

        if (f.HasFlag(Fmt.Underline))
        {
            node = Xhtml.El("u", node);
        }

        if (f.HasFlag(Fmt.Italic))
        {
            node = Xhtml.El("em", node);
        }

        if (f.HasFlag(Fmt.Bold))
        {
            node = Xhtml.El("strong", node);
        }

        return node;
    }
}
