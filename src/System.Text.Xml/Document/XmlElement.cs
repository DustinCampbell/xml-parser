using System.Collections.Generic;
using System.Text;

namespace System.Text.Xml;

/// <summary>
/// Represents an XML element within an <see cref="XmlDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XmlElement"/> is a lightweight readonly struct that provides zero-allocation
/// navigation over a parsed XML document's metadata. It does not own any data — the parent
/// <see cref="XmlDocument"/> must remain alive while any <see cref="XmlElement"/> is in use.
/// </para>
/// <para>
/// This design mirrors <c>System.Text.Json.JsonElement</c>: parsed documents are read-only
/// and navigation produces value-type views rather than heap-allocated node objects.
/// </para>
/// </remarks>
public readonly struct XmlElement
{
    private readonly XmlDocument _document;
    private readonly int _index;

    internal XmlElement(XmlDocument document, int index)
    {
        _document = document;
        _index = index;
    }

    internal int Index => _index;
    internal XmlDocument Document => _document;

    /// <summary>
    /// Gets the local name of the element.
    /// </summary>
    public string LocalName
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            return _document.GetName(row.NameStart, row.NameLength);
        }
    }

    /// <summary>
    /// Gets the namespace prefix of the element.
    /// </summary>
    public string Prefix
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            if (row.PrefixLength == 0) return string.Empty;
            return _document.GetName(row.NameStart - row.PrefixLength - 1, row.PrefixLength);
        }
    }

    /// <summary>
    /// Gets the namespace URI of the element.
    /// </summary>
    public string NamespaceUri
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            if (row.NsUriLength == 0) return string.Empty;
            return _document.GetName(row.NsUriStart, row.NsUriLength);
        }
    }

    /// <summary>
    /// Gets the number of attributes on this element.
    /// </summary>
    public int AttributeCount
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            return row.AttributeCount;
        }
    }

    /// <summary>
    /// Gets the number of direct child nodes (excluding attributes).
    /// </summary>
    public int ChildCount
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            return row.ChildCount;
        }
    }

    /// <summary>
    /// Gets the concatenated text content of the element and all descendant text nodes.
    /// </summary>
    public string InnerText
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            int endIdx = row.EndIndex;
            int childStart = _index + 1 + row.AttributeCount;

            // Fast path: single text child
            if (row.ChildCount == 1)
            {
                ref readonly DbRow child = ref _document.GetRow(childStart);
                if (child.NodeType == XmlNodeType.Text)
                {
                    return _document.GetDecodedValue(child.ValueStart, child.ValueLength);
                }
            }

            if (row.ChildCount == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            AppendInnerText(sb, childStart, endIdx);
            return sb.ToString();
        }
    }

    private void AppendInnerText(StringBuilder sb, int startIdx, int endIdx)
    {
        for (int i = startIdx; i < endIdx; i++)
        {
            ref readonly DbRow child = ref _document.GetRow(i);
            switch (child.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CData:
                    sb.Append(_document.GetDecodedValue(child.ValueStart, child.ValueLength));
                    break;
                case XmlNodeType.Element:
                    // Recurse into element's children (skip attributes)
                    int innerStart = i + 1 + child.AttributeCount;
                    AppendInnerText(sb, innerStart, child.EndIndex);
                    i = child.EndIndex - 1; // skip entire subtree
                    break;
            }
        }
    }

    /// <summary>
    /// Gets the leading trivia (whitespace, comments) that appear before this element in the source.
    /// </summary>
    /// <remarks>
    /// This method only returns trivia when the document was parsed with
    /// <see cref="XmlDocumentOptions.PreserveTrivia"/> set to <c>true</c>.
    /// Otherwise, it returns an empty list.
    /// </remarks>
    public XmlTriviaList GetLeadingTrivia() => _document.GetLeadingTriviaForNode(_index);

    /// <summary>
    /// Gets the trailing trivia (whitespace, comments) that appear after this element in the source.
    /// </summary>
    /// <remarks>
    /// This method only returns trivia when the document was parsed with
    /// <see cref="XmlDocumentOptions.PreserveTrivia"/> set to <c>true</c>.
    /// Otherwise, it returns an empty list.
    /// </remarks>
    public XmlTriviaList GetTrailingTrivia() => _document.GetTrailingTriviaForNode(_index);

    /// <summary>
    /// Returns the first attribute with the specified local name.
    /// </summary>
    public XmlAttribute? GetAttribute(string localName, string? ns = null)
    {
        ref readonly DbRow row = ref _document.GetRow(_index);
        int attrCount = row.AttributeCount;
        if (attrCount == 0) return null;

        int firstAttr = _index + 1;
        for (int i = 0; i < attrCount; i++)
        {
            int attrIdx = firstAttr + i;
            ref readonly DbRow attrRow = ref _document.GetRow(attrIdx);
            if (_document.NameEquals(attrRow.NameStart, attrRow.NameLength, localName))
            {
                if (ns is null || ns.Length == 0 || _document.NamespaceEquals(attrRow.NsUriStart, attrRow.NsUriLength, ns))
                {
                    return new XmlAttribute(_document, attrIdx);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a struct enumerator over the element's attributes.
    /// </summary>
    public AttributeEnumerator EnumerateAttributes() => new AttributeEnumerator(_document, _index);

    /// <summary>
    /// Returns a struct enumerator over the element's direct child nodes.
    /// </summary>
    public ChildEnumerator EnumerateChildren() => new ChildEnumerator(_document, _index);

    /// <summary>
    /// Returns a struct enumerator over the element's direct child elements.
    /// </summary>
    public ElementEnumerator EnumerateElements() => new ElementEnumerator(_document, _index, null);

    /// <summary>
    /// Returns a struct enumerator over the element's direct child elements with the specified local name.
    /// </summary>
    public ElementEnumerator EnumerateElements(string localName) => new ElementEnumerator(_document, _index, localName);

    /// <summary>
    /// Returns all direct child elements (allocates an IEnumerable).
    /// </summary>
    public IEnumerable<XmlElement> Elements()
    {
        var enumerator = EnumerateElements();
        while (enumerator.MoveNext())
        {
            yield return enumerator.Current;
        }
    }

    /// <summary>
    /// Returns all direct child elements with the specified local name (allocates an IEnumerable).
    /// </summary>
    public IEnumerable<XmlElement> Elements(string localName)
    {
        var enumerator = EnumerateElements(localName);
        while (enumerator.MoveNext())
        {
            yield return enumerator.Current;
        }
    }

    /// <summary>
    /// Returns all descendant elements (allocates an IEnumerable).
    /// </summary>
    public IEnumerable<XmlElement> Descendants()
    {
        ref readonly DbRow row = ref _document.GetRow(_index);
        int endIdx = row.EndIndex;
        int childStart = _index + 1 + row.AttributeCount;

        for (int i = childStart; i < endIdx; i++)
        {
            ref readonly DbRow child = ref _document.GetRow(i);
            if (child.NodeType == XmlNodeType.Element)
            {
                yield return new XmlElement(_document, i);
            }
        }
    }

    /// <summary>
    /// Enumerates attributes of an element without heap allocation.
    /// </summary>
    public struct AttributeEnumerator
    {
        private readonly XmlDocument _document;
        private readonly int _firstAttr;
        private readonly int _count;
        private int _current;

        internal AttributeEnumerator(XmlDocument document, int elementIndex)
        {
            _document = document;
            ref readonly DbRow row = ref document.GetRow(elementIndex);
            _firstAttr = elementIndex + 1;
            _count = row.AttributeCount;
            _current = -1;
        }

        public XmlAttribute Current => new XmlAttribute(_document, _firstAttr + _current);

        public bool MoveNext()
        {
            _current++;
            return _current < _count;
        }

        public AttributeEnumerator GetEnumerator() => this;
    }

    /// <summary>
    /// Enumerates direct child nodes of an element without heap allocation.
    /// </summary>
    public struct ChildEnumerator
    {
        private readonly XmlDocument _document;
        private readonly int _startIndex;
        private readonly int _endIndex;
        private int _current;

        internal ChildEnumerator(XmlDocument document, int elementIndex)
        {
            _document = document;
            ref readonly DbRow row = ref document.GetRow(elementIndex);
            _startIndex = elementIndex + 1 + row.AttributeCount;
            _endIndex = row.EndIndex;
            _current = _startIndex - 1; // before first
        }

        public XmlNodeValue Current => new XmlNodeValue(_document, _current);

        public bool MoveNext()
        {
            if (_current < _startIndex)
            {
                _current = _startIndex;
            }
            else
            {
                // Skip over subtree of current node
                ref readonly DbRow row = ref _document.GetRow(_current);
                if (row.NodeType == XmlNodeType.Element)
                {
                    _current = row.EndIndex;
                }
                else
                {
                    _current++;
                }
            }

            return _current < _endIndex;
        }

        public ChildEnumerator GetEnumerator() => this;
    }

    /// <summary>
    /// Enumerates direct child elements of an element without heap allocation.
    /// </summary>
    public struct ElementEnumerator
    {
        private readonly XmlDocument _document;
        private readonly string? _localNameFilter;
        private readonly int _startIndex;
        private readonly int _endIndex;
        private int _current;

        internal ElementEnumerator(XmlDocument document, int elementIndex, string? localNameFilter)
        {
            _document = document;
            _localNameFilter = localNameFilter;
            ref readonly DbRow row = ref document.GetRow(elementIndex);
            _startIndex = elementIndex + 1 + row.AttributeCount;
            _endIndex = row.EndIndex;
            _current = _startIndex - 1;
        }

        public XmlElement Current => new XmlElement(_document, _current);

        public bool MoveNext()
        {
            while (true)
            {
                if (_current < _startIndex)
                {
                    _current = _startIndex;
                }
                else
                {
                    ref readonly DbRow row = ref _document.GetRow(_current);
                    if (row.NodeType == XmlNodeType.Element)
                    {
                        _current = row.EndIndex;
                    }
                    else
                    {
                        _current++;
                    }
                }

                if (_current >= _endIndex)
                {
                    return false;
                }

                ref readonly DbRow candidate = ref _document.GetRow(_current);
                if (candidate.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (_localNameFilter is null || _document.NameEquals(candidate.NameStart, candidate.NameLength, _localNameFilter))
                {
                    return true;
                }
            }
        }

        public ElementEnumerator GetEnumerator() => this;

        /// <summary>
        /// Returns the count of matching elements (consumes the enumerator).
        /// </summary>
        public int Count()
        {
            int count = 0;
            while (MoveNext()) count++;
            return count;
        }
    }
}
