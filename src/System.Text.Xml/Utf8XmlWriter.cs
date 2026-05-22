using System.Buffers;
using System.Text;

namespace System.Text.Xml;

/// <summary>
/// Provides a high-performance forward-only way of generating UTF-8 encoded XML.
/// </summary>
public sealed class Utf8XmlWriter : IDisposable, IAsyncDisposable
{
    private static readonly Encoding s_utf8 = new UTF8Encoding(false);

    private readonly IBufferWriter<byte>? _output;
    private readonly Stream? _stream;
    private ArrayBufferWriter<byte>? _streamBuffer;
    private readonly XmlWriterOptions _options;
    private readonly List<ElementState> _elements = new();
    private readonly List<XmlNamespace> _namespaceBindings = new();
    private readonly List<int> _namespaceScopeCounts = new();
    private BitStack _textContentStates;

    private bool _openStartElement;
    private bool _disposed;
    private bool _documentWritten;
    private bool _documentCompleted;
    private long _bytesCommitted;
    private int _autoPrefixCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8XmlWriter"/> class that writes to an <see cref="IBufferWriter{T}"/>.
    /// </summary>
    public Utf8XmlWriter(IBufferWriter<byte> output, XmlWriterOptions options = default)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _options = options;
        InitializeNamespaces();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8XmlWriter"/> class that writes to a <see cref="Stream"/>.
    /// </summary>
    public Utf8XmlWriter(Stream utf8Stream, XmlWriterOptions options = default)
    {
        _stream = utf8Stream ?? throw new ArgumentNullException(nameof(utf8Stream));
        _streamBuffer = new ArrayBufferWriter<byte>();
        _options = options;
        InitializeNamespaces();
    }

    /// <summary>
    /// Gets the number of bytes written to the underlying destination but not yet flushed.
    /// </summary>
    public int BytesPending => _streamBuffer?.WrittenCount ?? 0;

    /// <summary>
    /// Gets the total number of bytes committed to the final destination.
    /// </summary>
    public long BytesCommitted => _bytesCommitted;

    /// <summary>
    /// Gets the current element depth.
    /// </summary>
    public int CurrentDepth => _elements.Count;

    /// <summary>
    /// Writes the XML declaration.
    /// </summary>
    public void WriteStartDocument()
    {
        CheckDisposed();

        if (_documentWritten)
        {
            ThrowHelper.ThrowInvalidOperation("The XML declaration has already been written.");
        }

        if (_options.OmitXmlDeclaration)
        {
            _documentWritten = true;
            return;
        }

        WriteRawCore("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        _documentWritten = true;

        if (_options.Indented)
        {
            WriteNewLine();
        }
    }

    /// <summary>
    /// Closes all open elements and completes the document.
    /// </summary>
    public void WriteEndDocument()
    {
        CheckDisposed();

        while (_elements.Count > 0)
        {
            WriteEndElement();
        }

        _documentCompleted = true;
    }

    /// <summary>
    /// Writes a start element.
    /// </summary>
    public void WriteStartElement(string localName, string? ns = null, string? prefix = null)
    {
        CheckDisposed();
        ValidateName(localName);
        if (!string.IsNullOrEmpty(prefix))
        {
            ValidateName(prefix!);
        }

        EnsureDocumentState();
        PrepareForChildNode();

        WriteByte(XmlConstants.LessThan);
        WriteQualifiedName(localName, prefix);

        _elements.Add(new ElementState(localName, prefix ?? string.Empty, ns ?? string.Empty));
        _namespaceScopeCounts.Add(0);
        _textContentStates.Push(false);
        _openStartElement = true;

        EnsureElementNamespace(ns, prefix);
    }

    /// <summary>
    /// Writes the matching end element for the current element.
    /// </summary>
    public void WriteEndElement()
    {
        CheckDisposed();

        if (_elements.Count == 0)
        {
            ThrowHelper.ThrowInvalidOperation("There is no open element to close.");
        }

        ElementState element = _elements[^1];
        bool hadTextContent = _textContentStates.Peek();

        if (_openStartElement)
        {
            WriteRawCore(" />");
            _openStartElement = false;
            PopElementScope();
            return;
        }

        if (_options.Indented && !hadTextContent)
        {
            WriteNewLine();
            WriteIndentation(_elements.Count - 1);
        }

        WriteRawCore("</");
        WriteQualifiedName(element.LocalName, element.Prefix);
        WriteByte(XmlConstants.GreaterThan);

        PopElementScope();
    }

    /// <summary>
    /// Writes an attribute with a string value.
    /// </summary>
    public void WriteAttributeString(string localName, string? value, string? ns = null, string? prefix = null)
    {
        CheckDisposed();
        ValidateName(localName);

        if (!_openStartElement)
        {
            ThrowHelper.ThrowInvalidOperation("Attributes can only be written immediately after a start element.");
        }

        if (prefix is "xmlns" || (string.IsNullOrEmpty(prefix) && string.Equals(localName, "xmlns", StringComparison.Ordinal)))
        {
            WriteNamespaceDeclaration(localName, prefix, value ?? string.Empty);
            return;
        }

        string effectivePrefix = ResolveAttributePrefix(ns, prefix);

        WriteByte(XmlConstants.Space);
        WriteQualifiedName(localName, effectivePrefix);
        WriteRawCore("=\"");
        if (!string.IsNullOrEmpty(value))
        {
            WriteEscapedString(value!, attributeValue: true);
        }

        WriteByte(XmlConstants.Quote);
    }

    /// <summary>
    /// Writes text content.
    /// </summary>
    public void WriteString(string text)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(text);

        CloseStartElement();
        MarkCurrentElementAsTextual();
        WriteEscapedString(text, attributeValue: false);
    }

