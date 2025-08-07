using System;
using System.Buffers;

namespace Lucene.Net.Util;

public unsafe class HybridArray<T> : IDisposable where T : unmanaged
{
    // simple threshold: 64KB (guarantees ArrayPool won't exceed 85KB LOH limit)
    private const int LOH_THRESHOLD = 64 * 1024;

    private readonly int _length;
    private readonly UnmanagedStringArray.Type _type;
    private readonly int _elementSize;
    private byte* _ptr;
    private T[] _array;

    public HybridArray(int length, UnmanagedStringArray.Type type, bool clear = false)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        _length = length;
        _type = type;
        _elementSize = sizeof(T);

        if (length * sizeof(T) > LOH_THRESHOLD)
        {
            _ptr = UnmanagedStringArray.Segment.AllocateMemory(_elementSize * _length, type);

            if (clear)
                new Span<T>(_ptr, _length).Clear();
        }
        else
        {
            _array = ArrayPool<T>.Shared.Rent(_length);

            if (clear)
                _array.AsSpan(0, _length).Clear();
        }
    }

    public int Length => _length;

    public int TotalManagedAllocations => _array != null ? _array.Length * _elementSize : 0;

    public ref T this[int index]
    {
        get
        {
            if (index >= _length)
                throw new IndexOutOfRangeException();

            if (_array != null)
                return ref _array[index];

            return ref ((T*)_ptr)[index];
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (_array != null)
        {
            ArrayPool<T>.Shared.Return(_array);
            _array = null;
        }
        else if (_ptr != null)
        {
            UnmanagedStringArray.Segment.FreeMemory(_ptr, _elementSize * _length, _type);
            _ptr = null;
        }
    }

    ~HybridArray()
    {
        Dispose();
    }
}