using System;

namespace System.Text.Xml;

/// <summary>
/// Represents an XML comment.
/// </summary>
public sealed class XmlCommentNode : XmlNode
{
    /// <summary>
    /// Initializes a new <see cref="XmlCommentNode"/> instance.
    /// </summary>
    /// <param name="value">The comment text.</param>
    public XmlCommentNode(string value)
        : base(XmlNodeType.Comment)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the comment text.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override void WriteTo(Utf8XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteTo(new Utf8XmlNodeWriter(writer));
    }

    internal override void WriteTo(XmlNodeWriter writer) => writer.WriteComment(Value);
}