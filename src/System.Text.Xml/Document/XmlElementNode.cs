using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace System.Text.Xml;

/// <summary>
/// Represents an XML element node.
/// </summary>
/// <remarks>
/// An <see cref="XmlElementNode"/> contains a qualified name, zero or more attributes, and zero or more child nodes.
/// </remarks>
public sealed class XmlElementNode : XmlNode
{
    private XmlAttributeNode[]? _attributes;
    private int _attributeCount;
    private XmlNode[]? _children;
    private int _childCount;
    private string? _directText; // Fast path: stores text content directly without child array

    /// <summary>
    /// Initializes a new <see cref="XmlElementNode"/> instance.
    /// </summary>
    /// <param name="name">The qualified element name.</param>
    /// <param name="attributes">The optional attribute collection.</param>
    /// <param name="children">The optional child node collection.</param>
    public XmlElementNode(XmlName name, IEnumerable<XmlAttributeNode>? attributes = null, IEnumerable<XmlNode>? children = null)
        : base(XmlNodeType.Element)
    {
        XmlNameAccessor.GetParts(name, out var localName, out var prefix, out var namespaceUri);
        LocalName = localName;
        Prefix = prefix;
        NamespaceUri = namespaceUri;

        if (attributes is not null)
        {
            foreach (var attribute in attributes)
            {
                SetAttribute(attribute);
            }
        }

        if (children is not null)
        {
            foreach (var child in children)
            {
                AddChild(child);
            }
        }
    }

    internal XmlElementNode(string localName, string? prefix, string? namespaceUri, XmlAttributeNode[]? attributes = null, IEnumerable<XmlNode>? children = null)
        : base(XmlNodeType.Element)
    {
        LocalName = localName;
        Prefix = prefix ?? string.Empty;
        NamespaceUri = namespaceUri ?? string.Empty;

        if (attributes is not null)
        {
            _attributes = attributes;
            _attributeCount = attributes.Length;
            for (int i = 0; i < _attributeCount; i++)
            {
                attributes[i].SetParent(this);
            }
        }

        if (children is not null)
        {
            foreach (var child in children)
            {
                AddChild(child);
            }
        }
    }

    /// <summary>
    /// Creates a mutable <see cref="XmlElementNode"/> from a read-only <see cref="XmlElement"/>.
    /// </summary>
    /// <param name="element">The read-only element to convert.</param>
    /// <returns>A new mutable element tree with the same structure and trivia.</returns>
    /// <remarks>
    /// <para>
    /// This is analogous to <c>JsonObject.Create(JsonElement)</c> in System.Text.Json.
    /// It performs a deep copy, converting the entire element subtree including attributes,
    /// child nodes, and trivia (when the source document was parsed with
    /// <see cref="XmlDocumentOptions.PreserveTrivia"/>).
    /// </para>
    /// </remarks>
    public static XmlElementNode Create(XmlElement element)
    {
        var elementNode = new XmlElementNode(
            element.LocalName,
            XmlDomConverter.NullIfEmpty(element.Prefix),
            XmlDomConverter.NullIfEmpty(element.NamespaceUri));

        // Convert attributes
        var attrs = element.EnumerateAttributes();
        while (attrs.MoveNext())
        {
            var attr = attrs.Current;
            elementNode.SetAttribute(new XmlAttributeNode(
                attr.LocalName,
                XmlDomConverter.NullIfEmpty(attr.Prefix),
                XmlDomConverter.NullIfEmpty(attr.NamespaceUri),
                attr.Value));
        }

        // Convert children
        var children = element.EnumerateChildren();
        while (children.MoveNext())
        {
            var childNode = XmlDomConverter.ConvertChild(children.Current);
            if (childNode is not null)
            {
                elementNode.AddChild(childNode);
            }
        }

        // Copy trivia if available
        XmlDomConverter.CopyTrivia(element, elementNode);

        return elementNode;
    }

