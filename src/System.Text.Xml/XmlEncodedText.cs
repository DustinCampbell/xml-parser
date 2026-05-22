using System.Text;

namespace System.Text.Xml;

/// <summary>
/// Represents XML text that has been encoded as UTF-8 and escaped for efficient writing.
/// </summary>
public readonly struct XmlEncodedText : IEquatable<XmlEncodedText>
{
    private readonly byte[]? _utf8Value;
    private readonly string? _value;

    private XmlEncodedText(byte[] utf8Value, string value)
    {
        _utf8Value = utf8Value;
        _value = value;
    }

    /// <summary>
    /// Encodes text as escaped UTF-8 XML text.
    /// </summary>
    public static XmlEncodedText Encode(string value)
    {
        ThrowHelper.ThrowIfNull(value);
        return new XmlEncodedText(Encoding.UTF8.GetBytes(Escape(value)), value);
    }

    /// <summary>
    /// Gets the encoded UTF-8 bytes.
    /// </summary>
    public ReadOnlySpan<byte> EncodedUtf8Bytes => _utf8Value;

    /// <inheritdoc/>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(XmlEncodedText other) => EncodedUtf8Bytes.SequenceEqual(other.EncodedUtf8Bytes);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XmlEncodedText other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (_utf8Value is null)
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;
            for (int i = 0; i < _utf8Value.Length; i++)
            {
                hash = (hash * 31) + _utf8Value[i];
            }

            return hash;
        }
    }

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    public static bool operator ==(XmlEncodedText left, XmlEncodedText right) => left.Equals(right);

    /// <summary>
    /// Compares two values for inequality.
    /// </summary>
    public static bool operator !=(XmlEncodedText left, XmlEncodedText right) => !left.Equals(right);

    private static string Escape(string value)
        => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}