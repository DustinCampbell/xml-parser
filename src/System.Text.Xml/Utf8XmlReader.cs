using System.Runtime.CompilerServices;
using System.Text;

namespace System.Text.Xml;

/// <summary>
/// Provides a high-performance forward-only reader for UTF-8 encoded XML text.
/// </summary>
public ref struct Utf8XmlReader
{
    private const int InitialElementCapacity = 8;
    private const int InitialNamespaceCapacity = 16;
    private const int InitialAttributeCapacity = 8;
    private const int SpecialPrefixXml = -2;
    private const int SpecialPrefixXmlns = -3;

    private static readonly Encoding s_utf8 = new UTF8Encoding(false, true);
    private static ReadOnlySpan<byte> NameTerminators => " \t\r\n>/=?"u8;

    // Lookup table: 1 = name terminator character, 0 = valid name character
    private static ReadOnlySpan<byte> NameTerminatorTable => new byte[256]
    {
        // 0x00-0x0F
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        // 0x10-0x1F
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        // 0x20-0x2F: space=term, !=ok, "=ok, #=ok, $=ok, %=ok, &=ok, '=ok, (=ok, )=ok, *=ok, +=ok, ,=ok, -=ok, .=ok, /=term
        1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
        // 0x30-0x3F: 0-9=ok, :=ok, ;=ok, <=ok, ==term, >=term, ?=term
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1,
        // 0x40-0x4F: @-O all ok
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x50-0x5F: P-_ all ok
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x60-0x6F: `-o all ok
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x70-0x7F: p-DEL, DEL is term
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
        // 0x80-0xFF: all ok (UTF-8 continuation/start bytes are valid in names)
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

    private readonly ReadOnlySpan<byte> _xmlData;
    private readonly XmlReaderOptions _options;

    private ElementFrame[] _elementStack;
    private int _elementStackCount;

    private NamespaceEntry[] _namespaces;
    private int _namespaceCount;

    private AttributeEntry[] _attributes;
    private int _attributeCount;
    private int _attributeIndex;

    private int _pos;
    private int _depth;
    private int _currentTokenDepth;
    private int _lastStartElementDepth;

    private int _lastLineScanPos;
    private long _lineNumber;
    private int _lineStartPos;

    private bool _emitSyntheticEndElement;
    private ElementFrame _syntheticEndElement;
    private bool _isEmptyElement;
    private XmlTokenType _tokenType;

    private int _valueStart;
    private int _valueLength;
    private int _localNameStart;
    private int _localNameLength;
    private int _prefixStart;
    private int _prefixLength;
    private NamespaceUriRef _namespaceUri;

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8XmlReader"/> struct.
    /// </summary>
    public Utf8XmlReader(ReadOnlySpan<byte> xmlData, XmlReaderOptions options = default)
    {
        _xmlData = xmlData;
        _options = options;

        _elementStack = System.Buffers.ArrayPool<ElementFrame>.Shared.Rent(InitialElementCapacity);
        _elementStackCount = 0;

        _namespaces = System.Buffers.ArrayPool<NamespaceEntry>.Shared.Rent(InitialNamespaceCapacity);
        _namespaces[0] = NamespaceEntry.CreateSpecial(SpecialPrefixXml, NamespaceUriKind.Xml);
        _namespaces[1] = NamespaceEntry.CreateSpecial(SpecialPrefixXmlns, NamespaceUriKind.Xmlns);
        _namespaceCount = 2;

        _attributes = System.Buffers.ArrayPool<AttributeEntry>.Shared.Rent(InitialAttributeCapacity);
        _attributeCount = 0;
        _attributeIndex = -1;

        _pos = 0;
        _depth = 0;
        _currentTokenDepth = 0;
        _lastStartElementDepth = -1;

        _lastLineScanPos = 0;
        _lineNumber = 1;
        _lineStartPos = 0;

        _emitSyntheticEndElement = false;
        _syntheticEndElement = default;
        _isEmptyElement = false;
        _tokenType = XmlTokenType.None;

        _valueStart = 0;
        _valueLength = 0;
        _localNameStart = 0;
        _localNameLength = 0;
        _prefixStart = 0;
        _prefixLength = 0;
        _namespaceUri = default;
    }

    /// <summary>
    /// Returns rented arrays to the pool. Call when done with the reader.
    /// </summary>
    public void Dispose()
    {
        if (_elementStack is not null)
        {
            System.Buffers.ArrayPool<ElementFrame>.Shared.Return(_elementStack, clearArray: false);
            _elementStack = null!;
        }

        if (_namespaces is not null)
        {
            System.Buffers.ArrayPool<NamespaceEntry>.Shared.Return(_namespaces, clearArray: false);
            _namespaces = null!;
        }

        if (_attributes is not null)
        {
            System.Buffers.ArrayPool<AttributeEntry>.Shared.Return(_attributes, clearArray: false);
            _attributes = null!;
        }
    }

    /// <summary>
    /// Gets the type of the current token.
    /// </summary>
    public XmlTokenType TokenType => _tokenType;

    /// <summary>
    /// Gets the raw UTF-8 bytes for the current token value.
    /// </summary>
    public ReadOnlySpan<byte> ValueSpan => _valueLength == 0 ? default : _xmlData.Slice(_valueStart, _valueLength);

    /// <summary>
    /// Gets the raw UTF-8 bytes for the current local name.
    /// </summary>
    public ReadOnlySpan<byte> LocalNameSpan => _localNameLength == 0 ? default : _xmlData.Slice(_localNameStart, _localNameLength);

    /// <summary>
    /// Gets the raw UTF-8 bytes for the current prefix.
    /// </summary>
    public ReadOnlySpan<byte> PrefixSpan => _prefixLength == 0 ? default : _xmlData.Slice(_prefixStart, _prefixLength);

    /// <summary>
    /// Gets the raw UTF-8 bytes for the current namespace URI.
    /// </summary>
    public ReadOnlySpan<byte> NamespaceUriSpan => GetNamespaceUriSpan(_namespaceUri);

    /// <summary>
    /// Gets the depth of the current token.
    /// </summary>
    public int CurrentDepth => _currentTokenDepth;

    /// <summary>
    /// Gets the total number of bytes consumed.
    /// </summary>
    public long BytesConsumed => _pos;

    /// <summary>
    /// Gets the current line number.
    /// </summary>
    public long LineNumber
    {
        get
        {
            EnsureLineInfoAt(_pos);
            return _lineNumber;
        }
    }

    /// <summary>
    /// Gets the current byte position within the current line.
    /// </summary>
    public long BytePositionInLine
    {
        get
        {
            EnsureLineInfoAt(_pos);
            return _pos - _lineStartPos;
        }
    }

    /// <summary>
    /// Gets a value that indicates whether the current start element was self-closing.
    /// </summary>
    public bool IsEmptyElement => _isEmptyElement;

    /// <summary>
    /// Reads the next XML token.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Read()
    {
        _attributeIndex = -1;

        if (_emitSyntheticEndElement)
        {
            EmitSyntheticEndElement();
            return true;
        }

        while (_pos < _xmlData.Length)
        {
            if (_xmlData[_pos] == XmlConstants.LessThan)
            {
                if (TryReadMarkup())
                {
                    return true;
                }

                continue;
            }

            if (TryReadText())
            {
                return true;
            }
        }

        _tokenType = XmlTokenType.None;
        return false;
    }

    /// <summary>
    /// Moves to the next attribute for the current start element.
    /// </summary>
    public bool MoveToNextAttribute()
    {
        if (_attributeCount == 0 || (_tokenType != XmlTokenType.StartElement && _tokenType != XmlTokenType.Attribute))
        {
            return false;
        }

        int nextIndex = _tokenType == XmlTokenType.StartElement ? 0 : _attributeIndex + 1;
        if (nextIndex >= _attributeCount)
        {
            return false;
        }

        _attributeIndex = nextIndex;
        ref readonly AttributeEntry attribute = ref _attributes[_attributeIndex];
        _tokenType = XmlTokenType.Attribute;
        _currentTokenDepth = _lastStartElementDepth + 1;
        _valueStart = attribute.ValueStart;
        _valueLength = attribute.ValueLength;
        _localNameStart = attribute.LocalNameStart;
        _localNameLength = attribute.LocalNameLength;
        _prefixStart = attribute.PrefixStart;
        _prefixLength = attribute.PrefixLength;
        _namespaceUri = attribute.NamespaceUri;
        _isEmptyElement = false;
        return true;
    }

    /// <summary>
    /// Skips the children of the current element.
    /// </summary>
    public void Skip()
    {
        if (_tokenType != XmlTokenType.StartElement)
        {
            return;
        }

        int depth = _currentTokenDepth;
        if (_isEmptyElement)
        {
            Read();
            return;
        }

        while (Read())
        {
            if (_tokenType == XmlTokenType.EndElement && _currentTokenDepth == depth)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Returns the current token value as a string.
    /// </summary>
    public string? GetString()
    {
        ReadOnlySpan<byte> value = ValueSpan;
        if (value.IsEmpty)
        {
            return string.Empty;
        }

        string text = DecodeUtf8(value);
        if (_tokenType is XmlTokenType.Text or XmlTokenType.Attribute or XmlTokenType.EntityReference)
        {
            // Fast path: only unescape if there's actually an ampersand
            return value.IndexOf((byte)'&') >= 0 ? Unescape(text) : text;
        }

        return text;
    }

    /// <summary>
    /// Returns the current local name as a string.
    /// </summary>
    public string GetLocalName() => LocalNameSpan.IsEmpty ? string.Empty : DecodeUtf8(LocalNameSpan);

    /// <summary>
    /// Returns the current prefix as a string.
    /// </summary>
    public string GetPrefix() => PrefixSpan.IsEmpty ? string.Empty : DecodeUtf8(PrefixSpan);

    /// <summary>
    /// Returns the current namespace URI as a string.
    /// </summary>
    public string GetNamespaceUri()
    {
        switch (_namespaceUri.Kind)
        {
            case NamespaceUriKind.None:
                return string.Empty;
            case NamespaceUriKind.Xml:
                return "http://www.w3.org/XML/1998/namespace";
            case NamespaceUriKind.Xmlns:
                return "http://www.w3.org/2000/xmlns/";
            default:
                ReadOnlySpan<byte> value = _xmlData.Slice(_namespaceUri.Start, _namespaceUri.Length);
                if (value.IsEmpty)
                {
                    return string.Empty;
                }

                string text = DecodeUtf8(value);
                return value.IndexOf((byte)'&') >= 0 ? Unescape(text) : text;
        }
    }

    private void EmitSyntheticEndElement()
    {
        _emitSyntheticEndElement = false;
        _tokenType = XmlTokenType.EndElement;
        _currentTokenDepth = _depth - 1;
        _valueStart = 0;
        _valueLength = 0;
        _localNameStart = _syntheticEndElement.LocalNameStart;
        _localNameLength = _syntheticEndElement.LocalNameLength;
        _prefixStart = _syntheticEndElement.PrefixStart;
        _prefixLength = _syntheticEndElement.PrefixLength;
        _namespaceUri = _syntheticEndElement.NamespaceUri;
        _isEmptyElement = false;
        _depth--;
        _namespaceCount = _syntheticEndElement.NamespaceScopeStart;
        _elementStackCount--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadMarkup()
    {
        if (_pos + 1 >= _xmlData.Length)
        {
            ThrowUnexpectedEndOfFileAt(_pos);
        }

        byte next = _xmlData[_pos + 1];
        if (next == XmlConstants.Slash)
        {
            return ReadEndElement();
        }

        if (next == XmlConstants.Question)
        {
            if (IsXmlDeclarationStart())
            {
                return ReadXmlDeclaration();
            }

            return ReadProcessingInstruction();
        }

        if (next == XmlConstants.Exclamation)
        {
            ReadOnlySpan<byte> remaining = _xmlData.Slice(_pos);
            if (remaining.StartsWith("<!--"u8))
            {
                return ReadComment();
            }

            if (remaining.StartsWith("<![CDATA["u8))
            {
                return ReadCData();
            }

            if (remaining.StartsWith("<!DOCTYPE"u8))
            {
                ThrowXmlExceptionAt("Document type declarations are not supported.", _pos);
            }

            ThrowXmlExceptionAt("Unexpected markup.", _pos);
        }

        return ReadStartElement();
    }

    private bool ReadXmlDeclaration()
    {
        int start = _pos + 5;
        int end = IndexOf("?>"u8, start);
        if (end < 0)
        {
            ThrowUnexpectedEndOfFileAt(_xmlData.Length);
        }

        TrimAsciiRange(start, end - start, out int valueStart, out int valueLength);

        _tokenType = XmlTokenType.XmlDeclaration;
        _currentTokenDepth = 0;
        _valueStart = valueStart;
        _valueLength = valueLength;
        _localNameStart = 0;
        _localNameLength = 0;
        _prefixStart = 0;
        _prefixLength = 0;
        _namespaceUri = default;
        _isEmptyElement = false;
        Advance(end + 2 - _pos);
        return true;
    }

    private bool ReadComment()
    {
        int start = _pos + 4;
        int end = IndexOf("-->"u8, start);
        if (end < 0)
        {
            ThrowUnexpectedEndOfFileAt(_xmlData.Length);
        }

        _tokenType = XmlTokenType.Comment;
        _currentTokenDepth = _depth;
        _valueStart = start;
        _valueLength = end - start;
        _localNameStart = 0;
        _localNameLength = 0;
        _prefixStart = 0;
        _prefixLength = 0;
        _namespaceUri = default;
        _isEmptyElement = false;
        Advance(end + 3 - _pos);

        return _options.CommentHandling != XmlCommentHandling.Skip;
    }

    private bool ReadCData()
    {
        int start = _pos + 9;
        int end = IndexOf("]]>"u8, start);
        if (end < 0)
        {
            ThrowUnexpectedEndOfFileAt(_xmlData.Length);
        }

        _tokenType = XmlTokenType.CData;
        _currentTokenDepth = _depth;
        _valueStart = start;
        _valueLength = end - start;
        _localNameStart = 0;
        _localNameLength = 0;
        _prefixStart = 0;
        _prefixLength = 0;
        _namespaceUri = default;
        _isEmptyElement = false;
        Advance(end + 3 - _pos);
        return true;
    }

    private bool ReadProcessingInstruction()
    {
        int nameStart = _pos + 2;
        ParseQualifiedName(nameStart, out int nameEnd, out int prefixLength, out int localNameStart, out int localNameLength);
        int cursor = nameEnd;
        SkipWhitespace(ref cursor);
        int end = IndexOf("?>"u8, cursor);
        if (end < 0)
        {
            ThrowUnexpectedEndOfFileAt(_xmlData.Length);
        }

        _tokenType = XmlTokenType.ProcessingInstruction;
        _currentTokenDepth = _depth;
        _valueStart = cursor;
        _valueLength = Math.Max(0, end - cursor);
        _localNameStart = localNameStart;
        _localNameLength = localNameLength;
        _prefixStart = prefixLength == 0 ? 0 : nameStart;
        _prefixLength = prefixLength;
        _namespaceUri = default;
        _isEmptyElement = false;
        Advance(end + 2 - _pos);
        return true;
    }

    private bool ReadStartElement()
    {
        int nameStart = _pos + 1;
        ParseQualifiedName(nameStart, out int nameEnd, out int prefixLength, out int localNameStart, out int localNameLength);
        int cursor = nameEnd;
        int namespaceScopeStart = _namespaceCount;

        _attributeCount = 0;
        bool isEmpty = false;

        while (true)
        {
            SkipWhitespace(ref cursor);
            if (cursor >= _xmlData.Length)
            {
                ThrowUnexpectedEndOfFileAt(_xmlData.Length);
            }

            byte value = _xmlData[cursor];
            if (value == XmlConstants.GreaterThan)
            {
                cursor++;
                break;
            }

            if (value == XmlConstants.Slash)
            {
                if (cursor + 1 >= _xmlData.Length || _xmlData[cursor + 1] != XmlConstants.GreaterThan)
                {
                    ThrowExpectedTokenAt("'>'", cursor + 1 < _xmlData.Length ? cursor + 1 : cursor);
                }

                cursor += 2;
                isEmpty = true;
                break;
            }

            ParseAttribute(ref cursor);
        }

        ResolveAttributeNamespaces();
        NamespaceUriRef elementNamespace = ResolveElementNamespace(nameStart, prefixLength);

        _tokenType = XmlTokenType.StartElement;
        _currentTokenDepth = _depth;
        _lastStartElementDepth = _currentTokenDepth;
        _valueStart = 0;
        _valueLength = 0;
        _localNameStart = localNameStart;
        _localNameLength = localNameLength;
        _prefixStart = prefixLength == 0 ? 0 : nameStart;
        _prefixLength = prefixLength;
        _namespaceUri = elementNamespace;
        _isEmptyElement = isEmpty;

        Advance(cursor - _pos);
        PushElement(new ElementFrame(localNameStart, localNameLength, prefixLength == 0 ? 0 : nameStart, prefixLength, elementNamespace, namespaceScopeStart));

        _depth++;
        if (_depth > _options.MaxDepth)
        {
            ThrowXmlExceptionAt($"The maximum depth of {_options.MaxDepth} has been exceeded.", _pos);
        }

        if (isEmpty)
        {
            _emitSyntheticEndElement = true;
            _syntheticEndElement = _elementStack[_elementStackCount - 1];
        }

        return true;
    }

    private void ParseAttribute(ref int cursor)
    {
        int nameStart = cursor;
        ParseQualifiedName(nameStart, out int nameEnd, out int prefixLength, out int localNameStart, out int localNameLength);
        cursor = nameEnd;
        SkipWhitespace(ref cursor);

        if (cursor >= _xmlData.Length || _xmlData[cursor] != XmlConstants.EqualSign)
        {
            ThrowExpectedTokenAt("'='", cursor);
        }

        cursor++;
        SkipWhitespace(ref cursor);
        if (cursor >= _xmlData.Length)
        {
            ThrowUnexpectedEndOfFileAt(_xmlData.Length);
        }

        byte quote = _xmlData[cursor];
        if (quote is not (XmlConstants.Quote or XmlConstants.Apostrophe))
        {
            ThrowExpectedTokenAt("a quote character", cursor);
        }

        cursor++;
        int valueStart = cursor;
        // Scan for closing quote - manual loop is faster for typical short attribute values
        while (cursor < _xmlData.Length && _xmlData[cursor] != quote)
        {
            cursor++;
        }

        if (cursor >= _xmlData.Length)
        {
            ThrowUnexpectedEndOfFileAt(_xmlData.Length);
        }

        int valueLength = cursor - valueStart;
        cursor++; // skip closing quote

        EnsureAttributeCapacity(_attributeCount + 1);

        bool isDefaultNamespace = prefixLength == 0 && NameEquals(localNameStart, localNameLength, "xmlns"u8);
        bool isPrefixedNamespace = prefixLength == 5 && NameEquals(nameStart, prefixLength, "xmlns"u8);
        NamespaceUriRef namespaceUri = default;
        bool isNamespaceDeclaration = false;

        if (isDefaultNamespace)
        {
            AddNamespace(new NamespaceEntry(0, 0, valueStart, valueLength, NamespaceUriKind.Buffer));
            namespaceUri = NamespaceUriRef.Xmlns;
            isNamespaceDeclaration = true;
        }
        else if (isPrefixedNamespace)
        {
            AddNamespace(new NamespaceEntry(localNameStart, localNameLength, valueStart, valueLength, NamespaceUriKind.Buffer));
            namespaceUri = NamespaceUriRef.Xmlns;
            isNamespaceDeclaration = true;
        }

        _attributes[_attributeCount++] = new AttributeEntry(
            localNameStart,
            localNameLength,
            prefixLength == 0 ? 0 : nameStart,
            prefixLength,
            valueStart,
            valueLength,
            namespaceUri,
            isNamespaceDeclaration);
    }

    private bool ReadEndElement()
    {
        if (_elementStackCount == 0)
        {
            ThrowXmlExceptionAt("Unexpected end element.", _pos);
        }

        int nameStart = _pos + 2;
        ParseQualifiedName(nameStart, out int nameEnd, out int prefixLength, out int localNameStart, out int localNameLength);
        int cursor = nameEnd;
        SkipWhitespace(ref cursor);
        if (cursor >= _xmlData.Length || _xmlData[cursor] != XmlConstants.GreaterThan)
        {
            ThrowExpectedTokenAt("'>'", cursor);
        }

        ElementFrame expected = _elementStack[_elementStackCount - 1];
        if (!NameEquals(expected.LocalNameStart, expected.LocalNameLength, localNameStart, localNameLength) ||
            !NameEquals(expected.PrefixStart, expected.PrefixLength, prefixLength == 0 ? 0 : nameStart, prefixLength))
        {
            ThrowXmlExceptionAt("The end element does not match the current start element.", _pos);
        }

        _tokenType = XmlTokenType.EndElement;
        _currentTokenDepth = _depth - 1;
        _valueStart = 0;
        _valueLength = 0;
        _localNameStart = localNameStart;
        _localNameLength = localNameLength;
        _prefixStart = prefixLength == 0 ? 0 : nameStart;
        _prefixLength = prefixLength;
        _namespaceUri = expected.NamespaceUri;
        _isEmptyElement = false;

        Advance(cursor + 1 - _pos);
        _depth--;
        _namespaceCount = expected.NamespaceScopeStart;
        _elementStackCount--;
        return true;
    }

    private bool TryReadText()
    {
        ReadOnlySpan<byte> remaining = _xmlData.Slice(_pos);
        int relativeEnd = remaining.IndexOf(XmlConstants.LessThan);
        int end = relativeEnd >= 0 ? _pos + relativeEnd : _xmlData.Length;
        if (end == _pos)
        {
            return false;
        }

        ReadOnlySpan<byte> value = _xmlData.Slice(_pos, end - _pos);
        bool isWhitespace = IsAllWhitespace(value);
        if (isWhitespace && _options.IgnoreWhitespace)
        {
            Advance(end - _pos);
            return false;
        }

        _tokenType = isWhitespace ? XmlTokenType.Whitespace : XmlTokenType.Text;
        _currentTokenDepth = _depth;
        _valueStart = _pos;
        _valueLength = end - _pos;
        _localNameStart = 0;
        _localNameLength = 0;
        _prefixStart = 0;
        _prefixLength = 0;
        _namespaceUri = default;
        _isEmptyElement = false;
        Advance(end - _pos);
        return true;
    }

    private void ResolveAttributeNamespaces()
    {
        if (_attributeCount == 0)
        {
            return;
        }

        for (int i = 0; i < _attributeCount; i++)
        {
            AttributeEntry attribute = _attributes[i];
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            attribute.NamespaceUri = attribute.PrefixLength == 0
                ? default
                : ResolveNamespaceRef(attribute.PrefixStart, attribute.PrefixLength);
            _attributes[i] = attribute;
        }
    }

    private NamespaceUriRef ResolveElementNamespace(int nameStart, int prefixLength)
        => prefixLength == 0 ? ResolveNamespaceRef(0, 0) : ResolveNamespaceRef(nameStart, prefixLength);

    private NamespaceUriRef ResolveNamespaceRef(int prefixStart, int prefixLength)
    {
        for (int i = _namespaceCount - 1; i >= 0; i--)
        {
            ref readonly NamespaceEntry entry = ref _namespaces[i];
            if (PrefixMatches(in entry, prefixStart, prefixLength))
            {
                return new NamespaceUriRef(entry.UriStart, entry.UriLength, entry.UriKind);
            }
        }

        return default;
    }

    private bool PrefixMatches(in NamespaceEntry entry, int prefixStart, int prefixLength)
    {
        if (entry.PrefixLength != prefixLength)
        {
            return false;
        }

        if (entry.PrefixStart >= 0)
        {
            return prefixLength == 0 || _xmlData.Slice(entry.PrefixStart, prefixLength).SequenceEqual(_xmlData.Slice(prefixStart, prefixLength));
        }

        return entry.PrefixStart switch
        {
            SpecialPrefixXml => prefixLength == 3 && _xmlData.Slice(prefixStart, prefixLength).SequenceEqual("xml"u8),
            SpecialPrefixXmlns => prefixLength == 5 && _xmlData.Slice(prefixStart, prefixLength).SequenceEqual("xmlns"u8),
            _ => false,
        };
    }

    private void ParseQualifiedName(int start, out int end, out int prefixLength, out int localNameStart, out int localNameLength)
    {
        ReadOnlySpan<byte> table = NameTerminatorTable;
        int pos = start;
        int dataLength = _xmlData.Length;
        int colonIdx = -1;

        while (pos < dataLength)
        {
            byte b = _xmlData[pos];
            if (table[b] != 0)
            {
                break;
            }

            if (b == XmlConstants.Colon && colonIdx < 0)
            {
                colonIdx = pos - start;
            }

            pos++;
        }

        int nameLength = pos - start;
        end = pos;

        if (nameLength == 0)
        {
            ThrowXmlExceptionAt("Expected an XML name.", start);
        }

        if (colonIdx < 0)
        {
            prefixLength = 0;
            localNameStart = start;
            localNameLength = nameLength;
            return;
        }

        if (colonIdx == 0 || colonIdx == nameLength - 1)
        {
            ThrowXmlExceptionAt("Invalid qualified XML name.", start + colonIdx);
        }

        // Check for second colon
        for (int i = start + colonIdx + 1; i < pos; i++)
        {
            if (_xmlData[i] == XmlConstants.Colon)
            {
                ThrowXmlExceptionAt("Invalid qualified XML name.", i);
            }
        }

        prefixLength = colonIdx;
        localNameStart = start + colonIdx + 1;
        localNameLength = nameLength - colonIdx - 1;
    }

    private static ReadOnlySpan<byte> WhitespaceBytes => " \t\r\n"u8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SkipWhitespace(ref int cursor)
    {
        // Most whitespace runs in well-formed XML are 0-2 bytes (single space between attributes)
        // A simple loop is faster than vectorized IndexOfAnyExcept for short runs
        while (cursor < _xmlData.Length && IsWhitespace(_xmlData[cursor]))
        {
            cursor++;
        }
    }

    private static int IndexOfAnyExceptWhitespace(ReadOnlySpan<byte> value)
    {
#if NET7_0_OR_GREATER
        return value.IndexOfAnyExcept(WhitespaceBytes);
#else
        for (int i = 0; i < value.Length; i++)
        {
            if (!IsWhitespace(value[i]))
            {
                return i;
            }
        }

        return -1;
#endif
    }

    private void EnsureAttributeCapacity(int required)
    {
        if (_attributes.Length >= required)
        {
            return;
        }

        int newSize = _attributes.Length == 0 ? InitialAttributeCapacity : _attributes.Length * 2;
        if (newSize < required)
        {
            newSize = required;
        }

        var newArray = System.Buffers.ArrayPool<AttributeEntry>.Shared.Rent(newSize);
        if (_attributeCount > 0)
        {
            Array.Copy(_attributes, newArray, _attributeCount);
        }

        System.Buffers.ArrayPool<AttributeEntry>.Shared.Return(_attributes, clearArray: false);
        _attributes = newArray;
    }

    private void EnsureElementCapacity(int required)
    {
        if (_elementStack.Length >= required)
        {
            return;
        }

        int newSize = _elementStack.Length == 0 ? InitialElementCapacity : _elementStack.Length * 2;
        if (newSize < required)
        {
            newSize = required;
        }

        var newArray = System.Buffers.ArrayPool<ElementFrame>.Shared.Rent(newSize);
        if (_elementStackCount > 0)
        {
            Array.Copy(_elementStack, newArray, _elementStackCount);
        }

        System.Buffers.ArrayPool<ElementFrame>.Shared.Return(_elementStack, clearArray: false);
        _elementStack = newArray;
    }

    private void EnsureNamespaceCapacity(int required)
    {
        if (_namespaces.Length >= required)
        {
            return;
        }

        int newSize = _namespaces.Length == 0 ? InitialNamespaceCapacity : _namespaces.Length * 2;
        if (newSize < required)
        {
            newSize = required;
        }

        var newArray = System.Buffers.ArrayPool<NamespaceEntry>.Shared.Rent(newSize);
        if (_namespaceCount > 0)
        {
            Array.Copy(_namespaces, newArray, _namespaceCount);
        }

        System.Buffers.ArrayPool<NamespaceEntry>.Shared.Return(_namespaces, clearArray: false);
        _namespaces = newArray;
    }

    private void PushElement(ElementFrame frame)
    {
        EnsureElementCapacity(_elementStackCount + 1);
        _elementStack[_elementStackCount++] = frame;
    }

    private void AddNamespace(NamespaceEntry entry)
    {
        EnsureNamespaceCapacity(_namespaceCount + 1);
        _namespaces[_namespaceCount++] = entry;
    }

    private void Advance(int count) => _pos += count;

    private bool IsXmlDeclarationStart()
    {
        ReadOnlySpan<byte> remaining = _xmlData.Slice(_pos);
        if (!remaining.StartsWith("<?xml"u8))
        {
            return false;
        }

        int next = _pos + 5;
        return next >= _xmlData.Length || IsWhitespace(_xmlData[next]) || _xmlData[next] == XmlConstants.Question;
    }

    private int IndexOf(ReadOnlySpan<byte> value, int start)
    {
        int index = _xmlData.Slice(start).IndexOf(value);
        return index >= 0 ? start + index : -1;
    }

    private void EnsureLineInfoAt(int position)
    {
        if (position < 0)
        {
            position = 0;
        }
        else if (position > _xmlData.Length)
        {
            position = _xmlData.Length;
        }

        if (position < _lastLineScanPos)
        {
            _lastLineScanPos = 0;
            _lineNumber = 1;
            _lineStartPos = 0;
        }

        for (int i = _lastLineScanPos; i < position; i++)
        {
            byte value = _xmlData[i];
            if (value == XmlConstants.LineFeed)
            {
                _lineNumber++;
                _lineStartPos = i + 1;
            }
            else if (value == XmlConstants.CarriageReturn && (i + 1 >= _xmlData.Length || _xmlData[i + 1] != XmlConstants.LineFeed))
            {
                _lineNumber++;
                _lineStartPos = i + 1;
            }
        }

        _lastLineScanPos = position;
    }

    private void ThrowXmlExceptionAt(string message, int position)
    {
        GetLineInfo(position, out long lineNumber, out long bytePositionInLine);
        ThrowHelper.ThrowXmlException(message, lineNumber, bytePositionInLine);
    }

    private void ThrowUnexpectedEndOfFileAt(int position)
    {
        GetLineInfo(position, out long lineNumber, out long bytePositionInLine);
        ThrowHelper.ThrowUnexpectedEndOfFile(lineNumber, bytePositionInLine);
    }

    private void ThrowExpectedTokenAt(string expected, int position)
    {
        GetLineInfo(position, out long lineNumber, out long bytePositionInLine);
        ThrowHelper.ThrowExpectedToken(expected, lineNumber, bytePositionInLine);
    }

    private void GetLineInfo(int position, out long lineNumber, out long bytePositionInLine)
    {
        EnsureLineInfoAt(position);
        lineNumber = _lineNumber;
        bytePositionInLine = position - _lineStartPos;
    }

    private void TrimAsciiRange(int start, int length, out int trimmedStart, out int trimmedLength)
    {
        int end = start + length - 1;
        trimmedStart = start;

        while (trimmedStart <= end && IsWhitespace(_xmlData[trimmedStart]))
        {
            trimmedStart++;
        }

        while (end >= trimmedStart && IsWhitespace(_xmlData[end]))
        {
            end--;
        }

        trimmedLength = end >= trimmedStart ? end - trimmedStart + 1 : 0;
        if (trimmedLength == 0)
        {
            trimmedStart = 0;
        }
    }

    private static bool IsWhitespace(byte value)
        => value is XmlConstants.Space or XmlConstants.Tab or XmlConstants.CarriageReturn or XmlConstants.LineFeed;

    private static bool IsAllWhitespace(ReadOnlySpan<byte> value)
#if NET7_0_OR_GREATER
        => value.IndexOfAnyExcept(WhitespaceBytes) < 0;
#else
        => IndexOfAnyExceptWhitespace(value) < 0;
#endif

    private static string DecodeUtf8(ReadOnlySpan<byte> value)
    {
#if NET
        return s_utf8.GetString(value);
#else
        unsafe
        {
            fixed (byte* ptr = value)
            {
                return s_utf8.GetString(ptr, value.Length);
            }
        }
#endif
    }

    private bool NameEquals(int start, int length, ReadOnlySpan<byte> value)
        => length == value.Length && (length == 0 || _xmlData.Slice(start, length).SequenceEqual(value));

    private bool NameEquals(int leftStart, int leftLength, int rightStart, int rightLength)
        => leftLength == rightLength && (leftLength == 0 || _xmlData.Slice(leftStart, leftLength).SequenceEqual(_xmlData.Slice(rightStart, rightLength)));

    private ReadOnlySpan<byte> GetNamespaceUriSpan(NamespaceUriRef uri)
        => uri.Kind switch
        {
            NamespaceUriKind.Buffer => _xmlData.Slice(uri.Start, uri.Length),
            NamespaceUriKind.Xml => XmlNamespaceUriBytes,
            NamespaceUriKind.Xmlns => XmlnsNamespaceUriBytes,
            _ => default,
        };

    private static ReadOnlySpan<byte> XmlNamespaceUriBytes => "http://www.w3.org/XML/1998/namespace"u8;

    private static ReadOnlySpan<byte> XmlnsNamespaceUriBytes => "http://www.w3.org/2000/xmlns/"u8;

    internal static string Unescape(string value)
    {
        if (value.IndexOf('&') < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '&')
            {
                builder.Append(value[i]);
                continue;
            }

            int semicolon = value.IndexOf(';', i + 1);
            if (semicolon < 0)
            {
                builder.Append('&');
                continue;
            }

            string entity = value[(i + 1)..semicolon];
            builder.Append(entity switch
            {
                "amp" => '&',
                "lt" => '<',
                "gt" => '>',
                "quot" => '"',
                "apos" => '\'',
                _ when entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase) && int.TryParse(entity.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hex) => (char)hex,
                _ when entity.Length > 0 && entity[0] == '#' && int.TryParse(entity.Substring(1), out int dec) => (char)dec,
                _ => '&',
            });

            if (!(entity is "amp" or "lt" or "gt" or "quot" or "apos") &&
                !(entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase) || (entity.Length > 0 && entity[0] == '#')))
            {
                builder.Append(entity);
                builder.Append(';');
            }

            i = semicolon;
        }

        return builder.ToString();
    }

    private readonly struct NamespaceEntry
    {
        public NamespaceEntry(int prefixStart, int prefixLength, int uriStart, int uriLength, NamespaceUriKind uriKind)
        {
            PrefixStart = prefixStart;
            PrefixLength = prefixLength;
            UriStart = uriStart;
            UriLength = uriLength;
            UriKind = uriKind;
        }

        public int PrefixStart { get; }

        public int PrefixLength { get; }

        public int UriStart { get; }

        public int UriLength { get; }

        public NamespaceUriKind UriKind { get; }

        public static NamespaceEntry CreateSpecial(int prefixMarker, NamespaceUriKind uriKind)
            => new(prefixMarker, prefixMarker == SpecialPrefixXml ? 3 : 5, 0, 0, uriKind);
    }

    private readonly struct ElementFrame
    {
        public ElementFrame(int localNameStart, int localNameLength, int prefixStart, int prefixLength, NamespaceUriRef namespaceUri, int namespaceScopeStart)
        {
            LocalNameStart = localNameStart;
            LocalNameLength = localNameLength;
            PrefixStart = prefixStart;
            PrefixLength = prefixLength;
            NamespaceUri = namespaceUri;
            NamespaceScopeStart = namespaceScopeStart;
        }

        public int LocalNameStart { get; }

        public int LocalNameLength { get; }

        public int PrefixStart { get; }

        public int PrefixLength { get; }

        public NamespaceUriRef NamespaceUri { get; }

        public int NamespaceScopeStart { get; }
    }

    private struct AttributeEntry
    {
        public AttributeEntry(int localNameStart, int localNameLength, int prefixStart, int prefixLength, int valueStart, int valueLength, NamespaceUriRef namespaceUri, bool isNamespaceDeclaration)
        {
            LocalNameStart = localNameStart;
            LocalNameLength = localNameLength;
            PrefixStart = prefixStart;
            PrefixLength = prefixLength;
            ValueStart = valueStart;
            ValueLength = valueLength;
            NamespaceUri = namespaceUri;
            IsNamespaceDeclaration = isNamespaceDeclaration;
        }

        public int LocalNameStart;
        public int LocalNameLength;
        public int PrefixStart;
        public int PrefixLength;
        public int ValueStart;
        public int ValueLength;
        public NamespaceUriRef NamespaceUri;
        public bool IsNamespaceDeclaration;
    }

    private readonly struct NamespaceUriRef
    {
        public NamespaceUriRef(int start, int length, NamespaceUriKind kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }

        public int Start { get; }

        public int Length { get; }

        public NamespaceUriKind Kind { get; }

        public static NamespaceUriRef Xmlns => new(0, 0, NamespaceUriKind.Xmlns);
    }

    private enum NamespaceUriKind : byte
    {
        None,
        Buffer,
        Xml,
        Xmlns,
    }
}

internal static class Utf8XmlReaderExtensions
{
    public static ReadOnlySpan<byte> TrimAscii(this ReadOnlySpan<byte> value)
    {
        int start = 0;
        int end = value.Length - 1;
        while (start <= end && (value[start] is XmlConstants.Space or XmlConstants.Tab or XmlConstants.CarriageReturn or XmlConstants.LineFeed))
        {
            start++;
        }

        while (end >= start && (value[end] is XmlConstants.Space or XmlConstants.Tab or XmlConstants.CarriageReturn or XmlConstants.LineFeed))
        {
            end--;
        }

        return value.Slice(start, end - start + 1);
    }
}
