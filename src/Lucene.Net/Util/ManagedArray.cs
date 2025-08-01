using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Lucene.Net.Util;

public class ManagedArray<T> : IArray<T> where T : unmanaged
{
    private readonly int _length;
    private T[] _array;

    public ManagedArray(int length)
    {
        _length = length;
        _array = ArrayPool<T>.Shared.Rent(_length);
        _array.AsSpan().Clear();
    }

    public int Length => _length;

    public int TotalManagedAllocations => _array.Length * Marshal.SizeOf<T>();

    public Span<T> AsSpan()
    {
        return _array.AsSpan(0, _length);
    }

    public ReadOnlySpan<T> AsSpanReadOnlySpan()
    {
        return _array.AsSpan(0, _length);
    }

    public void Dispose()
    {
        if (_array != null)
        {
            ArrayPool<T>.Shared.Return(_array);
            _array = null;
        }
    }
}