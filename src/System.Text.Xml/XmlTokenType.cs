namespace System.Text.Xml;

/// <summary>
/// Defines the kinds of tokens that can be encountered while reading XML.
/// </summary>
public enum XmlTokenType
{
    /// <summary>No token has been read.</summary>
    None,

    /// <summary>An XML declaration token.</summary>
    XmlDeclaration,

    /// <summary>A start element token.</summary>
    StartElement,

    /// <summary>An end element token.</summary>
    EndElement,

    /// <summary>An attribute token.</summary>
    Attribute,

    /// <summary>A text token.</summary>
    Text,

    /// <summary>A CDATA token.</summary>
    CData,

    /// <summary>A comment token.</summary>
    Comment,

    /// <summary>A processing instruction token.</summary>
    ProcessingInstruction,

    /// <summary>A whitespace token.</summary>
    Whitespace,

    /// <summary>An entity reference token.</summary>
    EntityReference,
}