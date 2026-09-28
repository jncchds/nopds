namespace Nopds.Formats.Parsers;

/// <summary>Formats without embedded metadata support: title comes from the file name.</summary>
public sealed class GenericParser : IBookParser
{
    public IReadOnlyCollection<string> Formats { get; } = [];

    public BookMetadata Parse(Stream stream, string fileName, bool includeCover) =>
        new() { Title = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim() };
}
