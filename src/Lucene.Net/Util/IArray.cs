using System;

namespace Lucene.Net.Util;

public interface IArray<T> : IDisposable where T : unmanaged
{
    int Length { get; }

    int TotalManagedAllocations { get; }

    Span<T> AsSpan();

    ReadOnlySpan<T> AsSpanReadOnlySpan();
}