    /// <summary>
    /// Converts this mutable element to a read-only <see cref="XmlDocument"/>.
    /// </summary>
    /// <param name="options">Optional document options for the resulting document.</param>
    /// <returns>A new read-only document with this element as the root.</returns>
    /// <remarks>
    /// <para>
    /// This serializes the mutable DOM to XML and re-parses it into the efficient
    /// read-only representation. Use this when you are done modifying and want to
    /// return to the high-performance struct-based DOM.
    /// </para>
    /// </remarks>
    public XmlDocument ToDocument(XmlDocumentOptions? options = null)
    {
        string xml = ToString();
        return options is not null
            ? XmlDocument.Parse(xml, options)
            : XmlDocument.Parse(xml);
    }

    /// <summary>
    /// Sets the children array directly from the parser (exact-sized, no copies).
    /// </summary>
    internal void SetChildrenInternal(XmlNode[] children, int count)
    {
        _children = children;
        _childCount = count;
        for (int i = 0; i < count; i++)
        {
            children[i].SetParent(this);
        }
    }

    /// <summary>
    /// Gets the local element name.
    /// </summary>
    public string LocalName { get; }

    /// <summary>
    /// Gets the namespace prefix associated with the element.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets the namespace URI associated with the element.
    /// </summary>
    public string NamespaceUri { get; }

    /// <summary>
    /// Gets the qualified element name.
    /// </summary>
    public XmlName Name => new XmlName(LocalName, Prefix, NamespaceUri);

    /// <summary>
    /// Gets the attributes declared on the element.
    /// </summary>
    public IReadOnlyList<XmlAttributeNode> Attributes => new ArraySlice<XmlAttributeNode>(_attributes, _attributeCount);

    /// <summary>
    /// Gets the child nodes contained by the element.
    /// </summary>
    public IReadOnlyList<XmlNode> Children
    {
        get
        {
            if (_directText is not null && _childCount == 0)
            {
                // Materialize the text node on first access
                EnsureChildCapacity(1);
                _children![0] = new XmlTextNode(_directText);
                _children[0].SetParent(this);
                _childCount = 1;
                _directText = null;
            }

            return new ArraySlice<XmlNode>(_children, _childCount);
        }
    }

    /// <summary>
    /// Gets the concatenated text content of the element and all descendant text-bearing nodes.
    /// </summary>
    public string InnerText
    {
        get
        {
            if (_directText is not null && _childCount == 0)
            {
                return _directText;
            }

            var builder = new StringBuilder();
            AppendInnerText(this, builder);
            return builder.ToString();
        }
    }

    /// <summary>
    /// Returns the first attribute with the specified local name and namespace URI.
    /// </summary>
    /// <param name="localName">The local attribute name to search for.</param>
    /// <param name="ns">The optional namespace URI to match.</param>
    /// <returns>The matching attribute, or <see langword="null"/> if none is found.</returns>
    public XmlAttributeNode? GetAttribute(string localName, string? ns = null)
    {
        ThrowHelper.ThrowIfNullOrEmpty(localName);

        int index = FindAttributeIndex(localName, ns ?? string.Empty);
        return index >= 0 ? _attributes![index] : null;
    }

    /// <summary>
    /// Returns the child elements of the current element.
    /// </summary>
    public IEnumerable<XmlElementNode> Elements()
    {
        if (_children is null)
        {
            yield break;
        }

        for (int i = 0; i < _childCount; i++)
        {
            if (_children[i] is XmlElementNode element)
            {
                yield return element;
            }
        }
    }

    /// <summary>
    /// Returns the child elements whose local name matches <paramref name="localName"/>.
    /// </summary>
    /// <param name="localName">The local name to match.</param>
    public IEnumerable<XmlElementNode> Elements(string localName)
    {
        ThrowHelper.ThrowIfNullOrEmpty(localName);

        if (_children is null)
        {
            yield break;
        }

        for (int i = 0; i < _childCount; i++)
        {
            if (_children[i] is XmlElementNode element && string.Equals(element.LocalName, localName, StringComparison.Ordinal))
            {
                yield return element;
            }
        }
    }

