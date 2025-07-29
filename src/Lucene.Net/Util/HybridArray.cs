using System;

namespace Lucene.Net.Util;

public static class HybridArray
{
    // simple threshold: 64KB (guarantees ArrayPool won't exceed 85KB LOH limit)
    private const int LOH_THRESHOLD = 64 * 1024;

    public static unsafe IArray<T> Create<T>(int length, UnmanagedStringArray.Type type) where T : unmanaged
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (length * sizeof(T) > LOH_THRESHOLD)
        {
            return new UnmanagedArray<T>(length, type);
        }

        return new ManagedArray<T>(length);
    }
}