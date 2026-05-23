using System.Collections.Generic;

namespace System.Text.Xml;

/// <summary>
/// A list of <see cref="XmlTrivia"/> items associated with a node.
/// Supports zero-allocation enumeration via a struct enumerator.
/// </summary>
public readonly struct XmlTriviaList
{
    private readonly XmlDocument? _document;
    private readonly int _startIndex;
    private readonly int _count;

    internal XmlTriviaList(XmlDocument document, int startIndex, int count)
    {
        _document = document;
        _startIndex = startIndex;
        _count = count;
    }

    /// <summary>
    /// Gets the number of trivia items in the list.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Returns true if this list has no trivia items.
    /// </summary>
    public bool IsEmpty => _count == 0;

    /// <summary>
    /// Gets the trivia at the specified index.
    /// </summary>
    public XmlTrivia this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _document!.GetTrivia(_startIndex + index);
        }
    }

    /// <summary>
    /// Returns a struct enumerator for zero-allocation iteration.
    /// </summary>
    public Enumerator GetEnumerator() => new Enumerator(_document!, _startIndex, _count);

    /// <summary>
    /// Converts to a list (allocating).
    /// </summary>
    public List<XmlTrivia> ToList()
    {
        var list = new List<XmlTrivia>(_count);
        for (int i = 0; i < _count; i++)
        {
            list.Add(_document!.GetTrivia(_startIndex + i));
        }
        return list;
    }

    /// <summary>
    /// Zero-allocation enumerator for trivia items.
    /// </summary>
    public struct Enumerator
    {
        private readonly XmlDocument _document;
        private readonly int _start;
        private readonly int _count;
        private int _index;

        internal Enumerator(XmlDocument document, int start, int count)
        {
            _document = document;
            _start = start;
            _count = count;
            _index = -1;
        }

        public XmlTrivia Current => _document.GetTrivia(_start + _index);

        public bool MoveNext()
        {
            _index++;
            return _index < _count;
        }
    }
}
