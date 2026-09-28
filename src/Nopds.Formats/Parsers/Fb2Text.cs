using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Nopds.Formats.Parsers;

/// <summary>Helpers for reading real-world (often slightly broken) FictionBook files.</summary>
public static partial class Fb2Text
{
    /// <summary>Decodes FB2 bytes honoring BOM and the XML declaration encoding (windows-1251, koi8-r, ...).</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 256));
        var m = EncodingDecl().Match(head);
        var encoding = Encoding.UTF8;
        if (m.Success)
        {
            try
            {
                encoding = Encoding.GetEncoding(m.Groups[1].Value.Trim());
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }

        return encoding.GetString(bytes);
    }

    /// <summary>Replaces HTML named entities with numeric ones, escapes stray ampersands, strips invalid chars.</summary>
    public static string Sanitize(string xml)
    {
        xml = Ampersand().Replace(xml, m =>
        {
            if (!m.Groups[1].Success)
            {
                return "&amp;";
            }

            var decoded = WebUtility.HtmlDecode(m.Value);
            if (decoded == m.Value)
            {
                return "&amp;" + m.Groups[1].Value;
            }

            var sb = new StringBuilder();
            foreach (var rune in decoded.EnumerateRunes())
            {
                sb.Append("&#").Append(rune.Value).Append(';');
            }

            return sb.ToString();
        });

        // Drop the encoding declaration: the string is already decoded.
        xml = EncodingDecl().Replace(xml, string.Empty, 1);
        return InvalidXmlChars().Replace(xml, string.Empty);
    }

    public static XmlReaderSettings ReaderSettings => new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        CheckCharacters = false,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>Loads the whole document leniently (used by the converter and the reader).</summary>
    public static XDocument LoadDocument(byte[] bytes)
    {
        var text = Sanitize(Decode(bytes));
        using var reader = XmlReader.Create(new StringReader(text), ReaderSettings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    [GeneratedRegex(@"encoding\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex EncodingDecl();

    [GeneratedRegex(@"&(?!(?:amp|lt|gt|quot|apos|#\d+|#[xX][0-9a-fA-F]+);)(?:([a-zA-Z][a-zA-Z0-9]{1,31});)?")]
    private static partial Regex Ampersand();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex InvalidXmlChars();
}
