using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Text.Xml;

/// <summary>
/// Represents a parsed XML document and its DOM-style node graph.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XmlDocument"/> provides a read/write in-memory representation for XML payloads.
/// </para>
/// <para>
/// Instances can be created directly from a root element or by parsing UTF-8 XML text from strings,
/// spans, files, and streams.
/// </para>
/// </remarks>
public sealed class XmlDocument : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="XmlDocument"/> instance.
    /// </summary>
    /// <param name="root">The document root element.</param>
    /// <param name="declaration">The optional XML declaration.</param>
    public XmlDocument(XmlElementNode root, XmlDeclarationNode? declaration = null)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Declaration = declaration;
    }

    /// <summary>
    /// Gets the XML declaration associated with the document, if present.
    /// </summary>
    public XmlDeclarationNode? Declaration { get; }

    /// <summary>
    /// Gets the root element of the document.
    /// </summary>
    public XmlElementNode Root { get; }

    /// <summary>
    /// Parses an XML document from a UTF-16 string.
    /// </summary>
    /// <param name="xml">The XML payload to parse.</param>
    /// <returns>The parsed <see cref="XmlDocument"/>.</returns>
    public static XmlDocument Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return Parse(Encoding.UTF8.GetBytes(xml));
    }

    /// <summary>
    /// Parses an XML document from a UTF-8 byte span.
    /// </summary>
    /// <param name="utf8Xml">The UTF-8 XML payload to parse.</param>
    /// <returns>The parsed <see cref="XmlDocument"/>.</returns>
    public static XmlDocument Parse(ReadOnlySpan<byte> utf8Xml)
    {
        return XmlDomParser.Parse(utf8Xml);
    }

    /// <summary>
    /// Parses an XML document asynchronously from the specified stream.
    /// </summary>
    /// <param name="stream">The stream containing UTF-8 XML data.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous parse operation.</returns>
    public static async Task<XmlDocument> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffer = await ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);
        return Parse(buffer);
    }

    /// <summary>
    /// Loads an XML document from the specified file path.
    /// </summary>
    /// <param name="filePath">The path to the XML file.</param>
    /// <returns>The parsed <see cref="XmlDocument"/>.</returns>
    public static XmlDocument Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        using var stream = File.OpenRead(filePath);
        return Load(stream);
    }

    /// <summary>
    /// Loads an XML document from the specified stream.
    /// </summary>
    /// <param name="stream">The stream containing XML data.</param>
    /// <returns>The parsed <see cref="XmlDocument"/>.</returns>
    public static XmlDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Parse(ReadAllBytes(stream));
    }

    /// <summary>
    /// Loads an XML document asynchronously from the specified stream.
    /// </summary>
    /// <param name="stream">The stream containing XML data.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous load operation.</returns>
    public static Task<XmlDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return ParseAsync(stream, cancellationToken);
    }

    /// <summary>
    /// Writes the document to the specified UTF-8 XML writer.
    /// </summary>
    /// <param name="writer">The writer to receive the document.</param>
    public void WriteTo(Utf8XmlWriter writer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(writer);

        var nodeWriter = new Utf8XmlNodeWriter(writer);
        Declaration?.WriteTo(nodeWriter);
        Root.WriteTo(nodeWriter);
    }

    /// <summary>
    /// Saves the document to the specified stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    public void Save(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        using var textWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        var nodeWriter = new TextXmlNodeWriter(textWriter);
        Declaration?.WriteTo(nodeWriter);
        Root.WriteTo(nodeWriter);
        textWriter.Flush();
    }

    /// <summary>
    /// Saves the document to the specified file path.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    public void Save(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using var stream = File.Create(filePath);
        Save(stream);
    }

    /// <summary>
    /// Saves the document asynchronously to the specified stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    public async Task SaveAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        Save(buffer);
        buffer.Position = 0;
        await buffer.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases resources held by the current document instance.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }

    /// <summary>
    /// Returns the serialized XML for the document.
    /// </summary>
    /// <returns>A string containing the document XML.</returns>
    public override string ToString()
    {
        using var stream = new MemoryStream();
        Save(stream);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var segment))
        {
            return segment.Array is null
                ? memoryStream.ToArray()
                : segment.AsSpan(0, checked((int)memoryStream.Length)).ToArray();
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var segment))
        {
            return segment.Array is null
                ? memoryStream.ToArray()
                : segment.AsSpan(0, checked((int)memoryStream.Length)).ToArray();
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static class XmlDomParser
    {
        public static XmlDocument Parse(ReadOnlySpan<byte> utf8Xml)
        {
            var reader = new Utf8XmlReader(
                utf8Xml,
                new XmlReaderOptions
                {
                    CommentHandling = XmlCommentHandling.Allow,
                    IgnoreWhitespace = true,
                });

            var elementStack = new Stack<XmlElementNode>(16);
            XmlDeclarationNode? declaration = null;
            XmlElementNode? root = null;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case XmlTokenType.XmlDeclaration:
                        declaration = ParseDeclaration(reader.GetString() ?? string.Empty);
                        break;

                    case XmlTokenType.StartElement:
                    {
                        var localName = reader.GetLocalName();
                        var prefix = reader.GetPrefix();
                        var namespaceUri = reader.GetNamespaceUri();
                        var isEmptyElement = reader.IsEmptyElement;
                        var attributes = ReadAttributes(ref reader);
                        var element = new XmlElementNode(localName, prefix, namespaceUri, attributes);

                        if (elementStack.Count > 0)
                        {
                            elementStack.Peek().AddChild(element);
                        }
                        else
                        {
                            root = element;
                        }

                        if (!isEmptyElement)
                        {
                            elementStack.Push(element);
                        }

                        break;
                    }

                    case XmlTokenType.EndElement:
                        if (elementStack.Count > 0 && IsMatchingElement(reader, elementStack.Peek()))
                        {
                            elementStack.Pop();
                        }
                        break;

                    case XmlTokenType.Text:
                        AddChild(elementStack, new XmlTextNode(reader.GetString() ?? string.Empty));
                        break;

                    case XmlTokenType.CData:
                        AddChild(elementStack, new XmlCDataNode(reader.GetString() ?? string.Empty));
                        break;

                    case XmlTokenType.Comment:
                        AddChild(elementStack, new XmlCommentNode(reader.GetString() ?? string.Empty));
                        break;

                    case XmlTokenType.ProcessingInstruction:
                        AddChild(elementStack, new XmlProcessingInstructionNode(reader.GetLocalName(), reader.GetString()));
                        break;
                }
            }

            if (root is null)
            {
                throw new FormatException("The XML payload did not contain a document element.");
            }

            return new XmlDocument(root, declaration);
        }

        private static IEnumerable<XmlAttributeNode> ReadAttributes(ref Utf8XmlReader reader)
        {
            List<XmlAttributeNode>? attributes = null;

            while (reader.MoveToNextAttribute())
            {
                (attributes ??= new List<XmlAttributeNode>()).Add(
                    new XmlAttributeNode(
                        reader.GetLocalName(),
                        reader.GetPrefix(),
                        reader.GetNamespaceUri(),
                        reader.GetString() ?? string.Empty));
            }

            return attributes is null ? Array.Empty<XmlAttributeNode>() : attributes;
        }

        private static XmlDeclarationNode ParseDeclaration(string declarationText)
        {
            var version = "1.0";
            string? encoding = "utf-8";
            string? standalone = null;

            var span = declarationText.AsSpan();
            while (!span.IsEmpty)
            {
                span = span.TrimStart();
                if (span.IsEmpty)
                {
                    break;
                }

                var equalsIndex = span.IndexOf('=');
                if (equalsIndex < 0)
                {
                    break;
                }

                var name = span[..equalsIndex].Trim().ToString();
                span = span[(equalsIndex + 1)..].TrimStart();
                if (span.IsEmpty)
                {
                    break;
                }

                var quote = span[0];
                if (quote is not ('\'' or '"'))
                {
                    break;
                }

                span = span[1..];
                var closingQuoteIndex = span.IndexOf(quote);
                if (closingQuoteIndex < 0)
                {
                    break;
                }

                var value = span[..closingQuoteIndex].ToString();
                span = span[(closingQuoteIndex + 1)..];

                if (string.Equals(name, "version", StringComparison.OrdinalIgnoreCase))
                {
                    version = value;
                }
                else if (string.Equals(name, "encoding", StringComparison.OrdinalIgnoreCase))
                {
                    encoding = value;
                }
                else if (string.Equals(name, "standalone", StringComparison.OrdinalIgnoreCase))
                {
                    standalone = value;
                }
            }

            return new XmlDeclarationNode(version, encoding, standalone);
        }

        private static void AddChild(Stack<XmlElementNode> elementStack, XmlNode child)
        {
            if (elementStack.Count == 0)
            {
                return;
            }

            elementStack.Peek().AddChild(child);
        }

        private static bool IsMatchingElement(Utf8XmlReader reader, XmlElementNode element)
        {
            return string.Equals(element.LocalName, reader.GetLocalName(), StringComparison.Ordinal)
                && string.Equals(element.Prefix, reader.GetPrefix(), StringComparison.Ordinal)
                && string.Equals(element.NamespaceUri, reader.GetNamespaceUri(), StringComparison.Ordinal);
        }
    }
}