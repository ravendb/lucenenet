using System;

namespace Lucene.Net.Util;

public interface IArray<T> : IDisposable where T : unmanaged
{
    int Length { get; }

    int TotalManagedAllocations { get; }

    ref T this[int index] { get; }
}