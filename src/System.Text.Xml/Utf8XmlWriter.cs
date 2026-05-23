using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace System.Text.Xml;

/// <summary>
/// Provides a high-performance forward-only way of generating UTF-8 encoded XML.
/// </summary>
public sealed class Utf8XmlWriter : IDisposable
#if NET
    , IAsyncDisposable
#endif
{
    private static readonly Encoding s_utf8 = new UTF8Encoding(false);
#if NET8_0_OR_GREATER
    private static readonly SearchValues<char> s_textEscapeChars = SearchValues.Create("&<>");
    private static readonly SearchValues<char> s_attributeEscapeChars = SearchValues.Create("&<>\"'");
#endif

    private readonly IBufferWriter<byte>? _output;
    private readonly Stream? _stream;
    private ArrayBufferWriter<byte>? _streamBuffer;
    private readonly bool _indented;
    private readonly char _indentCharacter;
    private readonly int _indentSize;
    private readonly string _newLine;
    private readonly bool _omitXmlDeclaration;

    private ElementState[] _elements;
    private bool[] _textContentStates;
    private XmlNamespace[] _namespaceBindings;
    private int[] _namespaceScopeCounts;
    private int _elementCount;
    private int _namespaceCount;
    private int _namespaceScopeDepth;

    // Cache for pre-encoded UTF-8 qualified names to avoid repeated encoding
    private NameEncodingEntry[] _nameCache;
    private int _nameCacheCount;

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
        _indented = options.Indented;
        _indentCharacter = options.IndentCharacter;
        _indentSize = options.IndentSize;
        _newLine = options.NewLine;
        _omitXmlDeclaration = options.OmitXmlDeclaration;
        _elements = new ElementState[8];
        _textContentStates = new bool[8];
        _namespaceBindings = new XmlNamespace[8];
        _namespaceScopeCounts = new int[8];
        _nameCache = new NameEncodingEntry[16];
        InitializeNamespaces();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8XmlWriter"/> class that writes to a <see cref="Stream"/>.
    /// </summary>
    public Utf8XmlWriter(Stream utf8Stream, XmlWriterOptions options = default)
    {
        _stream = utf8Stream ?? throw new ArgumentNullException(nameof(utf8Stream));
        _streamBuffer = new ArrayBufferWriter<byte>();
        _indented = options.Indented;
        _indentCharacter = options.IndentCharacter;
        _indentSize = options.IndentSize;
        _newLine = options.NewLine;
        _omitXmlDeclaration = options.OmitXmlDeclaration;
        _elements = new ElementState[8];
        _textContentStates = new bool[8];
        _namespaceBindings = new XmlNamespace[8];
        _namespaceScopeCounts = new int[8];
        _nameCache = new NameEncodingEntry[16];
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
    public int CurrentDepth => _elementCount;

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

        WriteStartDocumentCore();
    }

    /// <summary>
    /// Closes all open elements and completes the document.
    /// </summary>
    public void WriteEndDocument()
    {
        CheckDisposed();

        while (_elementCount > 0)
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
        ThrowHelper.ThrowIfNull(localName);

        PrepareForChildNode();
        WriteStartElementOpen(localName, prefix);

        EnsureElementCapacity(_elementCount + 1);
        EnsureNamespaceScopeCapacity(_namespaceScopeDepth + 1);
        _elements[_elementCount] = new ElementState(localName, prefix ?? string.Empty);
        _textContentStates[_elementCount] = false;
        _elementCount++;
        _namespaceScopeCounts[_namespaceScopeDepth++] = 0;
        _openStartElement = true;

        if (!string.IsNullOrEmpty(ns) || !string.IsNullOrEmpty(prefix))
        {
            EnsureElementNamespace(ns, prefix);
        }
    }

    /// <summary>
    /// Writes the matching end element for the current element.
    /// </summary>
    public void WriteEndElement()
    {
        if (_elementCount == 0)
        {
            ThrowHelper.ThrowInvalidOperation("There is no open element to close.");
        }

        int currentIndex = _elementCount - 1;
        ElementState element = _elements[currentIndex];
        bool hadTextContent = _textContentStates[currentIndex];

        if (_openStartElement)
        {
            WriteLiteral(" />"u8);
            _openStartElement = false;
            PopElementScope();
            return;
        }

        if (_indented && !hadTextContent)
        {
            WriteNewLine();
            WriteIndentation(currentIndex);
        }

        WriteEndElementClose(element.LocalName, element.Prefix);
        PopElementScope();
    }

    /// <summary>
    /// Writes an attribute with a string value.
    /// </summary>
    public void WriteAttributeString(string localName, string? value, string? ns = null, string? prefix = null)
    {
        ThrowHelper.ThrowIfNull(localName);

        if (!_openStartElement)
        {
            ThrowHelper.ThrowInvalidOperation("Attributes can only be written immediately after a start element.");
        }

        if (prefix is "xmlns" || (string.IsNullOrEmpty(prefix) && string.Equals(localName, "xmlns", StringComparison.Ordinal)))
        {
            WriteNamespaceDeclaration(localName, prefix, value ?? string.Empty);
            return;
        }

        // Fast path: no namespace resolution needed when both ns and prefix are empty
        string effectivePrefix;
        if (string.IsNullOrEmpty(ns) && string.IsNullOrEmpty(prefix))
        {
            effectivePrefix = string.Empty;
        }
        else
        {
            effectivePrefix = ResolveAttributePrefix(ns, prefix);
        }

        WriteAttributeStart(localName, effectivePrefix);
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
        ThrowHelper.ThrowIfNull(text);

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
        ThrowHelper.ThrowIfNull(text);

        if (text.IndexOf("]]>", StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException("CDATA content cannot contain the sequence ']]>'.", nameof(text));
        }

        CloseStartElement();
        MarkCurrentElementAsTextual();
        WriteLiteral("<![CDATA["u8);
        WriteUtf8String(text);
        WriteLiteral("]]>"u8);
    }

    /// <summary>
    /// Writes a comment.
    /// </summary>
    public void WriteComment(string text)
    {
        CheckDisposed();
        ThrowHelper.ThrowIfNull(text);

        if (text.IndexOf("--", StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException("Comment content cannot contain '--'.", nameof(text));
        }

        PrepareForChildNode();
        WriteLiteral("<!--"u8);
        WriteUtf8String(text);
        WriteLiteral("-->"u8);
    }

    /// <summary>
    /// Writes a processing instruction.
    /// </summary>
    public void WriteProcessingInstruction(string name, string? text)
    {
        CheckDisposed();
        ThrowHelper.ThrowIfNull(name);

        PrepareForChildNode();
        WriteProcessingInstructionStart(name, !string.IsNullOrEmpty(text));
        if (!string.IsNullOrEmpty(text))
        {
            WriteUtf8String(text!);
        }

        WriteLiteral("?>"u8);
    }

    /// <summary>
    /// Writes raw XML without escaping.
    /// </summary>
    public void WriteRaw(string rawXml)
    {
        CheckDisposed();
        ThrowHelper.ThrowIfNull(rawXml);

        CloseStartElement();
        MarkCurrentElementAsTextual();
        WriteUtf8String(rawXml);
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

#if NET
        await _stream.WriteAsync(_streamBuffer.WrittenMemory).ConfigureAwait(false);
#else
        var memory = _streamBuffer.WrittenMemory;
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
        {
            await _stream.WriteAsync(segment.Array!, segment.Offset, segment.Count).ConfigureAwait(false);
        }
        else
        {
            byte[] buffer = memory.Span.ToArray();
            await _stream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        }
#endif
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

#if NET
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
#endif

    private void InitializeNamespaces()
    {
        _namespaceBindings[0] = XmlNamespace.Xml;
        _namespaceBindings[1] = XmlNamespace.Xmlns;
        _namespaceCount = 2;
        _namespaceScopeCounts[0] = 2;
        _namespaceScopeDepth = 1;
    }

    private void WriteStartDocumentCore()
    {
        if (_omitXmlDeclaration)
        {
            _documentWritten = true;
            return;
        }

        WriteLiteral("<?xml version=\"1.0\" encoding=\"utf-8\"?>"u8);
        _documentWritten = true;

        if (_indented)
        {
            WriteNewLine();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureDocumentState()
    {
        if (_documentWritten)
        {
            if (_documentCompleted)
            {
                ThrowHelper.ThrowInvalidOperation("The document has already been completed.");
            }

            return;
        }

        EnsureDocumentStateSlow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureDocumentStateSlow()
    {
        if (_documentCompleted)
        {
            ThrowHelper.ThrowInvalidOperation("The document has already been completed.");
        }

        WriteStartDocumentCore();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PrepareForChildNode()
    {
        EnsureDocumentState();
        CloseStartElement();

        if (_indented && _elementCount > 0 && !_textContentStates[_elementCount - 1])
        {
            WriteNewLine();
            WriteIndentation(_elementCount);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        _elementCount--;
        _textContentStates[_elementCount] = false;

        int scopeCount = _namespaceScopeCounts[--_namespaceScopeDepth];
        if (scopeCount != 0)
        {
            _namespaceCount -= scopeCount;
        }
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
            return existingPrefix!;
        }

        string generatedPrefix = $"ns{++_autoPrefixCounter}";
        WriteNamespaceDeclaration(generatedPrefix, "xmlns", ns!);
        return generatedPrefix;
    }

    private void WriteNamespaceDeclaration(string localName, string? prefix, string uri)
    {
        WriteAttributeStart(localName, prefix);
        WriteEscapedString(uri, attributeValue: true);
        WriteByte(XmlConstants.Quote);

        string declaredPrefix = prefix == "xmlns" ? localName : string.Empty;
        AddNamespace(new XmlNamespace(declaredPrefix, uri));
    }

    private void AddNamespace(XmlNamespace xmlNamespace)
    {
        EnsureNamespaceBindingCapacity(_namespaceCount + 1);
        _namespaceBindings[_namespaceCount++] = xmlNamespace;
        _namespaceScopeCounts[_namespaceScopeDepth - 1]++;
    }

    private string? LookupNamespace(string prefix)
    {
        for (int i = _namespaceCount - 1; i >= 0; i--)
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
        for (int i = _namespaceCount - 1; i >= 0; i--)
        {
            XmlNamespace xmlNamespace = _namespaceBindings[i];
            if (!string.IsNullOrEmpty(xmlNamespace.Prefix) && string.Equals(xmlNamespace.Uri, namespaceUri, StringComparison.Ordinal))
            {
                return xmlNamespace.Prefix;
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkCurrentElementAsTextual()
    {
        if (_elementCount > 0)
        {
            _textContentStates[_elementCount - 1] = true;
        }
    }

    private void WriteEscapedString(string text, bool attributeValue)
    {
        ReadOnlySpan<char> remaining = text.AsSpan();

        while (!remaining.IsEmpty)
        {
            int index = IndexOfEscapeCharacter(remaining, attributeValue);
            if (index < 0)
            {
                WriteUtf8String(remaining);
                return;
            }

            if (index > 0)
            {
                WriteUtf8String(remaining[..index]);
            }

            WriteEscapeSequence(remaining[index]);
            remaining = remaining[(index + 1)..];
        }
    }

    private static int IndexOfEscapeCharacter(ReadOnlySpan<char> value, bool attributeValue)
    {
#if NET8_0_OR_GREATER
        return value.IndexOfAny(attributeValue ? s_attributeEscapeChars : s_textEscapeChars);
#elif NET
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch == '&' || ch == '<' || ch == '>' || (attributeValue && (ch == '"' || ch == '\'')))
            {
                return i;
            }
        }

        return -1;
#else
        unsafe
        {
            fixed (char* ptr = value)
            {
                char* p = ptr;
                char* pEnd = ptr + value.Length;
                if (attributeValue)
                {
                    while (p < pEnd)
                    {
                        char ch = *p;
                        if (ch == '&' || ch == '<' || ch == '>' || ch == '"' || ch == '\'')
                        {
                            return (int)(p - ptr);
                        }
                        p++;
                    }
                }
                else
                {
                    while (p < pEnd)
                    {
                        char ch = *p;
                        if (ch == '&' || ch == '<' || ch == '>')
                        {
                            return (int)(p - ptr);
                        }
                        p++;
                    }
                }
            }
        }

        return -1;
#endif
    }

    private void WriteEscapeSequence(char value)
    {
        switch (value)
        {
            case '&':
                WriteLiteral("&amp;"u8);
                break;
            case '<':
                WriteLiteral("&lt;"u8);
                break;
            case '>':
                WriteLiteral("&gt;"u8);
                break;
            case '"':
                WriteLiteral("&quot;"u8);
                break;
            case '\'':
                WriteLiteral("&apos;"u8);
                break;
        }
    }

    private void WriteIndentation(int depth)
    {
        int count = depth * _indentSize;
        if (count == 0)
        {
            return;
        }

        if (_indentCharacter <= 0x7F)
        {
            Span<byte> span = GetDestinationSpan(count);
            span[..count].Fill((byte)_indentCharacter);
            AdvanceOutput(count);
            return;
        }

        Span<char> buffer = count <= 256 ? stackalloc char[count] : new char[count];
        buffer.Fill(_indentCharacter);
        WriteUtf8String(buffer);
    }

    private void WriteNewLine() => WriteUtf8String(_newLine);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteLiteral(ReadOnlySpan<byte> value)
    {
        Span<byte> span = GetDestinationSpan(value.Length);
        value.CopyTo(span);
        AdvanceOutput(value.Length);
    }

    private void WriteStartElementOpen(string localName, string? prefix)
    {
        int sizeHint = 1 + GetQualifiedNameMaxByteCount(localName, prefix);
        Span<byte> span = GetDestinationSpan(sizeHint);
        span[0] = XmlConstants.LessThan;
        int written = 1 + WriteQualifiedNameToSpan(localName, prefix, span[1..]);
        AdvanceOutput(written);
    }

    private void WriteEndElementClose(string localName, string? prefix)
    {
        int sizeHint = 3 + GetQualifiedNameMaxByteCount(localName, prefix);
        Span<byte> span = GetDestinationSpan(sizeHint);
        span[0] = XmlConstants.LessThan;
        span[1] = XmlConstants.Slash;
        int written = 2 + WriteQualifiedNameToSpan(localName, prefix, span[2..]);
        span[written] = XmlConstants.GreaterThan;
        AdvanceOutput(written + 1);
    }

    private void WriteAttributeStart(string localName, string? prefix)
    {
        int sizeHint = 3 + GetQualifiedNameMaxByteCount(localName, prefix);
        Span<byte> span = GetDestinationSpan(sizeHint);
        span[0] = XmlConstants.Space;
        int written = 1 + WriteQualifiedNameToSpan(localName, prefix, span[1..]);
        span[written] = XmlConstants.EqualSign;
        span[written + 1] = XmlConstants.Quote;
        AdvanceOutput(written + 2);
    }

    private void WriteProcessingInstructionStart(string name, bool appendTrailingSpace)
    {
        int sizeHint = 2 + GetMaxByteCount(name.Length) + (appendTrailingSpace ? 1 : 0);
        Span<byte> span = GetDestinationSpan(sizeHint);
        span[0] = XmlConstants.LessThan;
        span[1] = XmlConstants.Question;
        int written = 2 + EncodeToSpan(name, span[2..]);
        if (appendTrailingSpace)
        {
            span[written++] = XmlConstants.Space;
        }

        AdvanceOutput(written);
    }

    private void WriteUtf8String(string value) => WriteUtf8String(value.AsSpan());

    private void WriteUtf8String(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        Span<byte> asciiSpan = GetDestinationSpan(value.Length);
        if (TryEncodeAscii(value, asciiSpan, out int asciiWritten))
        {
            AdvanceOutput(asciiWritten);
            return;
        }

#if NET
        int byteCount = s_utf8.GetByteCount(value);
        Span<byte> span = GetDestinationSpan(byteCount);
        int written = s_utf8.GetBytes(value, span);
#else
        int byteCount;
        int written;
        unsafe
        {
            fixed (char* charPtr = value)
            {
                byteCount = s_utf8.GetByteCount(charPtr, value.Length);
                Span<byte> span = GetDestinationSpan(byteCount);
                fixed (byte* bytePtr = span)
                {
                    written = s_utf8.GetBytes(charPtr, value.Length, bytePtr, byteCount);
                }
            }
        }
#endif
        AdvanceOutput(written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteByte(byte value)
    {
        Span<byte> span = GetDestinationSpan(1);
        span[0] = value;
        AdvanceOutput(1);
    }

    private int WriteQualifiedNameToSpan(string localName, string? prefix, Span<byte> destination)
    {
        byte[]? cached = LookupCachedName(localName, prefix);
        if (cached is not null)
        {
            cached.AsSpan().CopyTo(destination);
            return cached.Length;
        }

        int written = EncodeQualifiedNameUncached(localName, prefix, destination);
        CacheEncodedName(localName, prefix, destination.Slice(0, written));
        return written;
    }

    private static int EncodeQualifiedNameUncached(string localName, string? prefix, Span<byte> destination)
    {
        int written = 0;
        if (!string.IsNullOrEmpty(prefix))
        {
            int prefixBytes = EncodeToSpan(prefix!, destination);
            destination[prefixBytes] = XmlConstants.Colon;
            written = prefixBytes + 1;
            destination = destination[written..];
        }

        return written + EncodeToSpan(localName, destination);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[]? LookupCachedName(string localName, string? prefix)
    {
        var cache = _nameCache;
        for (int i = 0; i < _nameCacheCount; i++)
        {
            ref var entry = ref cache[i];
            if (ReferenceEquals(entry.LocalName, localName) && ReferenceEquals(entry.Prefix, prefix))
            {
                return entry.Utf8Bytes;
            }
        }

        return null;
    }

    private void CacheEncodedName(string localName, string? prefix, ReadOnlySpan<byte> encoded)
    {
        if (_nameCacheCount < _nameCache.Length)
        {
            _nameCache[_nameCacheCount++] = new NameEncodingEntry(localName, prefix, encoded.ToArray());
        }
    }

    private static int EncodeToSpan(ReadOnlySpan<char> value, Span<byte> destination)
    {
        if (TryEncodeAscii(value, destination, out int written))
        {
            return written;
        }

#if NET
        return s_utf8.GetBytes(value, destination);
#else
        unsafe
        {
            fixed (char* charPtr = value)
            fixed (byte* bytePtr = destination)
            {
                return s_utf8.GetBytes(charPtr, value.Length, bytePtr, destination.Length);
            }
        }
#endif
    }

    private static bool TryEncodeAscii(ReadOnlySpan<char> value, Span<byte> destination, out int written)
    {
#if NET8_0_OR_GREATER
        if (System.Text.Ascii.FromUtf16(value, destination, out written) == System.Buffers.OperationStatus.Done)
        {
            return true;
        }
#elif NET
        if (value.Length <= destination.Length)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch > 0x7F)
                {
                    written = 0;
                    return false;
                }

                destination[i] = (byte)ch;
            }

            written = value.Length;
            return true;
        }
#else
        if (value.Length <= destination.Length)
        {
            unsafe
            {
                fixed (char* srcPtr = value)
                fixed (byte* dstPtr = destination)
                {
                    char* src = srcPtr;
                    char* srcEnd = srcPtr + value.Length;
                    byte* dst = dstPtr;
                    while (src < srcEnd)
                    {
                        char ch = *src;
                        if (ch > 0x7F)
                        {
                            written = 0;
                            return false;
                        }

                        *dst = (byte)ch;
                        src++;
                        dst++;
                    }
                }
            }

            written = value.Length;
            return true;
        }
#endif

        written = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetQualifiedNameMaxByteCount(string localName, string? prefix)
    {
        int byteCount = GetMaxByteCount(localName.Length);
        if (!string.IsNullOrEmpty(prefix))
        {
            byteCount += GetMaxByteCount(prefix!.Length) + 1;
        }

        return byteCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetMaxByteCount(int charCount) => charCount == 0 ? 0 : charCount * 3;

    private void EnsureElementCapacity(int capacity)
    {
        if (capacity <= _elements.Length)
        {
            return;
        }

        int newCapacity = _elements.Length * 2;
        while (newCapacity < capacity)
        {
            newCapacity *= 2;
        }

        Array.Resize(ref _elements, newCapacity);
        Array.Resize(ref _textContentStates, newCapacity);
    }

    private void EnsureNamespaceBindingCapacity(int capacity)
    {
        if (capacity <= _namespaceBindings.Length)
        {
            return;
        }

        int newCapacity = _namespaceBindings.Length * 2;
        while (newCapacity < capacity)
        {
            newCapacity *= 2;
        }

        Array.Resize(ref _namespaceBindings, newCapacity);
    }

    private void EnsureNamespaceScopeCapacity(int capacity)
    {
        if (capacity <= _namespaceScopeCounts.Length)
        {
            return;
        }

        int newCapacity = _namespaceScopeCounts.Length * 2;
        while (newCapacity < capacity)
        {
            newCapacity *= 2;
        }

        Array.Resize(ref _namespaceScopeCounts, newCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<byte> GetDestinationSpan(int sizeHint)
        => _streamBuffer is not null ? _streamBuffer.GetSpan(sizeHint) : _output!.GetSpan(sizeHint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceOutput(int count)
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

#if NET
        _stream.Write(_streamBuffer.WrittenSpan);
#else
        // Avoid .ToArray() allocation by using MemoryMarshal to get the underlying array
        var memory = _streamBuffer.WrittenMemory;
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
        {
            _stream.Write(segment.Array!, segment.Offset, segment.Count);
        }
        else
        {
            byte[] buffer = memory.Span.ToArray();
            _stream.Write(buffer, 0, buffer.Length);
        }
#endif
        _bytesCommitted += _streamBuffer.WrittenCount;
        _streamBuffer.Clear();
        _stream.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Utf8XmlWriter));
        }
    }

    private readonly struct ElementState
    {
        public ElementState(string localName, string prefix)
        {
            LocalName = localName;
            Prefix = prefix;
        }

        public string LocalName { get; }

        public string Prefix { get; }
    }

    private readonly struct NameEncodingEntry
    {
        public NameEncodingEntry(string localName, string? prefix, byte[] utf8Bytes)
        {
            LocalName = localName;
            Prefix = prefix;
            Utf8Bytes = utf8Bytes;
        }

        public string LocalName { get; }
        public string? Prefix { get; }
        public byte[] Utf8Bytes { get; }
    }
}
