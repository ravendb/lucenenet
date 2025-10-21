using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucene.Net.Util;

public sealed class ManagedArray<T> : IArray<T> where T : unmanaged
{
    private readonly int _length;
    private T[] _array;

    public ManagedArray(int length, bool clear)
    {
        _length = length;
        _array = ArrayPool<T>.Shared.Rent(_length);

        if (clear)
            _array.AsSpan(0, _length).Clear();
    }

    public int Length => _length;

    public int TotalManagedAllocations => _array.Length * Marshal.SizeOf<T>();

    public ref T this[int index]
    {
        get
        {
            if (index >= _length)
                ThrowArgumentOutOfRangeIndexException();

            return ref _array[index];
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowArgumentOutOfRangeIndexException()
    {
        throw new IndexOutOfRangeException();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (_array != null)
        {
            ArrayPool<T>.Shared.Return(_array);
            _array = null;
        }
    }

    ~ManagedArray()
    {
        Dispose();
    }
}