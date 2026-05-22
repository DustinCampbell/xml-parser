using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly List<XmlAttributeNode> _attributes;
    private readonly List<XmlNode> _children;
    private readonly XmlName _name;

    /// <summary>
    /// Initializes a new <see cref="XmlElementNode"/> instance.
    /// </summary>
    /// <param name="name">The qualified element name.</param>
    /// <param name="attributes">The optional attribute collection.</param>
    /// <param name="children">The optional child node collection.</param>
    public XmlElementNode(XmlName name, IEnumerable<XmlAttributeNode>? attributes = null, IEnumerable<XmlNode>? children = null)
        : base(XmlNodeType.Element)
    {
        _name = name;
        XmlNameAccessor.GetParts(name, out var localName, out var prefix, out var namespaceUri);
        LocalName = localName;
        Prefix = prefix;
        NamespaceUri = namespaceUri;
        _attributes = new List<XmlAttributeNode>();
        _children = new List<XmlNode>();

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

    internal XmlElementNode(string localName, string? prefix, string? namespaceUri, IEnumerable<XmlAttributeNode>? attributes = null, IEnumerable<XmlNode>? children = null)
        : this(XmlNameAccessor.Create(localName, prefix, namespaceUri), attributes, children)
    {
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
    public XmlName Name => _name;

    /// <summary>
    /// Gets the attributes declared on the element.
    /// </summary>
    public IReadOnlyList<XmlAttributeNode> Attributes => _attributes;

    /// <summary>
    /// Gets the child nodes contained by the element.
    /// </summary>
    public IReadOnlyList<XmlNode> Children => _children;

    /// <summary>
    /// Gets the concatenated text content of the element and all descendant text-bearing nodes.
    /// </summary>
    public string InnerText
    {
        get
        {
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
        ArgumentException.ThrowIfNullOrEmpty(localName);
        return _attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.LocalName, localName, StringComparison.Ordinal) &&
            string.Equals(attribute.NamespaceUri, ns ?? string.Empty, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns the child elements of the current element.
    /// </summary>
    public IEnumerable<XmlElementNode> Elements() => _children.OfType<XmlElementNode>();

    /// <summary>
    /// Returns the child elements whose local name matches <paramref name="localName"/>.
    /// </summary>
    /// <param name="localName">The local name to match.</param>
    public IEnumerable<XmlElementNode> Elements(string localName)
    {
        ArgumentException.ThrowIfNullOrEmpty(localName);
        return Elements().Where(element => string.Equals(element.LocalName, localName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns the descendant elements of the current element in document order.
    /// </summary>
    public IEnumerable<XmlElementNode> Descendants()
    {
        foreach (var child in _children.OfType<XmlElementNode>())
        {
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
        ArgumentNullException.ThrowIfNull(child);

        if (child.NodeType == XmlNodeType.Attribute)
        {
            throw new InvalidOperationException("Attributes must be added through SetAttribute.");
        }

        if (child.Parent is not null && !ReferenceEquals(child.Parent, this))
        {
            throw new InvalidOperationException("The node already belongs to a different element.");
        }

        child.SetParent(this);
        _children.Add(child);
    }

    /// <summary>
    /// Removes the specified child node from the element.
    /// </summary>
    /// <param name="child">The child node to remove.</param>
    /// <returns><see langword="true"/> if the node was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveChild(XmlNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (_children.Remove(child))
        {
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
        ArgumentNullException.ThrowIfNull(attribute);

        if (attribute.Parent is not null && !ReferenceEquals(attribute.Parent, this))
        {
            throw new InvalidOperationException("The attribute already belongs to a different element.");
        }

        var existing = GetAttribute(attribute.LocalName, attribute.NamespaceUri);
        if (existing is not null)
        {
            existing.SetParent(null);
            _attributes.Remove(existing);
        }

        attribute.SetParent(this);
        _attributes.Add(attribute);
    }

    /// <summary>
    /// Removes the first attribute that matches the specified local name and namespace URI.
    /// </summary>
    /// <param name="localName">The local attribute name to remove.</param>
    /// <param name="ns">The optional namespace URI to match.</param>
    /// <returns><see langword="true"/> if an attribute was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveAttribute(string localName, string? ns = null)
    {
        var attribute = GetAttribute(localName, ns);
        if (attribute is null)
        {
            return false;
        }

        attribute.SetParent(null);
        _attributes.Remove(attribute);
        return true;
    }

    /// <inheritdoc />
    public override void WriteTo(Utf8XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteTo(new Utf8XmlNodeWriter(writer));
    }

    internal override void WriteTo(XmlNodeWriter writer)
    {
        writer.WriteStartElement(Prefix, LocalName, NamespaceUri);

        foreach (var attribute in _attributes)
        {
            attribute.WriteTo(writer);
        }

        foreach (var child in _children)
        {
            child.WriteTo(writer);
        }

        writer.WriteEndElement();
    }

    private static void AppendInnerText(XmlElementNode element, StringBuilder builder)
    {
        foreach (var child in element.Children)
        {
            switch (child)
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
}