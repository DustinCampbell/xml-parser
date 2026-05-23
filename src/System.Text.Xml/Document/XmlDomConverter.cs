using System.Collections.Generic;

namespace System.Text.Xml;

/// <summary>
/// Provides internal conversion helpers between the read-only struct DOM and the mutable class DOM.
/// </summary>
internal static class XmlDomConverter
{
    internal static XmlNode? ConvertChild(XmlNodeValue child)
    {
        switch (child.NodeType)
        {
            case XmlNodeType.Element:
                return XmlElementNode.Create(child.AsElement());

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

    internal static void CopyTrivia(XmlElement element, XmlNode targetNode)
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

    internal static string? NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
