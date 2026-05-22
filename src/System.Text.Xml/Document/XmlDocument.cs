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
        ThrowHelper.ThrowIfNull(xml);
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
        ThrowHelper.ThrowIfNull(stream);
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
        ThrowHelper.ThrowIfNullOrEmpty(filePath);
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
        ThrowHelper.ThrowIfNull(stream);
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
        ThrowHelper.ThrowIfDisposed(_disposed, this);
        ThrowHelper.ThrowIfNull(writer);

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
        ThrowHelper.ThrowIfDisposed(_disposed, this);
        ThrowHelper.ThrowIfNull(stream);

        using var textWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true);
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
        ThrowHelper.ThrowIfDisposed(_disposed, this);
        ThrowHelper.ThrowIfNullOrEmpty(filePath);

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
        ThrowHelper.ThrowIfDisposed(_disposed, this);
        ThrowHelper.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        Save(buffer);
        buffer.Position = 0;
        await buffer.CopyToAsync(stream, 81920, cancellationToken).ConfigureAwait(false);
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
        await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
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

            try
            {
            var elementStack = new XmlElementNode[16];
            int elementStackCount = 0;
            XmlDeclarationNode? declaration = null;
            XmlElementNode? root = null;
            bool skipNextEndElement = false;
            var nameCache = new Utf8StringCache(64);

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case XmlTokenType.XmlDeclaration:
                        declaration = ParseDeclaration(reader.GetString() ?? string.Empty);
                        break;

                    case XmlTokenType.StartElement:
                    {
                        var localName = nameCache.GetOrAdd(reader.LocalNameSpan);
                        var prefix = nameCache.GetOrAdd(reader.PrefixSpan);
                        var namespaceUri = reader.GetNamespaceUri();
                        if (namespaceUri.Length > 0)
                        {
                            namespaceUri = nameCache.GetOrAdd(reader.NamespaceUriSpan);
                        }
                        var isEmptyElement = reader.IsEmptyElement;
                        var attributes = ReadAttributes(ref reader, nameCache);
                        var element = new XmlElementNode(localName, prefix, namespaceUri, attributes);

                        if (elementStackCount > 0)
                        {
                            elementStack[elementStackCount - 1].AddChildInternal(element);
                        }
                        else
                        {
                            root = element;
                        }

                        if (!isEmptyElement)
                        {
                            if (elementStackCount == elementStack.Length)
                            {
                                Array.Resize(ref elementStack, elementStack.Length * 2);
                            }

                            elementStack[elementStackCount++] = element;
                        }
                        else
                        {
                            skipNextEndElement = true;
                        }

                        break;
                    }

                    case XmlTokenType.EndElement:
                        if (skipNextEndElement)
                        {
                            skipNextEndElement = false;
                        }
                        else if (elementStackCount > 0)
                        {
                            elementStack[--elementStackCount] = null!;
                        }
                        break;

                    case XmlTokenType.Text:
                        if (elementStackCount > 0)
                        {
                            elementStack[elementStackCount - 1].SetDirectText(reader.GetString() ?? string.Empty);
                        }
                        break;

                    case XmlTokenType.CData:
                        if (elementStackCount > 0)
                        {
                            elementStack[elementStackCount - 1].AddChildInternal(new XmlCDataNode(reader.GetString() ?? string.Empty));
                        }
                        break;

                    case XmlTokenType.Comment:
                        if (elementStackCount > 0)
                        {
                            elementStack[elementStackCount - 1].AddChildInternal(new XmlCommentNode(reader.GetString() ?? string.Empty));
                        }
                        break;

                    case XmlTokenType.ProcessingInstruction:
                        if (elementStackCount > 0)
                        {
                            elementStack[elementStackCount - 1].AddChildInternal(new XmlProcessingInstructionNode(reader.GetLocalName(), reader.GetString()));
                        }
                        break;
                }
            }

            if (root is null)
            {
                throw new FormatException("The XML payload did not contain a document element.");
            }

            return new XmlDocument(root, declaration);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private static XmlAttributeNode[]? ReadAttributes(ref Utf8XmlReader reader, Utf8StringCache nameCache)
        {
            if (!reader.MoveToNextAttribute())
            {
                return null;
            }

            // First attribute
            var first = new XmlAttributeNode(
                nameCache.GetOrAdd(reader.LocalNameSpan),
                nameCache.GetOrAdd(reader.PrefixSpan),
                GetCachedNamespaceUri(ref reader, nameCache),
                reader.GetString() ?? string.Empty);

            if (!reader.MoveToNextAttribute())
            {
                return new[] { first };
            }

            // Multiple attributes - use small stack buffer
            var buffer = new XmlAttributeNode[8];
            buffer[0] = first;
            int count = 1;

            do
            {
                if (count == buffer.Length)
                {
                    Array.Resize(ref buffer, buffer.Length * 2);
                }

                buffer[count++] = new XmlAttributeNode(
                    nameCache.GetOrAdd(reader.LocalNameSpan),
                    nameCache.GetOrAdd(reader.PrefixSpan),
                    GetCachedNamespaceUri(ref reader, nameCache),
                    reader.GetString() ?? string.Empty);
            }
            while (reader.MoveToNextAttribute());

            if (count == buffer.Length)
            {
                return buffer;
            }

            var result = new XmlAttributeNode[count];
            Array.Copy(buffer, result, count);
            return result;
        }

        private static string GetCachedNamespaceUri(ref Utf8XmlReader reader, Utf8StringCache cache)
        {
            var nsUri = reader.GetNamespaceUri();
            if (nsUri.Length == 0)
            {
                return string.Empty;
            }

            return cache.GetOrAdd(reader.NamespaceUriSpan);
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

    }

}