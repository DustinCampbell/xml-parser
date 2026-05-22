using System;

namespace System.Text.Xml;

/// <summary>
/// Represents a CDATA section within an XML element.
/// </summary>
public sealed class XmlCDataNode : XmlNode
{
    /// <summary>
    /// Initializes a new <see cref="XmlCDataNode"/> instance.
    /// </summary>
    /// <param name="value">The CDATA content.</param>
    public XmlCDataNode(string value)
        : base(XmlNodeType.CData)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the CDATA content.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override void WriteTo(Utf8XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteTo(new Utf8XmlNodeWriter(writer));
    }

    internal override void WriteTo(XmlNodeWriter writer) => writer.WriteCData(Value);
}