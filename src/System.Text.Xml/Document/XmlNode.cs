using System.IO;
using System.Globalization;
using System.Text;

namespace System.Text.Xml;

/// <summary>
/// Represents a node in an XML document tree.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XmlNode"/> is the common base type for all DOM-style node representations exposed by
/// <see cref="XmlDocument"/>.
/// </para>
/// <para>
/// Derived types model concrete XML constructs such as elements, attributes, text nodes, comments,
/// CDATA sections, and processing instructions.
/// </para>
/// </remarks>
public abstract class XmlNode
{
    /// <summary>
    /// Initializes a new <see cref="XmlNode"/> instance.
    /// </summary>
    /// <param name="nodeType">The node type represented by the instance.</param>
    protected XmlNode(XmlNodeType nodeType)
    {
        NodeType = nodeType;
    }

    /// <summary>
    /// Gets the kind of node represented by the current instance.
    /// </summary>
    public XmlNodeType NodeType { get; }

    /// <summary>
    /// Gets the containing element for the current node.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> when the node is not attached to an element.
    /// </remarks>
    public XmlElementNode? Parent { get; private set; }

    internal void SetParent(XmlElementNode? parent) => Parent = parent;

    /// <summary>
    /// Writes the current node to the specified UTF-8 XML writer.
    /// </summary>
    /// <param name="writer">The writer to receive the serialized node.</param>
    public abstract void WriteTo(Utf8XmlWriter writer);

    internal abstract void WriteTo(XmlNodeWriter writer);

    /// <summary>
    /// Returns the XML text for the current node.
    /// </summary>
    /// <returns>A string containing the serialized XML representation of the node.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        using var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture);
        var writer = new TextXmlNodeWriter(stringWriter);
        WriteTo(writer);
        stringWriter.Flush();
        return builder.ToString();
    }
}

internal abstract class XmlNodeWriter
{
    public abstract void WriteDeclaration(string version, string? encoding, string? standalone);
    public abstract void WriteStartElement(string prefix, string localName, string namespaceUri);
    public abstract void WriteEndElement();
    public abstract void WriteAttribute(string prefix, string localName, string namespaceUri, string value);
    public abstract void WriteString(string value);
    public abstract void WriteCData(string value);
    public abstract void WriteComment(string value);
    public abstract void WriteProcessingInstruction(string target, string? data);
}

internal sealed class TextXmlNodeWriter : XmlNodeWriter
{
    private readonly TextWriter _writer;
    private readonly Stack<(string Prefix, string LocalName)> _elementNames = new();
    private bool _startTagOpen;

    public TextXmlNodeWriter(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public override void WriteDeclaration(string version, string? encoding, string? standalone)
    {
        EnsureStartTagClosed();
        _writer.Write(XmlDeclarationNode.FormatDeclaration(version, encoding, standalone));
    }

    public override void WriteStartElement(string prefix, string localName, string namespaceUri)
    {
        EnsureStartTagClosed();
        _writer.Write('<');
        WriteQualifiedName(prefix, localName);
        _elementNames.Push((prefix, localName));
        _startTagOpen = true;
    }

    public override void WriteEndElement()
    {
        var elementName = _elementNames.Pop();
        if (_startTagOpen)
        {
            _writer.Write(" />");
            _startTagOpen = false;
            return;
        }

        _writer.Write("</");
        WriteQualifiedName(elementName.Prefix, elementName.LocalName);
        _writer.Write('>');
    }

    public override void WriteAttribute(string prefix, string localName, string namespaceUri, string value)
    {
        if (!_startTagOpen)
        {
            throw new InvalidOperationException("Attributes can only be written immediately after a start element.");
        }

        _writer.Write(' ');
        WriteQualifiedName(prefix, localName);
        _writer.Write("=\"");
        _writer.Write(EscapeAttributeValue(value));
        _writer.Write('"');
    }

    public override void WriteString(string value)
    {
        EnsureStartTagClosed();
        _writer.Write(EscapeText(value));
    }

    public override void WriteCData(string value)
    {
        EnsureStartTagClosed();
        _writer.Write("<![CDATA[");
        _writer.Write(value);
        _writer.Write("]]>");
    }

    public override void WriteComment(string value)
    {
        EnsureStartTagClosed();
        _writer.Write("<!--");
        _writer.Write(value);
        _writer.Write("-->");
    }

    public override void WriteProcessingInstruction(string target, string? data)
    {
        EnsureStartTagClosed();
        _writer.Write("<?");
        _writer.Write(target);
        if (!string.IsNullOrEmpty(data))
        {
            _writer.Write(' ');
            _writer.Write(data);
        }

        _writer.Write("?>");
    }

    private void EnsureStartTagClosed()
    {
        if (_startTagOpen)
        {
            _writer.Write('>');
            _startTagOpen = false;
        }
    }

    private void WriteQualifiedName(string prefix, string localName)
    {
        if (!string.IsNullOrEmpty(prefix))
        {
            _writer.Write(prefix);
            _writer.Write(':');
        }

        _writer.Write(localName);
    }

    private static string EscapeText(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    private static string EscapeAttributeValue(string value) => EscapeText(value)
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;")
        .Replace("\r", "&#xD;")
        .Replace("\n", "&#xA;")
        .Replace("\t", "&#x9;");
}

internal sealed class Utf8XmlNodeWriter : XmlNodeWriter
{
    private readonly Utf8XmlWriter _writer;

    public Utf8XmlNodeWriter(Utf8XmlWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public override void WriteDeclaration(string version, string? encoding, string? standalone)
    {
        _writer.WriteStartDocument();
    }

    public override void WriteStartElement(string prefix, string localName, string namespaceUri)
    {
        _writer.WriteStartElement(localName, namespaceUri, prefix);
    }

    public override void WriteEndElement()
    {
        _writer.WriteEndElement();
    }

    public override void WriteAttribute(string prefix, string localName, string namespaceUri, string value)
    {
        _writer.WriteAttributeString(localName, value, namespaceUri, prefix);
    }

    public override void WriteString(string value)
    {
        _writer.WriteString(value);
    }

    public override void WriteCData(string value)
    {
        _writer.WriteCData(value);
    }

    public override void WriteComment(string value)
    {
        _writer.WriteComment(value);
    }

    public override void WriteProcessingInstruction(string target, string? data)
    {
        _writer.WriteProcessingInstruction(target, data);
    }
}

internal static class XmlNameAccessor
{
    public static XmlName Create(string localName, string? prefix = null, string? namespaceUri = null)
    {
        return new XmlName(localName, prefix ?? string.Empty, namespaceUri ?? string.Empty);
    }

    public static void GetParts(XmlName name, out string localName, out string prefix, out string namespaceUri)
    {
        localName = name.LocalName;
        prefix = name.Prefix;
        namespaceUri = name.NamespaceUri;
    }
}