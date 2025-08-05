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
        _array.AsSpan(0, _length).Clear();
    }

    public int Length => _length;

    public int TotalManagedAllocations => _array.Length * Marshal.SizeOf<T>();

    public ref T this[int index]
    {
        get
        {
            if (index >= _length)
                throw new IndexOutOfRangeException();

            return ref _array[index];
        }
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