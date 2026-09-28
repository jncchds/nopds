namespace Nopds.Formats;

public interface IBookParser
{
    /// <summary>Lowercase extensions (without dot) this parser handles.</summary>
    IReadOnlyCollection<string> Formats { get; }

    /// <summary>Reads metadata from a seekable stream. Throws on unreadable files.</summary>
    BookMetadata Parse(Stream stream, string fileName, bool includeCover);
}