    /// <summary>
    /// Returns the descendant elements of the current element in document order.
    /// </summary>
    public IEnumerable<XmlElementNode> Descendants()
    {
        if (_children is null)
        {
            yield break;
        }

        for (int i = 0; i < _childCount; i++)
        {
            if (_children[i] is not XmlElementNode child)
            {
                continue;
            }

            yield return child;

            foreach (var descendant in child.Descendants())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Appends a child node to the element.
    /// </summary>
    /// <param name="child">The child node to add.</param>
    public void AddChild(XmlNode child)
    {
        ThrowHelper.ThrowIfNull(child);

        if (child.NodeType == XmlNodeType.Attribute)
        {
            throw new InvalidOperationException("Attributes must be added through SetAttribute.");
        }

        if (child.Parent is not null && !ReferenceEquals(child.Parent, this))
        {
            throw new InvalidOperationException("The node already belongs to a different element.");
        }

        EnsureChildCapacity(_childCount + 1);
        child.SetParent(this);
        _children![_childCount++] = child;
    }

    /// <summary>
    /// Fast internal add for DOM parsing - skips validation.
    /// </summary>
    internal void AddChildInternal(XmlNode child)
    {
        if (_directText is not null)
        {
            // Materialize the direct text as a child node first
            EnsureChildCapacity(2);
            var textNode = new XmlTextNode(_directText);
            textNode.SetParent(this);
            _children![0] = textNode;
            _childCount = 1;
            _directText = null;
        }
        else
        {
            EnsureChildCapacity(_childCount + 1);
        }

        child.SetParent(this);
        _children![_childCount++] = child;
    }

    /// <summary>
    /// Sets direct text content without creating a child node (internal fast path for parsing).
    /// </summary>
    internal void SetDirectText(string text)
    {
        if (_childCount > 0 || _directText is not null)
        {
            // Fall back to normal child addition
            AddChildInternal(new XmlTextNode(text));
            return;
        }

        _directText = text;
    }

    /// <summary>
    /// Returns true if the element has direct text set (used during parsing).
    /// </summary>
    internal bool HasDirectText => _directText is not null && _childCount == 0;

    /// <summary>
    /// Consumes and returns the direct text, clearing it from the element.
    /// </summary>
    internal string? ConsumeDirectText()
    {
        var text = _directText;
        _directText = null;
        return text;
    }

    /// <summary>
    /// Removes the specified child node from the element.
    /// </summary>
    /// <param name="child">The child node to remove.</param>
    /// <returns><see langword="true"/> if the node was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveChild(XmlNode child)
    {
        ThrowHelper.ThrowIfNull(child);

        if (_children is null)
        {
            return false;
        }

        for (int i = 0; i < _childCount; i++)
        {
            if (!ReferenceEquals(_children[i], child))
            {
                continue;
            }

            int remaining = _childCount - i - 1;
            if (remaining > 0)
            {
                Array.Copy(_children, i + 1, _children, i, remaining);
            }

            _children[--_childCount] = null!;
            child.SetParent(null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Adds or replaces an attribute on the current element.
    /// </summary>
    /// <param name="attribute">The attribute to add or replace.</param>
    public void SetAttribute(XmlAttributeNode attribute)
    {
        ThrowHelper.ThrowIfNull(attribute);

        if (attribute.Parent is not null && !ReferenceEquals(attribute.Parent, this))
        {
            throw new InvalidOperationException("The attribute already belongs to a different element.");
        }

        string namespaceUri = attribute.NamespaceUri;
        int existingIndex = FindAttributeIndex(attribute.LocalName, namespaceUri);
        if (existingIndex >= 0)
        {
            _attributes![existingIndex].SetParent(null);
            _attributes[existingIndex] = attribute;
            attribute.SetParent(this);
            return;
        }

        EnsureAttributeCapacity(_attributeCount + 1);
        attribute.SetParent(this);
        _attributes![_attributeCount++] = attribute;
    }

    /// <summary>
    /// Removes the first attribute that matches the specified local name and namespace URI.
    /// </summary>
    /// <param name="localName">The local attribute name to remove.</param>
    /// <param name="ns">The optional namespace URI to match.</param>
    /// <returns><see langword="true"/> if an attribute was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveAttribute(string localName, string? ns = null)
    {
        ThrowHelper.ThrowIfNullOrEmpty(localName);

        int index = FindAttributeIndex(localName, ns ?? string.Empty);
        if (index < 0)
        {
            return false;
        }

        XmlAttributeNode attribute = _attributes![index];
        int remaining = _attributeCount - index - 1;
        if (remaining > 0)
        {
            Array.Copy(_attributes, index + 1, _attributes, index, remaining);
        }

        _attributes[--_attributeCount] = null!;
        attribute.SetParent(null);
        return true;
    }

    /// <inheritdoc />
    public override void WriteTo(Utf8XmlWriter writer)
    {
        ThrowHelper.ThrowIfNull(writer);
        WriteTo(new Utf8XmlNodeWriter(writer));
    }

    internal override void WriteTo(XmlNodeWriter writer)
    {
        writer.WriteStartElement(Prefix, LocalName, NamespaceUri);

        for (int i = 0; i < _attributeCount; i++)
        {
            _attributes![i].WriteTo(writer);
        }

        if (_directText is not null && _childCount == 0)
        {
            writer.WriteString(_directText);
        }
        else
        {
            for (int i = 0; i < _childCount; i++)
            {
                _children![i].WriteTo(writer);
            }
        }

        writer.WriteEndElement();
    }

    private static void AppendInnerText(XmlElementNode element, StringBuilder builder)
    {
        if (element._directText is not null && element._childCount == 0)
        {
            builder.Append(element._directText);
            return;
        }

        if (element._children is null)
        {
            return;
        }

        for (int i = 0; i < element._childCount; i++)
        {
            switch (element._children[i])
            {
                case XmlTextNode text:
                    builder.Append(text.Value);
                    break;
                case XmlCDataNode cdata:
                    builder.Append(cdata.Value);
                    break;
                case XmlElementNode childElement:
                    AppendInnerText(childElement, builder);
                    break;
            }
        }
    }

    private int FindAttributeIndex(string localName, string namespaceUri)
    {
        if (_attributes is null)
        {
            return -1;
        }

        for (int i = 0; i < _attributeCount; i++)
        {
            XmlAttributeNode attribute = _attributes[i];
            if (string.Equals(attribute.LocalName, localName, StringComparison.Ordinal) &&
                string.Equals(attribute.NamespaceUri, namespaceUri, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void InitializeAttributes(IEnumerable<XmlAttributeNode>? attributes)
    {
        if (attributes is null)
        {
            return;
        }

        switch (attributes)
        {
            case List<XmlAttributeNode> list when list.Count > 0:
                _attributes = new XmlAttributeNode[list.Count];
                list.CopyTo(_attributes, 0);
                _attributeCount = list.Count;
                break;
            case XmlAttributeNode[] array when array.Length > 0:
                _attributes = new XmlAttributeNode[array.Length];
                array.AsSpan().CopyTo(_attributes);
                _attributeCount = array.Length;
                break;
            case ICollection<XmlAttributeNode> collection when collection.Count > 0:
                _attributes = new XmlAttributeNode[collection.Count];
                foreach (var attribute in collection)
                {
                    _attributes[_attributeCount++] = attribute;
                }

                break;
            default:
                foreach (var attribute in attributes)
                {
                    EnsureAttributeCapacity(_attributeCount + 1);
                    _attributes![_attributeCount++] = attribute;
                }

                break;
        }

        for (int i = 0; i < _attributeCount; i++)
        {
            _attributes![i].SetParent(this);
        }
    }

    private void EnsureAttributeCapacity(int required)
    {
        if (_attributes is not null && _attributes.Length >= required)
        {
            return;
        }

        int newCapacity = _attributes is null ? 4 : _attributes.Length * 2;
        if (newCapacity < required)
        {
            newCapacity = required;
        }

        Array.Resize(ref _attributes, newCapacity);
    }

    private void EnsureChildCapacity(int required)
    {
        if (_children is not null && _children.Length >= required)
        {
            return;
        }

        int newCapacity = _children is null ? 4 : _children.Length * 2;
        if (newCapacity < required)
        {
            newCapacity = required;
        }

        Array.Resize(ref _children, newCapacity);
    }
}

internal readonly struct ArraySlice<T> : IReadOnlyList<T>
{
    private readonly T[]? _array;
    private readonly int _count;

    public ArraySlice(T[]? array, int count)
    {
        _array = array;
        _count = count;
    }

    public int Count => _count;

    public T this[int index]
        => (uint)index < (uint)_count ? _array![index] : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<T> GetEnumerator()
    {
        if (_array is null)
        {
            yield break;
        }

        for (int i = 0; i < _count; i++)
        {
            yield return _array[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
