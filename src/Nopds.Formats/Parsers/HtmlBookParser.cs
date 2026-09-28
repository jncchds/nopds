using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Nopds.Formats.Parsers;

/// <summary>HTML books: title from &lt;title&gt;, author and description from meta tags, language from &lt;html lang&gt;.</summary>
public sealed partial class HtmlBookParser : IBookParser
{
    private const int HeadBytes = 64 * 1024;

    public IReadOnlyCollection<string> Formats { get; } = ["html", "htm", "xhtml"];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover)
    {
        var buffer = new byte[HeadBytes];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var bytes = buffer.AsSpan(0, read).ToArray();
        var ascii = Encoding.ASCII.GetString(bytes);
        var encoding = Charset().Match(ascii) is { Success: true } m ? TextDecoding.TryGetEncoding(m.Groups[1].Value) : null;
        var head = (encoding ?? TextDecoding.Detect(bytes, out _)).GetString(bytes);

        var meta = new BookMetadata
        {
            Title = Clean(Title().Match(head).Groups[1].Value) ?? DocumentMeta.TitleFromFileName(fileName),
            Lang = DocumentMeta.Language(Lang().Match(head).Groups[1].Value),
            Annotation = Clean(Meta("description", head)),
        };
        DocumentMeta.AddAuthors(meta, Clean(Meta("author", head)));
        return meta;
    }

    private static string? Meta(string name, string head)
    {
        foreach (Match m in MetaTag().Matches(head))
        {
            var tag = m.Value;
            if (Regex.IsMatch(tag, $@"name\s*=\s*[""']?{name}[""'\s>]", RegexOptions.IgnoreCase) && ContentAttr().Match(tag) is { Success: true } c)
            {
                return c.Groups[1].Success ? c.Groups[1].Value : c.Groups[2].Value;
            }
        }

        return null;
    }

    private static string? Clean(string? s) =>
        DocumentMeta.NullIfBlank(s is null ? null : Spaces().Replace(WebUtility.HtmlDecode(s), " "));

    [GeneratedRegex(@"<meta[^>]+charset\s*=\s*[""']?([\w\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Charset();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Title();

    [GeneratedRegex(@"<html[^>]*\slang\s*=\s*[""']?([\w\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Lang();

    [GeneratedRegex(@"<meta\s[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"content\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex ContentAttr();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
