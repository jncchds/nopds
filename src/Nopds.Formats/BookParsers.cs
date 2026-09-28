using System.Text;
using Nopds.Formats.Parsers;

namespace Nopds.Formats;

/// <summary>Registry of format parsers; unknown formats fall back to <see cref="GenericParser"/>.</summary>
public sealed class BookParsers
{
    private readonly Dictionary<string, IBookParser> _byFormat = new(StringComparer.OrdinalIgnoreCase);
    private readonly GenericParser _generic = new();

    static BookParsers() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public BookParsers(IEnumerable<IBookParser> parsers)
    {
        foreach (var p in parsers)
        {
            foreach (var f in p.Formats)
            {
                _byFormat[f] = p;
            }
        }
    }

    public static BookParsers CreateDefault() => new([new Fb2Parser(), new EpubParser(), new MobiParser(), new ComicParser(), new DocxParser(), new OdtParser(), new RtfParser(), new HtmlBookParser()]);

    public IBookParser For(string format) => _byFormat.TryGetValue(format, out var p) ? p : _generic;

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover = false)
    {
        var format = FormatOf(fileName);
        return For(format).Parse(stream, fileName, includeCover);
    }

    /// <summary>Lowercase extension without the dot; "fb2.zip" style names report the inner format.</summary>
    public static string FormatOf(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext;
    }
}
