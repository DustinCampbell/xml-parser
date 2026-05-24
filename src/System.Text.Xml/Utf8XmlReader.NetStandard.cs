using System.Runtime.CompilerServices;

namespace System.Text.Xml;

#if !NET
// On .NET Framework / netstandard2.0, ReadOnlySpan<byte> indexing goes through the System.Memory
// polyfill which the JIT cannot optimize (no bounds check elimination). This file provides
// alternative implementations of hot scanning methods that use direct byte[] indexing, which
// the .NET Framework JIT CAN optimize with bounds check elimination.
//
// This follows the same pattern as System.Text.Json's JsonReaderHelper.netstandard.cs.

public ref partial struct Utf8XmlReader
{
    /// <summary>
    /// Reads the next XML token. Uses direct array indexing for optimal .NET Framework performance.
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

        byte[]? data = _rawData;
        if (data != null)
        {
            while (_pos < data.Length)
            {
                if (data[_pos] == XmlConstants.LessThan)
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
        }
        else
        {
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
        }

        _tokenType = XmlTokenType.None;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadMarkup()
    {
        byte[]? data = _rawData;
        if (data != null)
        {
            if (_pos + 1 >= data.Length)
            {
                ThrowUnexpectedEndOfFileAt(_pos);
            }

            byte next = data[_pos + 1];
            return DispatchMarkup(next);
        }
        else
        {
            if (_pos + 1 >= _xmlData.Length)
            {
                ThrowUnexpectedEndOfFileAt(_pos);
            }

            byte next = _xmlData[_pos + 1];
            return DispatchMarkup(next);
        }
    }

    private bool DispatchMarkup(byte next)
    {
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

    private void ParseQualifiedName(int start, out int end, out int prefixLength, out int localNameStart, out int localNameLength)
    {
        byte[]? data = _rawData;
        if (data != null)
        {
            ParseQualifiedNameArray(data, start, out end, out prefixLength, out localNameStart, out localNameLength);
        }
        else
        {
            ParseQualifiedNameSpan(start, out end, out prefixLength, out localNameStart, out localNameLength);
        }
    }

    private void ParseQualifiedNameArray(byte[] data, int start, out int end, out int prefixLength, out int localNameStart, out int localNameLength)
    {
        byte[] table = s_nameTerminatorTableArray;
        int pos = start;
        int colonIdx = -1;

        // Both data[pos] and table[b] are direct array indexing — no span polyfill overhead
        while (pos < data.Length)
        {
            byte b = data[pos];
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
            if (data[i] == XmlConstants.Colon)
            {
                ThrowXmlExceptionAt("Invalid qualified XML name.", i);
            }
        }

        prefixLength = colonIdx;
        localNameStart = start + colonIdx + 1;
        localNameLength = nameLength - colonIdx - 1;
    }

    private void ParseQualifiedNameSpan(int start, out int end, out int prefixLength, out int localNameStart, out int localNameLength)
    {
        byte[] table = s_nameTerminatorTableArray;
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

    private void SkipWhitespace(ref int cursor)
    {
        byte[]? data = _rawData;
        if (data != null)
        {
            while (cursor < data.Length && IsWhitespace(data[cursor]))
            {
                cursor++;
            }
        }
        else
        {
            while (cursor < _xmlData.Length && IsWhitespace(_xmlData[cursor]))
            {
                cursor++;
            }
        }
    }

    private bool TryReadText()
    {
        byte[]? data = _rawData;
        int end;

        if (data != null)
        {
            // Direct array scan for '<' — JIT eliminates bounds checks
            end = _pos;
            while (end < data.Length && data[end] != XmlConstants.LessThan)
            {
                end++;
            }
        }
        else
        {
            ReadOnlySpan<byte> remaining = _xmlData.Slice(_pos);
            int relativeEnd = remaining.IndexOf(XmlConstants.LessThan);
            end = relativeEnd >= 0 ? _pos + relativeEnd : _xmlData.Length;
        }

        if (end == _pos)
        {
            return false;
        }

        ReadOnlySpan<byte> value = _xmlData.Slice(_pos, end - _pos);
        bool isWhitespace;
        if (data != null)
        {
            isWhitespace = IsAllWhitespaceArray(data, _pos, end - _pos);
        }
        else
        {
            isWhitespace = IsAllWhitespace(value);
        }
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

    private bool ReadStartElement()
    {
        int nameStart = _pos + 1;
        ParseQualifiedName(nameStart, out int nameEnd, out int prefixLength, out int localNameStart, out int localNameLength);
        int cursor = nameEnd;
        int namespaceScopeStart = _namespaceCount;

        _attributeCount = 0;
        bool isEmpty = false;

        byte[]? data = _rawData;
        if (data != null)
        {
            while (true)
            {
                SkipWhitespace(ref cursor);
                if (cursor >= data.Length)
                {
                    ThrowUnexpectedEndOfFileAt(data.Length);
                }

                byte value = data[cursor];
                if (value == XmlConstants.GreaterThan)
                {
                    cursor++;
                    break;
                }

                if (value == XmlConstants.Slash)
                {
                    if (cursor + 1 >= data.Length || data[cursor + 1] != XmlConstants.GreaterThan)
                    {
                        ThrowExpectedTokenAt("'>'", cursor + 1 < data.Length ? cursor + 1 : cursor);
                    }

                    cursor += 2;
                    isEmpty = true;
                    break;
                }

                ParseAttribute(ref cursor);
            }
        }
        else
        {
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

        byte[]? data = _rawData;
        if (data != null)
        {
            if (cursor >= data.Length || data[cursor] != XmlConstants.EqualSign)
            {
                ThrowExpectedTokenAt("'='", cursor);
            }

            cursor++;
            SkipWhitespace(ref cursor);
            if (cursor >= data.Length)
            {
                ThrowUnexpectedEndOfFileAt(data.Length);
            }

            byte quote = data[cursor];
            if (quote is not (XmlConstants.Quote or XmlConstants.Apostrophe))
            {
                ThrowExpectedTokenAt("a quote character", cursor);
            }

            cursor++;
            int valueStart = cursor;
            // Direct array scan for closing quote — JIT eliminates bounds checks
            while (cursor < data.Length && data[cursor] != quote)
            {
                cursor++;
            }

            if (cursor >= data.Length)
            {
                ThrowUnexpectedEndOfFileAt(data.Length);
            }

            int valueLength = cursor - valueStart;
            cursor++; // skip closing quote

            StoreAttribute(nameStart, prefixLength, localNameStart, localNameLength, valueStart, valueLength);
        }
        else
        {
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

            StoreAttribute(nameStart, prefixLength, localNameStart, localNameLength, valueStart, valueLength);
        }
    }

    private void StoreAttribute(int nameStart, int prefixLength, int localNameStart, int localNameLength, int valueStart, int valueLength)
    {
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

        byte[]? data = _rawData;
        int dataLength = data?.Length ?? _xmlData.Length;
        byte endByte = data != null && cursor < data.Length ? data[cursor] : (cursor < _xmlData.Length ? _xmlData[cursor] : (byte)0);

        if (cursor >= dataLength || endByte != XmlConstants.GreaterThan)
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

    private int IndexOf(ReadOnlySpan<byte> value, int start)
    {
        byte[]? data = _rawData;
        if (data != null && value.Length <= 3)
        {
            // Manual search using direct array indexing for short patterns (-->, ]]>, ?>)
            byte first = value[0];
            int searchEnd = data.Length - value.Length + 1;
            for (int i = start; i < searchEnd; i++)
            {
                if (data[i] == first)
                {
                    bool match = true;
                    for (int j = 1; j < value.Length; j++)
                    {
                        if (data[i + j] != value[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match) return i;
                }
            }

            return -1;
        }

        int index = _xmlData.Slice(start).IndexOf(value);
        return index >= 0 ? start + index : -1;
    }

    private static int IndexOfAnyExceptWhitespace(ReadOnlySpan<byte> value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!IsWhitespace(value[i]))
            {
                return i;
            }
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAllWhitespaceArray(byte[] data, int start, int length)
    {
        int end = start + length;
        while (start < end)
        {
            if (!IsWhitespace(data[start]))
            {
                return false;
            }

            start++;
        }

        return true;
    }
}
#endif
