using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Text.Xml;

/// <summary>
/// Stores trivia (whitespace, comments) associated with nodes in a parsed document.
/// Each node can have leading trivia (before it) and trailing trivia (after it).
/// </summary>
internal sealed class TriviaDb
{
    private TriviaEntry[] _entries;
    private int _count;

    // Maps node index → (leadingStart, leadingCount, trailingStart, trailingCount)
    private NodeTriviaRef[] _nodeRefs;

    public TriviaDb(int nodeCapacity, int triviaCapacity)
    {
        _entries = ArrayPool<TriviaEntry>.Shared.Rent(triviaCapacity);
        _nodeRefs = new NodeTriviaRef[nodeCapacity];
        _count = 0;
    }

    public int Count => _count;

    /// <summary>
    /// Gets the trivia entry at the specified index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly TriviaEntry GetEntry(int index) => ref _entries[index];

    /// <summary>
    /// Gets the leading trivia range for the specified node.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetLeadingTrivia(int nodeIndex, out int start, out int count)
    {
        if (nodeIndex < _nodeRefs.Length)
        {
            start = _nodeRefs[nodeIndex].LeadingStart;
            count = _nodeRefs[nodeIndex].LeadingCount;
        }
        else
        {
            start = 0;
            count = 0;
        }
    }

    /// <summary>
    /// Gets the trailing trivia range for the specified node.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetTrailingTrivia(int nodeIndex, out int start, out int count)
    {
        if (nodeIndex < _nodeRefs.Length)
        {
            start = _nodeRefs[nodeIndex].TrailingStart;
            count = _nodeRefs[nodeIndex].TrailingCount;
        }
        else
        {
            start = 0;
            count = 0;
        }
    }

    /// <summary>
    /// Marks the start of leading trivia collection for a node.
    /// Call before appending the node's leading trivia entries.
    /// </summary>
    public void BeginLeadingTrivia(int nodeIndex)
    {
        EnsureNodeCapacity(nodeIndex);
        _nodeRefs[nodeIndex].LeadingStart = _count;
        _nodeRefs[nodeIndex].LeadingCount = 0;
    }

    /// <summary>
    /// Marks the start of trailing trivia collection for a node.
    /// Call before appending the node's trailing trivia entries.
    /// </summary>
    public void BeginTrailingTrivia(int nodeIndex)
    {
        EnsureNodeCapacity(nodeIndex);
        _nodeRefs[nodeIndex].TrailingStart = _count;
        _nodeRefs[nodeIndex].TrailingCount = 0;
    }

    /// <summary>
    /// Appends a trivia entry and increments the leading count for the given node.
    /// </summary>
    public void AppendLeading(int nodeIndex, TriviaEntry entry)
    {
        Append(entry);
        _nodeRefs[nodeIndex].LeadingCount++;
    }

    /// <summary>
    /// Appends a trivia entry and increments the trailing count for the given node.
    /// </summary>
    public void AppendTrailing(int nodeIndex, TriviaEntry entry)
    {
        Append(entry);
        _nodeRefs[nodeIndex].TrailingCount++;
    }

    private void Append(TriviaEntry entry)
    {
        if (_count == _entries.Length)
        {
            var newArray = ArrayPool<TriviaEntry>.Shared.Rent(_entries.Length * 2);
            Array.Copy(_entries, newArray, _count);
            ArrayPool<TriviaEntry>.Shared.Return(_entries, clearArray: false);
            _entries = newArray;
        }
        _entries[_count++] = entry;
    }

    private void EnsureNodeCapacity(int nodeIndex)
    {
        if (nodeIndex >= _nodeRefs.Length)
        {
            Array.Resize(ref _nodeRefs, Math.Max(_nodeRefs.Length * 2, nodeIndex + 1));
        }
    }

    /// <summary>
    /// Compacts this trivia database into a final owned form.
    /// Returns this instance with compacted arrays.
    /// </summary>
    public TriviaDb Compact()
    {
        var finalEntries = new TriviaEntry[_count];
        Array.Copy(_entries, finalEntries, _count);
        ArrayPool<TriviaEntry>.Shared.Return(_entries, clearArray: false);
        _entries = finalEntries;
        return this;
    }
}

/// <summary>
/// A single trivia entry in the trivia database.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TriviaEntry
{
    /// <summary>Kind of trivia.</summary>
    public XmlTriviaKind Kind;

    /// <summary>Offset into the UTF-8 source for the trivia content.</summary>
    public int Start;

    /// <summary>Length of the trivia content in the source.</summary>
    public int Length;
}

/// <summary>
/// Maps a node to its trivia ranges in the TriviaDb entries array.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NodeTriviaRef
{
    public int LeadingStart;
    public ushort LeadingCount;
    public int TrailingStart;
    public ushort TrailingCount;
}
