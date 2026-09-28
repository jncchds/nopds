namespace Nopds.Formats;

public static class MediaTypes
{
    public const string Fb2 = "application/x-fictionbook+xml";
    public const string Fb2Zip = "application/x-zip-compressed-fb2";
    public const string Epub = "application/epub+zip";
    public const string Mobi = "application/x-mobipocket-ebook";
    public const string Azw3 = "application/vnd.amazon.ebook";
    public const string Pdf = "application/pdf";
    public const string Djvu = "image/vnd.djvu";
    public const string Zip = "application/zip";
    public const string Cbz = "application/vnd.comicbook+zip";
    public const string Octet = "application/octet-stream";

    public static string ForFormat(string format) => format.ToLowerInvariant() switch
    {
        "fb2" => Fb2,
        "epub" => Epub,
        "kepub" => Epub,
        "mobi" => Mobi,
        "azw" or "azw3" => Azw3,
        "pdf" => Pdf,
        "djvu" or "djv" => Djvu,
        "txt" => "text/plain",
        "rtf" => "application/rtf",
        "doc" => "application/msword",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "cbz" => Cbz,
        "cbr" => "application/vnd.comicbook-rar",
        "zip" => Zip,
        _ => Octet,
    };

    public static string ForImage(string nameOrType)
    {
        var s = nameOrType.ToLowerInvariant();
        if (s.StartsWith("image/", StringComparison.Ordinal))
        {
            return s;
        }

        return Path.GetExtension(s) switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "image/jpeg",
        };
    }
}
