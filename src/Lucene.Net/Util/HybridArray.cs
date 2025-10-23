using System;

namespace Lucene.Net.Util;

public static class HybridArray
{
    // simple threshold: 64KB (guarantees ArrayPool won't exceed 85KB LOH limit)
    private const int LOH_THRESHOLD = 64 * 1024;

    public static bool UseOnlyManagedArray = false;

    public static unsafe IArray<T> Create<T>(int length, UnmanagedStringArray.Type type, bool clear = false) where T : unmanaged
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (UseOnlyManagedArray == false && length * sizeof(T) > LOH_THRESHOLD)
        {
            return new UnmanagedArray<T>(length, type, clear);
        }

        return new ManagedArray<T>(length, clear);
    }
}