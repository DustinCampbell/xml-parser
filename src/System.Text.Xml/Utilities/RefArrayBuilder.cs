// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Adapted from https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ValueListBuilder.cs

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Text.Xml;

/// <summary>
///  A ref struct builder for arrays that uses pooled memory for efficient allocation.
///  This builder automatically grows as needed and returns memory to the pool when disposed.
/// </summary>
internal ref struct RefArrayBuilder<T>
{
    private BufferScope<T> _scope;
    private int _count;

    /// <summary>
    ///  Initializes a new instance with the specified initial capacity.
    /// </summary>
    public RefArrayBuilder(int initialCapacity)
    {
        _scope = new BufferScope<T>(initialCapacity);
    }

    /// <summary>
    ///  Initializes a new instance with the specified scratch buffer (typically stack-allocated).
    /// </summary>
    public RefArrayBuilder(Span<T> scratchBuffer)
    {
        _scope = new BufferScope<T>(scratchBuffer);
    }

    /// <summary>
    ///  Releases the pooled array back to the shared pool.
    /// </summary>
    public void Dispose()
    {
        _scope.Dispose();
    }

    /// <summary>
    ///  Gets the current capacity of the builder.
    /// </summary>
    public readonly int Capacity => _scope.Length;

    /// <summary>
    ///  Gets a value indicating whether the builder contains no elements.
    /// </summary>
    public readonly bool IsEmpty => _count == 0;

    /// <summary>
    ///  Gets or sets the number of elements in the builder.
    /// </summary>
    public int Count
    {
        readonly get => _count;
        set
        {
            Debug.Assert(value >= 0);
            Debug.Assert(value <= _scope.Length);
            _count = value;
        }
    }

    /// <summary>
    ///  Gets a reference to the element at the specified index.
    /// </summary>
    public ref T this[int index]
    {
        get
        {
            Debug.Assert(index < _count);
            return ref _scope[index];
        }
    }

    /// <summary>
    ///  Returns a reference to this builder, allowing it to be passed by ref
    ///  even when declared in a using statement.
    /// </summary>
    [UnscopedRef]
    public ref RefArrayBuilder<T> AsRef() => ref this;

    /// <summary>
    ///  Returns a <see cref="Span{T}"/> view of the elements in the builder.
    /// </summary>
    public readonly Span<T> AsSpan()
        => _scope.AsSpan()[.._count];

    /// <summary>
    ///  Adds an item to the end of the builder. The builder will automatically grow if needed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        int count = _count;
        Span<T> span = _scope;

        if ((uint)count < (uint)span.Length)
        {
            span[count] = item;
            _count = count + 1;
        }
        else
        {
            AddWithResize(item);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddWithResize(T item)
    {
        Debug.Assert(_count == _scope.Length);

        int count = _count;
        Grow(1);
        _scope[count] = item;
        _count = count + 1;
    }

    /// <summary>
    ///  Creates an array containing a copy of the elements in the builder.
    /// </summary>
    public readonly T[] ToArray()
        => AsSpan().ToArray();

    /// <summary>
    ///  Creates an array containing a copy of the elements, or returns null if the builder is empty.
    /// </summary>
    public readonly T[]? ToArrayOrNull()
        => _count == 0 ? null : AsSpan().ToArray();

    private void Grow(int size)
    {
        const int ArrayMaxLength = 0x7FFFFFC7;

        Span<T> span = _scope;
        int nextCapacity = Math.Max(
            val1: span.Length != 0 ? span.Length * 2 : 4,
            val2: span.Length + size);

        if ((uint)nextCapacity > ArrayMaxLength)
        {
            nextCapacity = Math.Max(Math.Max(span.Length + 1, ArrayMaxLength), span.Length);
        }

        _scope.EnsureCapacity(nextCapacity, copy: true);
    }
}
