using Nopds.Formats;

namespace Nopds.Conversion;

/// <summary>An in-process converter from one or more source formats to a target format.</summary>
public interface IBookConverter
{
    IReadOnlyCollection<string> Sources { get; }

    string Target { get; }

    /// <param name="input">The source file.</param>
    /// <param name="meta">Catalog metadata of the book; used where the file itself declares nothing better.</param>
    /// <param name="output">Receives the converted file.</param>
    void Convert(byte[] input, BookMetadata meta, Stream output);
}
