using System;

namespace Lucene.Net.Util;

public unsafe class UnmanagedArray<T> : IArray<T> where T : unmanaged
{
    private readonly int _length;
    private readonly UnmanagedStringArray.Type _type;
    private readonly int _elementSize;
    private byte* _ptr;

    public UnmanagedArray(int length, UnmanagedStringArray.Type type)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        _length = length;
        _type = type;
        _elementSize = sizeof(T);

        _ptr = UnmanagedStringArray.Segment.AllocateMemory(_elementSize * _length, type);

        // initialize all elements to default
        AsSpan().Clear();
    }

    public int Length => _length;

    public int TotalManagedAllocations => 0;

    public Span<T> AsSpan()
    {
        return new Span<T>(_ptr, _length);
    }

    public ReadOnlySpan<T> AsSpanReadOnlySpan()
    {
        return new ReadOnlySpan<T>(_ptr, _length);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (_ptr != null)
        {
            UnmanagedStringArray.Segment.FreeMemory(_ptr, _elementSize * _length, _type);
            _ptr = null;
        }
    }

    ~UnmanagedArray()
    {
        Dispose();
    }
}