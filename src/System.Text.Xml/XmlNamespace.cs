namespace System.Text.Xml;

/// <summary>
/// Represents an XML namespace declaration.
/// </summary>
public readonly record struct XmlNamespace
{
    /// <summary>
    /// Initializes a new instance of the <see cref="XmlNamespace"/> struct.
    /// </summary>
    public XmlNamespace(string prefix, string uri)
    {
        Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
    }

    /// <summary>
    /// Gets the namespace prefix.
    /// </summary>
    public string Prefix { get; init; }

    /// <summary>
    /// Gets the namespace URI.
    /// </summary>
    public string Uri { get; init; }

    /// <summary>
    /// Gets the predefined <c>xml</c> namespace.
    /// </summary>
    public static XmlNamespace Xml { get; } = new("xml", "http://www.w3.org/XML/1998/namespace");

    /// <summary>
    /// Gets the predefined <c>xmlns</c> namespace.
    /// </summary>
    public static XmlNamespace Xmlns { get; } = new("xmlns", "http://www.w3.org/2000/xmlns/");
}