    /// <summary>
    /// Writes a CDATA section.
    /// </summary>
    public void WriteCData(string text)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(text);

        if (text.Contains("]]>", StringComparison.Ordinal))
        {
            throw new ArgumentException("CDATA content cannot contain the sequence ']]>'.", nameof(text));
        }

        CloseStartElement();
        MarkCurrentElementAsTextual();
        WriteRawCore("<![CDATA[");
        WriteRawCore(text);
        WriteRawCore("]]>");
    }

    /// <summary>
    /// Writes a comment.
    /// </summary>
    public void WriteComment(string text)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(text);

        if (text.Contains("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Comment content cannot contain '--'.", nameof(text));
        }

        PrepareForChildNode();
        WriteRawCore("<!--");
        WriteRawCore(text);
        WriteRawCore("-->");
    }

    /// <summary>
    /// Writes a processing instruction.
    /// </summary>
    public void WriteProcessingInstruction(string name, string? text)
    {
        CheckDisposed();
        ValidateName(name);

        PrepareForChildNode();
        WriteRawCore("<?");
        WriteRawCore(name);
        if (!string.IsNullOrEmpty(text))
        {
            WriteByte(XmlConstants.Space);
            WriteRawCore(text!);
        }

        WriteRawCore("?>");
    }

    /// <summary>
    /// Writes raw XML without escaping.
    /// </summary>
    public void WriteRaw(string rawXml)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(rawXml);

        CloseStartElement();
        MarkCurrentElementAsTextual();
        WriteRawCore(rawXml);
    }

    /// <summary>
    /// Flushes any buffered XML to the underlying destination.
    /// </summary>
    public void Flush()
    {
        CheckDisposed();
        FlushCore();
    }

    /// <summary>
    /// Flushes any buffered XML to the underlying destination asynchronously.
    /// </summary>
    public async Task FlushAsync()
    {
        CheckDisposed();

        if (_streamBuffer is null || _stream is null || _streamBuffer.WrittenCount == 0)
        {
            return;
        }

        await _stream.WriteAsync(_streamBuffer.WrittenMemory).ConfigureAwait(false);
        _bytesCommitted += _streamBuffer.WrittenCount;
        _streamBuffer.Clear();
        await _stream.FlushAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        FlushCore();
        _disposed = true;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await FlushAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void InitializeNamespaces()
    {
        _namespaceBindings.Add(XmlNamespace.Xml);
        _namespaceBindings.Add(XmlNamespace.Xmlns);
        _namespaceScopeCounts.Add(2);
    }

    private void EnsureDocumentState()
    {
        if (_documentCompleted)
        {
            ThrowHelper.ThrowInvalidOperation("The document has already been completed.");
        }

        if (!_documentWritten)
        {
            WriteStartDocument();
        }
    }

    private void PrepareForChildNode()
    {
        EnsureDocumentState();
        CloseStartElement();

        if (_options.Indented && _elements.Count > 0 && !_textContentStates.Peek())
        {
            WriteNewLine();
            WriteIndentation(_elements.Count);
        }
    }

    private void CloseStartElement()
    {
        if (_openStartElement)
        {
            WriteByte(XmlConstants.GreaterThan);
            _openStartElement = false;
        }
    }

    private void PopElementScope()
    {
        _elements.RemoveAt(_elements.Count - 1);
        int scopeCount = _namespaceScopeCounts[^1];
        _namespaceScopeCounts.RemoveAt(_namespaceScopeCounts.Count - 1);
        if (scopeCount > 0)
        {
            _namespaceBindings.RemoveRange(_namespaceBindings.Count - scopeCount, scopeCount);
        }

        _textContentStates.Pop();
    }

    private void EnsureElementNamespace(string? ns, string? prefix)
    {
        if (string.IsNullOrEmpty(ns))
        {
            if (!string.IsNullOrEmpty(prefix) && LookupNamespace(prefix!) is null)
            {
                ThrowHelper.ThrowInvalidOperation($"The prefix '{prefix}' is not bound to a namespace URI.");
            }

            return;
        }

        if (string.IsNullOrEmpty(prefix))
        {
            string? currentDefault = LookupNamespace(string.Empty);
            if (!string.Equals(currentDefault, ns, StringComparison.Ordinal))
            {
                WriteNamespaceDeclaration("xmlns", null, ns!);
            }

            return;
        }

        string? existing = LookupNamespace(prefix!);
        if (!string.Equals(existing, ns, StringComparison.Ordinal))
        {
            WriteNamespaceDeclaration(prefix!, "xmlns", ns!);
        }
    }

    private string ResolveAttributePrefix(string? ns, string? prefix)
    {
        if (string.IsNullOrEmpty(ns))
        {
            if (!string.IsNullOrEmpty(prefix) && LookupNamespace(prefix!) is null)
            {
                ThrowHelper.ThrowInvalidOperation($"The prefix '{prefix}' is not bound to a namespace URI.");
            }

            return prefix ?? string.Empty;
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            string? existing = LookupNamespace(prefix!);
            if (!string.Equals(existing, ns, StringComparison.Ordinal))
            {
                WriteNamespaceDeclaration(prefix!, "xmlns", ns!);
            }

            return prefix!;
        }

        string? existingPrefix = LookupPrefix(ns!);
        if (!string.IsNullOrEmpty(existingPrefix))
        {
            return existingPrefix;
        }

        string generatedPrefix = $"ns{++_autoPrefixCounter}";
        WriteNamespaceDeclaration(generatedPrefix, "xmlns", ns!);
        return generatedPrefix;
    }

    private void WriteNamespaceDeclaration(string localName, string? prefix, string uri)
    {
        WriteByte(XmlConstants.Space);
        WriteQualifiedName(localName, prefix);
        WriteRawCore("=\"");
        WriteEscapedString(uri, attributeValue: true);
        WriteByte(XmlConstants.Quote);

        string declaredPrefix = prefix == "xmlns" ? localName : string.Empty;
        AddNamespace(new XmlNamespace(declaredPrefix, uri));
    }

    private void AddNamespace(XmlNamespace xmlNamespace)
    {
        _namespaceBindings.Add(xmlNamespace);
        _namespaceScopeCounts[^1]++;
    }

    private string? LookupNamespace(string prefix)
    {
        for (int i = _namespaceBindings.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_namespaceBindings[i].Prefix, prefix, StringComparison.Ordinal))
            {
                return _namespaceBindings[i].Uri;
            }
        }

        return null;
    }

    private string? LookupPrefix(string namespaceUri)
    {
        for (int i = _namespaceBindings.Count - 1; i >= 0; i--)
        {
            XmlNamespace xmlNamespace = _namespaceBindings[i];
            if (!string.IsNullOrEmpty(xmlNamespace.Prefix) && string.Equals(xmlNamespace.Uri, namespaceUri, StringComparison.Ordinal))
            {
                return xmlNamespace.Prefix;
            }
        }

        return null;
    }

    private void MarkCurrentElementAsTextual()
    {
        if (_elements.Count > 0)
        {
            _textContentStates.SetTop(true);
        }
    }

    private void WriteQualifiedName(string localName, string? prefix)
    {
        if (!string.IsNullOrEmpty(prefix))
        {
            WriteRawCore(prefix!);
            WriteByte(XmlConstants.Colon);
        }

        WriteRawCore(localName);
    }

    private void WriteEscapedString(string text, bool attributeValue)
    {
        int lastSegmentStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            string? replacement = text[i] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' when attributeValue => "&quot;",
                '\'' when attributeValue => "&apos;",
                _ => null,
            };

            if (replacement is null)
            {
                continue;
            }

            if (i > lastSegmentStart)
            {
                WriteRawCore(text.AsSpan(lastSegmentStart, i - lastSegmentStart));
            }

            WriteRawCore(replacement);
            lastSegmentStart = i + 1;
        }

        if (lastSegmentStart < text.Length)
        {
            WriteRawCore(text.AsSpan(lastSegmentStart));
        }
    }

    private void WriteIndentation(int depth)
    {
        int count = depth * _options.IndentSize;
        if (count == 0)
        {
            return;
        }

        Span<char> buffer = count <= 256 ? stackalloc char[count] : new char[count];
        buffer.Fill(_options.IndentCharacter);
        WriteRawCore(buffer);
    }

    private void WriteNewLine() => WriteRawCore(_options.NewLine);

    private void WriteRawCore(string text) => WriteRawCore(text.AsSpan());

    private void WriteRawCore(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        int byteCount = s_utf8.GetByteCount(text);
        Span<byte> span = GetDestinationSpan(byteCount);
        int written = s_utf8.GetBytes(text, span);
        Advance(written);
    }

    private void WriteByte(byte value)
    {
        Span<byte> span = GetDestinationSpan(1);
        span[0] = value;
        Advance(1);
    }

    private Span<byte> GetDestinationSpan(int sizeHint)
        => _streamBuffer is not null ? _streamBuffer.GetSpan(sizeHint) : _output!.GetSpan(sizeHint);

    private void Advance(int count)
    {
        if (_streamBuffer is not null)
        {
            _streamBuffer.Advance(count);
        }
        else
        {
            _output!.Advance(count);
            _bytesCommitted += count;
        }
    }

    private void FlushCore()
    {
        if (_streamBuffer is null || _stream is null || _streamBuffer.WrittenCount == 0)
        {
            return;
        }

        _stream.Write(_streamBuffer.WrittenSpan);
        _bytesCommitted += _streamBuffer.WrittenCount;
        _streamBuffer.Clear();
        _stream.Flush();
    }

    private void CheckDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Utf8XmlWriter));
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ThrowHelper.ThrowInvalidXmlName(name);
        }

        for (int i = 0; i < name.Length; i++)
        {
            char ch = name[i];
            bool valid = char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.';
            if (!valid)
            {
                ThrowHelper.ThrowInvalidXmlName(name);
            }
        }
    }

    private readonly record struct ElementState(string LocalName, string Prefix, string NamespaceUri);
}