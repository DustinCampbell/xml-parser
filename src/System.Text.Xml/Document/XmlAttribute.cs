namespace System.Text.Xml;

/// <summary>
/// Represents an XML attribute within an <see cref="XmlDocument"/>.
/// </summary>
/// <remarks>
/// <see cref="XmlAttribute"/> is a lightweight readonly struct providing zero-allocation
/// access to attribute metadata stored in the parent <see cref="XmlDocument"/>.
/// </remarks>
public readonly struct XmlAttribute
{
    private readonly XmlDocument _document;
    private readonly int _index;

    internal XmlAttribute(XmlDocument document, int index)
    {
        _document = document;
        _index = index;
    }

    /// <summary>
    /// Gets the local name of the attribute.
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
    /// Gets the namespace prefix of the attribute.
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
    /// Gets the namespace URI of the attribute.
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
    /// Gets the attribute value.
    /// </summary>
    public string Value
    {
        get
        {
            ref readonly DbRow row = ref _document.GetRow(_index);
            return _document.GetDecodedValue(row.ValueStart, row.ValueLength);
        }
    }
}
