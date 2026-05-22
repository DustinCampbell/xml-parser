using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EscapeAttributeValue(string value) => EscapeText(value)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal)
        .Replace("\r", "&#xD;", StringComparison.Ordinal)
        .Replace("\n", "&#xA;", StringComparison.Ordinal)
        .Replace("\t", "&#x9;", StringComparison.Ordinal);
}

internal sealed class Utf8XmlNodeWriter : XmlNodeWriter
{
    private readonly object _writer;
    private readonly Type _writerType;

    public Utf8XmlNodeWriter(Utf8XmlWriter writer)
    {
        _writer = writer!;
        _writerType = _writer.GetType();
    }

    public override void WriteDeclaration(string version, string? encoding, string? standalone)
    {
        if (TryInvoke("WriteDeclaration", version, encoding, standalone))
        {
            return;
        }

        if (TryInvoke("WriteStartDocument", ParseStandalone(standalone)))
        {
            return;
        }

        if (TryInvoke("WriteStartDocument"))
        {
            return;
        }

        if (TryInvoke("WriteRaw", XmlDeclarationNode.FormatDeclaration(version, encoding, standalone)))
        {
            return;
        }

        throw CreateMissingMethodException("WriteDeclaration");
    }

    public override void WriteStartElement(string prefix, string localName, string namespaceUri)
    {
        if (TryInvoke("WriteStartElement", prefix, localName, namespaceUri) ||
            TryInvoke("WriteStartElement", localName, namespaceUri) ||
            TryInvoke("WriteStartElement", localName))
        {
            return;
        }

        throw CreateMissingMethodException("WriteStartElement");
    }

    public override void WriteEndElement()
    {
        if (!TryInvoke("WriteEndElement"))
        {
            throw CreateMissingMethodException("WriteEndElement");
        }
    }

    public override void WriteAttribute(string prefix, string localName, string namespaceUri, string value)
    {
        if (TryInvoke("WriteAttributeString", prefix, localName, namespaceUri, value) ||
            TryInvoke("WriteAttributeString", localName, value) ||
            TryInvoke("WriteAttribute", prefix, localName, namespaceUri, value))
        {
            return;
        }

        throw CreateMissingMethodException("WriteAttributeString");
    }

    public override void WriteString(string value)
    {
        if (!TryInvoke("WriteString", value))
        {
            throw CreateMissingMethodException("WriteString");
        }
    }

    public override void WriteCData(string value)
    {
        if (!TryInvoke("WriteCData", value))
        {
            throw CreateMissingMethodException("WriteCData");
        }
    }

    public override void WriteComment(string value)
    {
        if (!TryInvoke("WriteComment", value))
        {
            throw CreateMissingMethodException("WriteComment");
        }
    }

    public override void WriteProcessingInstruction(string target, string? data)
    {
        if (!TryInvoke("WriteProcessingInstruction", target, data))
        {
            throw CreateMissingMethodException("WriteProcessingInstruction");
        }
    }

    private bool TryInvoke(string methodName, params object?[] args)
    {
        var method = _writerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                var parameters = m.GetParameters();
                if (parameters.Length != args.Length)
                {
                    return false;
                }

                for (var i = 0; i < parameters.Length; i++)
                {
                    var argument = args[i];
                    if (argument is null)
                    {
                        if (parameters[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(parameters[i].ParameterType) is null)
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!parameters[i].ParameterType.IsAssignableFrom(argument.GetType()))
                    {
                        return false;
                    }
                }

                return true;
            });

        if (method is null)
        {
            return false;
        }

        method.Invoke(_writer, args);
        return true;
    }

    private Exception CreateMissingMethodException(string methodName) =>
        new NotSupportedException($"The configured {nameof(Utf8XmlWriter)} implementation does not expose a supported '{methodName}' overload.");

    private static bool? ParseStandalone(string? standalone)
    {
        if (standalone is null)
        {
            return null;
        }

        if (string.Equals(standalone, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(standalone, "no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }
}

internal static class XmlNameAccessor
{
    private static readonly Type s_xmlNameType = typeof(XmlName);
    private static readonly PropertyInfo? s_localNameProperty = s_xmlNameType.GetProperty("LocalName", BindingFlags.Instance | BindingFlags.Public);
    private static readonly PropertyInfo? s_prefixProperty = s_xmlNameType.GetProperty("Prefix", BindingFlags.Instance | BindingFlags.Public);
    private static readonly PropertyInfo? s_namespaceUriProperty = s_xmlNameType.GetProperty("NamespaceUri", BindingFlags.Instance | BindingFlags.Public)
        ?? s_xmlNameType.GetProperty("NamespaceURI", BindingFlags.Instance | BindingFlags.Public);
    private static readonly ConstructorInfo[] s_constructors = s_xmlNameType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);

    public static XmlName Create(string localName, string? prefix = null, string? namespaceUri = null)
    {
        prefix ??= string.Empty;
        namespaceUri ??= string.Empty;

        foreach (var constructor in s_constructors)
        {
            var parameters = constructor.GetParameters();
            if (!parameters.All(static p => p.ParameterType == typeof(string)))
            {
                continue;
            }

            switch (parameters.Length)
            {
                case 1 when TryInvoke(constructor, [localName], out var single):
                    return (XmlName)single!;
                case 2 when TryInvoke(constructor, [localName, namespaceUri], out var pair):
                    return (XmlName)pair!;
                case 2 when TryInvoke(constructor, [prefix, localName], out var qualifiedPair):
                    return (XmlName)qualifiedPair!;
                case 3 when TryInvoke(constructor, [localName, prefix, namespaceUri], out var triple):
                    return (XmlName)triple!;
                case 3 when TryInvoke(constructor, [prefix, localName, namespaceUri], out var alternateTriple):
                    return (XmlName)alternateTriple!;
            }
        }

        var factories = s_xmlNameType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, "Create", StringComparison.Ordinal) &&
                        m.ReturnType == s_xmlNameType &&
                        m.GetParameters().All(static p => p.ParameterType == typeof(string)));

        foreach (var factory in factories)
        {
            var parameters = factory.GetParameters();
            if (parameters.Length == 3)
            {
                return (XmlName)factory.Invoke(null, [prefix, localName, namespaceUri])!;
            }

            if (parameters.Length == 2)
            {
                return (XmlName)factory.Invoke(null, [localName, namespaceUri])!;
            }

            if (parameters.Length == 1)
            {
                return (XmlName)factory.Invoke(null, [localName])!;
            }
        }

        return default;
    }

    public static void GetParts(XmlName name, out string localName, out string prefix, out string namespaceUri)
    {
        localName = GetPart(name, s_localNameProperty);
        prefix = GetPart(name, s_prefixProperty);
        namespaceUri = GetPart(name, s_namespaceUriProperty);

        if (string.IsNullOrEmpty(localName))
        {
            var displayName = name.ToString() ?? string.Empty;
            var separatorIndex = displayName.IndexOf(':');
            if (separatorIndex >= 0)
            {
                prefix = displayName[..separatorIndex];
                localName = displayName[(separatorIndex + 1)..];
            }
            else
            {
                localName = displayName;
            }
        }
    }

    private static string GetPart(XmlName name, PropertyInfo? property)
    {
        if (property is null)
        {
            return string.Empty;
        }

        return property.GetValue(name) as string ?? string.Empty;
    }

    private static bool TryInvoke(ConstructorInfo constructor, object?[] args, out object? value)
    {
        try
        {
            value = constructor.Invoke(args);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}