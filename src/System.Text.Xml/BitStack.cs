namespace System.Text.Xml;

internal struct BitStack
{
    private const int AllocationFreeMaxDepth = sizeof(ulong) * 8;
    private ulong _allocationFreeContainer;
    private bool[]? _array;
    private int _count;

    public readonly int Count => _count;

    public void Push(bool value)
    {
        if (_count < AllocationFreeMaxDepth)
        {
            if (value)
            {
                _allocationFreeContainer |= 1UL << _count;
            }
            else
            {
                _allocationFreeContainer &= ~(1UL << _count);
            }
        }
        else
        {
            _array ??= new bool[AllocationFreeMaxDepth];
            if (_count >= AllocationFreeMaxDepth + _array.Length)
            {
                Array.Resize(ref _array, _array.Length * 2);
            }

            _array[_count - AllocationFreeMaxDepth] = value;
        }

        _count++;
    }

    public bool Pop()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("The stack is empty.");
        }

        bool value = GetAt(_count - 1);
        _count--;
        return value;
    }

    public readonly bool Peek()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("The stack is empty.");
        }

        return GetAt(_count - 1);
    }

    public void SetTop(bool value)
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("The stack is empty.");
        }

        int index = _count - 1;
        if (index < AllocationFreeMaxDepth)
        {
            if (value)
            {
                _allocationFreeContainer |= 1UL << index;
            }
            else
            {
                _allocationFreeContainer &= ~(1UL << index);
            }
        }
        else
        {
            _array![index - AllocationFreeMaxDepth] = value;
        }
    }

    private readonly bool GetAt(int index)
    {
        if (index < AllocationFreeMaxDepth)
        {
            return ((_allocationFreeContainer >> index) & 1UL) != 0;
        }

        return _array![index - AllocationFreeMaxDepth];
    }
}