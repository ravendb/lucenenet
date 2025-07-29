using System;

namespace Lucene.Net.Util;

public interface IArray<T> : IDisposable where T : unmanaged
{
    int TotalManagedAllocations { get; }
    Span<T> AsSpan();
}