using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Text.Xml;

/// <summary>
/// Internal flat metadata store for parsed XML documents.
/// Each row represents one node (element, attribute, text, comment, CDATA, PI, declaration).
/// </summary>
internal sealed class MetadataDb
{
    // Fields packed into DbRow for minimal memory:
    // - NodeType (byte)
    // - Flags (byte): HasChildren, HasAttributes, IsEmpty flags
    // - NameStart (int): offset into UTF-8 source for local name
    // - NameLength (ushort): length of local name
    // - PrefixLength (ushort): length of prefix (prefix immediately precedes ":" + localName)
    // - ValueStart (int): offset into UTF-8 source for value/text
    // - ValueLength (int): length of value
    // - NsUriStart (int): offset into UTF-8 source for namespace URI
    // - NsUriLength (ushort): length of namespace URI
    // - AttributeCount (ushort): number of attributes (elements only)
    // - ChildCount (int): number of direct child nodes (elements only)
    // - EndIndex (int): index of the row just past this element's subtree (for skipping)

    private DbRow[] _rows;
    private int _count;

    public MetadataDb(int initialCapacity)
    {
        _rows = new DbRow[initialCapacity];
        _count = 0;
    }

    public int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref DbRow GetRow(int index) => ref _rows[index];

    public ref readonly DbRow GetRowReadOnly(int index) => ref _rows[index];

    public int Append(DbRow row)
    {
        if (_count == _rows.Length)
        {
            Array.Resize(ref _rows, _rows.Length * 2);
        }

        int index = _count++;
        _rows[index] = row;
        return index;
    }

    /// <summary>
    /// Trims the internal array to exact size to minimize retained memory.
    /// </summary>
    public void TrimExcess()
    {
        if (_count < _rows.Length)
        {
            Array.Resize(ref _rows, _count);
        }
    }
}

/// <summary>
/// A single row in the metadata database representing one XML node.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DbRow
{
    /// <summary>The node type.</summary>
    public XmlNodeType NodeType;

    /// <summary>Offset into UTF-8 source for the start of the qualified name (prefix:localName or just localName).</summary>
    public int NameStart;

    /// <summary>Length of the local name portion.</summary>
    public ushort NameLength;

    /// <summary>Length of the prefix portion (0 if no prefix). The prefix starts at NameStart - PrefixLength - 1 (for the colon).</summary>
    public ushort PrefixLength;

    /// <summary>Offset into UTF-8 source for the value (text content, attribute value, comment text, etc.).</summary>
    public int ValueStart;

    /// <summary>Length of the value.</summary>
    public int ValueLength;

    /// <summary>Offset into UTF-8 source for the namespace URI.</summary>
    public int NsUriStart;

    /// <summary>Length of the namespace URI.</summary>
    public ushort NsUriLength;

    /// <summary>Number of attributes (elements only, attributes immediately follow the element row).</summary>
    public ushort AttributeCount;

    /// <summary>Number of direct child nodes, excluding attributes (elements only).</summary>
    public int ChildCount;

    /// <summary>Index of the row after this element's entire subtree (elements only). Used for skipping/navigation.</summary>
    public int EndIndex;

    public static DbRow CreateElement(int nameStart, ushort nameLength, ushort prefixLength, int nsUriStart, ushort nsUriLength)
    {
        return new DbRow
        {
            NodeType = XmlNodeType.Element,
            NameStart = nameStart,
            NameLength = nameLength,
            PrefixLength = prefixLength,
            NsUriStart = nsUriStart,
            NsUriLength = nsUriLength,
        };
    }

    public static DbRow CreateAttribute(int nameStart, ushort nameLength, ushort prefixLength, int valueStart, int valueLength, int nsUriStart, ushort nsUriLength)
    {
        return new DbRow
        {
            NodeType = XmlNodeType.Attribute,
            NameStart = nameStart,
            NameLength = nameLength,
            PrefixLength = prefixLength,
            ValueStart = valueStart,
            ValueLength = valueLength,
            NsUriStart = nsUriStart,
            NsUriLength = nsUriLength,
        };
    }

    public static DbRow CreateText(int valueStart, int valueLength)
    {
        return new DbRow
        {
            NodeType = XmlNodeType.Text,
            ValueStart = valueStart,
            ValueLength = valueLength,
        };
    }

    public static DbRow CreateCData(int valueStart, int valueLength)
    {
        return new DbRow
        {
            NodeType = XmlNodeType.CData,
            ValueStart = valueStart,
            ValueLength = valueLength,
        };
    }

    public static DbRow CreateComment(int valueStart, int valueLength)
    {
        return new DbRow
        {
            NodeType = XmlNodeType.Comment,
            ValueStart = valueStart,
            ValueLength = valueLength,
        };
    }

    public static DbRow CreateProcessingInstruction(int nameStart, ushort nameLength, int valueStart, int valueLength)
    {
        return new DbRow
        {
            NodeType = XmlNodeType.ProcessingInstruction,
            NameStart = nameStart,
            NameLength = nameLength,
            ValueStart = valueStart,
            ValueLength = valueLength,
        };
    }

    public static DbRow CreateDeclaration(int valueStart, int valueLength)
    {
        return new DbRow
        {
            NodeType = XmlNodeType.Declaration,
            ValueStart = valueStart,
            ValueLength = valueLength,
        };
    }
}
