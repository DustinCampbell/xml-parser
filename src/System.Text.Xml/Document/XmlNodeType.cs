using System;

namespace System.Text.Xml;

/// <summary>
/// Identifies the semantic kind of an <see cref="XmlNode"/> instance.
/// </summary>
public enum XmlNodeType
{
    /// <summary>
    /// The node is an XML element.
    /// </summary>
    Element,

    /// <summary>
    /// The node is a text node.
    /// </summary>
    Text,

    /// <summary>
    /// The node is a CDATA section.
    /// </summary>
    CData,

    /// <summary>
    /// The node is an XML comment.
    /// </summary>
    Comment,

    /// <summary>
    /// The node is a processing instruction.
    /// </summary>
    ProcessingInstruction,

    /// <summary>
    /// The node is an XML declaration.
    /// </summary>
    Declaration,

    /// <summary>
    /// The node is an XML attribute.
    /// </summary>
    Attribute,
}