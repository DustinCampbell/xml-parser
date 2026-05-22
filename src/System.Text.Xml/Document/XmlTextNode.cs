using System;

namespace System.Text.Xml;

/// <summary>
/// Represents text content within an XML element.
/// </summary>
public sealed class XmlTextNode : XmlNode
{
    /// <summary>
    /// Initializes a new <see cref="XmlTextNode"/> instance.
    /// </summary>
    /// <param name="value">The text value.</param>
    public XmlTextNode(string value)
        : base(XmlNodeType.Text)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the text value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override void WriteTo(Utf8XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteTo(new Utf8XmlNodeWriter(writer));
    }

    internal override void WriteTo(XmlNodeWriter writer) => writer.WriteString(Value);
}