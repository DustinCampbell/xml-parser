#if !NET
using System;

namespace System
{
    public readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _value = fromEnd ? ~value : value;
        }

        public int Value => _value < 0 ? ~_value : _value;

        public bool IsFromEnd => _value < 0;

        public int GetOffset(int length)
        {
            int offset = _value;
            if (IsFromEnd)
            {
                offset += length + 1;
            }

            return offset;
        }

        public static Index Start => new Index(0);

        public static Index End => new Index(0, fromEnd: true);

        public static Index FromStart(int value) => new Index(value);

        public static Index FromEnd(int value) => new Index(value, fromEnd: true);

        public override bool Equals(object? obj) => obj is Index other && Equals(other);

        public bool Equals(Index other) => _value == other._value;

        public override int GetHashCode() => _value;

        public override string ToString() => IsFromEnd ? "^" + Value.ToString() : Value.ToString();

        public static implicit operator Index(int value) => FromStart(value);
    }

    public readonly struct Range : IEquatable<Range>
    {
        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public Index Start { get; }

        public Index End { get; }

        public static Range All => new Range(Index.Start, Index.End);

        public static Range StartAt(Index start) => new Range(start, Index.End);

        public static Range EndAt(Index end) => new Range(Index.Start, end);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);

            if ((uint)end > (uint)length || (uint)start > (uint)end)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            return (start, end - start);
        }

        public override bool Equals(object? obj) => obj is Range other && Equals(other);

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);

        public override int GetHashCode() => (Start, End).GetHashCode();

        public override string ToString() => Start.ToString() + ".." + End.ToString();
    }
}
#endif
