namespace System.Text.Xml;

/// <summary>
/// Represents a qualified XML name.
/// </summary>
public readonly struct XmlName : IEquatable<XmlName>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="XmlName"/> struct.
    /// </summary>
    public XmlName(string localName, string? prefix = null, string? namespaceUri = null)
    {
        if (string.IsNullOrWhiteSpace(localName))
        {
            throw new ArgumentException("The local name must not be null or empty.", nameof(localName));
        }

        LocalName = localName;
        Prefix = prefix ?? string.Empty;
        NamespaceUri = namespaceUri ?? string.Empty;
    }

    /// <summary>
    /// Gets the local name.
    /// </summary>
    public string LocalName { get; }

    /// <summary>
    /// Gets the prefix.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets the namespace URI.
    /// </summary>
    public string NamespaceUri { get; }

    /// <summary>
    /// Parses a qualified XML name.
    /// </summary>
    public static XmlName Parse(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            throw new ArgumentException("The qualified name must not be null or empty.", nameof(qualifiedName));
        }

        int separator = qualifiedName.IndexOf(':');
        return separator < 0
            ? new XmlName(qualifiedName)
            : new XmlName(qualifiedName[(separator + 1)..], qualifiedName[..separator]);
    }

    /// <summary>
    /// Returns the qualified name.
    /// </summary>
    public override string ToString() => string.IsNullOrEmpty(Prefix) ? LocalName : $"{Prefix}:{LocalName}";

    /// <inheritdoc/>
    public bool Equals(XmlName other) =>
        string.Equals(LocalName, other.LocalName, StringComparison.Ordinal) &&
        string.Equals(Prefix, other.Prefix, StringComparison.Ordinal) &&
        string.Equals(NamespaceUri, other.NamespaceUri, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XmlName other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(LocalName, Prefix, NamespaceUri);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    public static bool operator ==(XmlName left, XmlName right) => left.Equals(right);

    /// <summary>
    /// Compares two values for inequality.
    /// </summary>
    public static bool operator !=(XmlName left, XmlName right) => !left.Equals(right);
}