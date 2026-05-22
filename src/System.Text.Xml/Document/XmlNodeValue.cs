namespace System.Text.Xml;

/// <summary>
/// Represents a child node within an <see cref="XmlElement"/> (text, CDATA, comment, PI, or element).
/// </summary>
/// <remarks>
/// <see cref="XmlNodeValue"/> is a lightweight readonly struct for zero-allocation child traversal.
/// </remarks>
public readonly struct XmlNodeValue
{
    private readonly XmlDocument _document;
    private readonly int _index;

    internal XmlNodeValue(XmlDocument document, int index)
    {
        _document = document;
        _index = index;
    }

    /// <summary>
    /// Gets the type of this node.
    /// </summary>
    public XmlNodeType NodeType
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            return row.NodeType;
        }
    }

    /// <summary>
    /// Gets the text value for text, CDATA, and comment nodes. Returns empty string for elements.
    /// </summary>
    public string Value
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            if (row.NodeType == XmlNodeType.Element) return string.Empty;
            return _document.GetDecodedValue(row.ValueStart, row.ValueLength);
        }
    }

    /// <summary>
    /// Gets this node as an <see cref="XmlElement"/>. Only valid when <see cref="NodeType"/> is <see cref="XmlNodeType.Element"/>.
    /// </summary>
    public XmlElement AsElement()
    {
        ref readonly DbRow row = ref _document.GetRow(_index);
        if (row.NodeType != XmlNodeType.Element)
        {
            throw new InvalidOperationException("This node is not an element.");
        }

        return new XmlElement(_document, _index);
    }
}
