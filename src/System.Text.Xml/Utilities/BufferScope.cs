// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Text.Xml;

/// <summary>
///  Allows renting a buffer from <see cref="ArrayPool{T}"/> with a using statement. Can be used directly as if it
///  were a <see cref="Span{T}"/>.
/// </summary>
internal ref struct BufferScope<T>
{
    private T[]? _array;
    private Span<T> _span;

    /// <summary>
    ///  Initializes a new instance of the <see cref="BufferScope{T}"/> with the specified minimum length.
    /// </summary>
    public BufferScope(int minimumLength)
    {
        _array = ArrayPool<T>.Shared.Rent(minimumLength);
        _span = _array;
    }

    /// <summary>
    ///  Create the <see cref="BufferScope{T}"/> with an initial buffer. Useful for creating with an initial stack
    ///  allocated buffer.
    /// </summary>
    public BufferScope(Span<T> initialBuffer)
    {
        _array = null;
        _span = initialBuffer;
    }

    /// <summary>
    ///  Create the <see cref="BufferScope{T}"/> with an initial buffer, renting from pool if not large enough.
    /// </summary>
    public BufferScope(Span<T> initialBuffer, int minimumLength)
    {
        if (initialBuffer.Length >= minimumLength)
        {
            _array = null;
            _span = initialBuffer;
        }
        else
        {
            _array = ArrayPool<T>.Shared.Rent(minimumLength);
            _span = _array;
        }
    }

    /// <summary>
    ///  Ensure that the buffer has enough space for <paramref name="capacity"/> number of elements.
    /// </summary>
    public void EnsureCapacity(int capacity, bool copy = false)
    {
        if (_span.Length >= capacity)
        {
            return;
        }

        IncreaseCapacity(capacity, copy);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void IncreaseCapacity(int capacity, bool copy)
    {
        Debug.Assert(capacity > _span.Length);

        T[] newArray = ArrayPool<T>.Shared.Rent(capacity);
        if (copy)
        {
            _span.CopyTo(newArray);
        }

        if (_array is not null)
        {
            ArrayPool<T>.Shared.Return(_array, clearArray: TypeInfo<T>.IsReferenceOrContainsReferences());
        }

        _array = newArray;
        _span = _array;
    }

    public ref T this[int i]
        => ref _span[i];

    public readonly Span<T> Slice(int start, int length)
        => _span.Slice(start, length);

    public readonly int Length => _span.Length;

    public readonly Span<T> AsSpan()
        => _span;

    public static implicit operator Span<T>(BufferScope<T> scope)
        => scope._span;

    public static implicit operator ReadOnlySpan<T>(BufferScope<T> scope)
        => scope._span;

    public void Dispose()
    {
        _span = default;

        if (_array is not null)
        {
            ArrayPool<T>.Shared.Return(_array, clearArray: TypeInfo<T>.IsReferenceOrContainsReferences());
        }

        _array = null;
    }
}
