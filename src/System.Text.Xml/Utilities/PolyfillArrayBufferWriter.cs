#if !NET
using System;

namespace System.Buffers
{
    internal sealed class ArrayBufferWriter<T> : IBufferWriter<T>
    {
        private const int DefaultInitialBufferSize = 256;
        private T[] _buffer;
        private int _index;

        public ArrayBufferWriter(int initialCapacity = DefaultInitialBufferSize)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _buffer = initialCapacity == 0 ? Array.Empty<T>() : new T[initialCapacity];
        }

        public int WrittenCount => _index;

        public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, _index);

        public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, _index);

        public void Advance(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (_index > _buffer.Length - count)
            {
                throw new InvalidOperationException("Cannot advance past the end of the buffer.");
            }

            _index += count;
        }

        public Memory<T> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsMemory(_index);
        }

        public Span<T> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsSpan(_index);
        }

        public void Clear()
        {
            if (_index > 0)
            {
                Array.Clear(_buffer, 0, _index);
                _index = 0;
            }
        }

        private void CheckAndResizeBuffer(int sizeHint)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }

            if (sizeHint == 0)
            {
                sizeHint = 1;
            }

            int availableSpace = _buffer.Length - _index;
            if (sizeHint <= availableSpace)
            {
                return;
            }

            int growBy = Math.Max(sizeHint, _buffer.Length == 0 ? DefaultInitialBufferSize : _buffer.Length);
            int newSize = checked(_buffer.Length + growBy);
            Array.Resize(ref _buffer, newSize);
        }
    }
}
#endif
