using System.Text;

namespace Nopds.Formats.Rtf;

/// <summary>
/// Collects decoded RTF text: byte runs (plain text and \'hh escapes) are decoded together with the
/// current code page, so double-byte encodings survive; \uN characters are appended directly.
/// </summary>
public sealed class RtfText
{
    private readonly List<byte> _bytes = [];
    private readonly StringBuilder _text = new();
    private Encoding _encoding;

    public RtfText(Encoding encoding) => _encoding = encoding;

    public Encoding Encoding
    {
        get => _encoding;
        set
        {
            if (!ReferenceEquals(value, _encoding))
            {
                FlushBytes();
                _encoding = value;
            }
        }
    }

    public bool IsEmpty => _bytes.Count == 0 && _text.Length == 0;

    public void AddBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            _bytes.Add(b is (byte)'\t' ? (byte)' ' : b);
        }
    }

    public void AddByte(byte b) => _bytes.Add(b);

    public void AddChar(char c)
    {
        FlushBytes();
        _text.Append(c);
    }

    public void AddString(string s)
    {
        FlushBytes();
        _text.Append(s);
    }

    /// <summary>Returns and clears the collected text.</summary>
    public string Take()
    {
        FlushBytes();
        var s = _text.ToString();
        _text.Clear();
        return s;
    }

    private void FlushBytes()
    {
        if (_bytes.Count > 0)
        {
            _text.Append(_encoding.GetString(_bytes.ToArray()));
            _bytes.Clear();
        }
    }

    /// <summary>Encoding for a code page, falling back to Windows-1252.</summary>
    public static Encoding CodePage(int? codePage) =>
        codePage is > 0 && TextDecoding.TryGetEncoding(codePage.Value.ToString()) is { } e ? e : TextDecoding.TryGetEncoding("1252")!;
}
