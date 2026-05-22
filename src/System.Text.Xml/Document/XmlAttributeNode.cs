using System;

namespace System.Text.Xml;

/// <summary>
/// Represents an attribute declared on an <see cref="XmlElementNode"/>.
/// </summary>
public sealed class XmlAttributeNode : XmlNode
{
    private readonly XmlName _name;

    /// <summary>
    /// Initializes a new <see cref="XmlAttributeNode"/> instance.
    /// </summary>
    /// <param name="name">The qualified name of the attribute.</param>
    /// <param name="value">The attribute value.</param>
    public XmlAttributeNode(XmlName name, string value)
        : base(XmlNodeType.Attribute)
    {
        _name = name;
        XmlNameAccessor.GetParts(name, out var localName, out var prefix, out var namespaceUri);
        LocalName = localName;
        Prefix = prefix;
        NamespaceUri = namespaceUri;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal XmlAttributeNode(string localName, string? prefix, string? namespaceUri, string value)
        : this(XmlNameAccessor.Create(localName, prefix, namespaceUri), value)
    {
    }

    /// <summary>
    /// Gets the local attribute name.
    /// </summary>
    public string LocalName { get; }

    /// <summary>
    /// Gets the namespace prefix associated with the attribute.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets the namespace URI associated with the attribute.
    /// </summary>
    public string NamespaceUri { get; }

    /// <summary>
    /// Gets the qualified attribute name.
    /// </summary>
    public XmlName Name => _name;

    /// <summary>
    /// Gets the attribute value.
    /// </summary>
    public string Value { get; private set; }

    /// <summary>
    /// Writes the current attribute using the specified UTF-8 XML writer.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    public override void WriteTo(Utf8XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteTo(new Utf8XmlNodeWriter(writer));
    }

    internal override void WriteTo(XmlNodeWriter writer)
    {
        writer.WriteAttribute(Prefix, LocalName, NamespaceUri, Value);
    }

    internal void SetValue(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}