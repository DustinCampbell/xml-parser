using System.Collections.Generic;

namespace System.Text.Xml;

/// <summary>
/// Provides conversion methods between the read-only struct DOM (<see cref="XmlElement"/>)
/// and the mutable class DOM (<see cref="XmlElementNode"/>).
/// </summary>
/// <remarks>
/// <para>
/// This bridge enables a workflow where XML is parsed cheaply using the read-only DOM,
/// converted to a mutable DOM only when modifications are needed, and then serialized back.
/// Trivia (whitespace and comments) is preserved through the conversion when the document
/// was parsed with <see cref="XmlDocumentOptions.PreserveTrivia"/>.
/// </para>
/// </remarks>
public static class XmlDomConverter
{
    /// <summary>
    /// Converts a read-only <see cref="XmlElement"/> to a mutable <see cref="XmlElementNode"/>.
    /// </summary>
    /// <param name="element">The read-only element to convert.</param>
    /// <returns>A new mutable element tree with the same structure and trivia.</returns>
    public static XmlElementNode ToMutable(this XmlElement element)
    {
        var doc = element.Document;
        var elementNode = new XmlElementNode(
            element.LocalName,
            NullIfEmpty(element.Prefix),
            NullIfEmpty(element.NamespaceUri));

        // Convert attributes
        var attrs = element.EnumerateAttributes();
        while (attrs.MoveNext())
        {
            var attr = attrs.Current;
            elementNode.SetAttribute(new XmlAttributeNode(
                attr.LocalName,
                NullIfEmpty(attr.Prefix),
                NullIfEmpty(attr.NamespaceUri),
                attr.Value));
        }

        // Convert children
        var children = element.EnumerateChildren();
        while (children.MoveNext())
        {
            var child = children.Current;
            XmlNode? childNode = ConvertChild(child, doc);
            if (childNode is not null)
            {
                elementNode.AddChild(childNode);
            }
        }

        // Copy trivia if available
        CopyTrivia(element, elementNode);

        return elementNode;
    }

    /// <summary>
    /// Converts a mutable <see cref="XmlElementNode"/> back to a read-only <see cref="XmlDocument"/>.
    /// </summary>
    /// <param name="element">The mutable element to serialize and re-parse.</param>
    /// <param name="options">Optional document options for the resulting document.</param>
    /// <returns>A new read-only document.</returns>
    /// <remarks>
    /// This serializes the mutable DOM to XML text and re-parses it into the efficient
    /// read-only format. This is the recommended path when you're done modifying and want
    /// to return to the high-performance read-only representation.
    /// </remarks>
    public static XmlDocument ToReadOnly(this XmlElementNode element, XmlDocumentOptions? options = null)
    {
        string xml = WrapInDocument(element);
        return options is not null
            ? XmlDocument.Parse(xml, options)
            : XmlDocument.Parse(xml);
    }

    private static XmlNode? ConvertChild(XmlNodeValue child, XmlDocument doc)
    {
        switch (child.NodeType)
        {
            case XmlNodeType.Element:
                return child.AsElement().ToMutable();

            case XmlNodeType.Text:
                return new XmlTextNode(child.Value);

            case XmlNodeType.CData:
                return new XmlCDataNode(child.Value);

            case XmlNodeType.Comment:
                return new XmlCommentNode(child.Value);

            case XmlNodeType.ProcessingInstruction:
                return new XmlProcessingInstructionNode(child.Target, child.Value);

            default:
                return null;
        }
    }

    private static void CopyTrivia(XmlElement element, XmlNode targetNode)
    {
        var leading = element.GetLeadingTrivia();
        if (!leading.IsEmpty)
        {
            for (int i = 0; i < leading.Count; i++)
            {
                var trivia = leading[i];
                targetNode.AddLeadingTrivia(new XmlNodeTrivia(trivia.Kind, trivia.Text));
            }
        }

        var trailing = element.GetTrailingTrivia();
        if (!trailing.IsEmpty)
        {
            for (int i = 0; i < trailing.Count; i++)
            {
                var trivia = trailing[i];
                targetNode.AddTrailingTrivia(new XmlNodeTrivia(trivia.Kind, trivia.Text));
            }
        }
    }

    private static string WrapInDocument(XmlElementNode element)
    {
        return element.ToString();
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
