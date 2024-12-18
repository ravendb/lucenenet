using System;
using System.Buffers;
using Lucene.Net.Util;

namespace Lucene.Net.Index;

public class UnmanagedIndexTerms : IDisposable
{
    private readonly string[] fields; // fields are interned
    private readonly UnmanagedStringArray text;

    public int Length => text.Length;

    public UnmanagedIndexTerms(int size)
    {
        fields = ArrayPool<string>.Shared.Rent(size); ;
        text = new UnmanagedStringArray(size, startIndex: 0);
    }

    public void Add(int index, string field, Span<char> textAsSpan)
    {
        fields[index] = field;
        text.Add(textAsSpan);
    }

    public UnmanagedTerm this[int position]
    {
        get => new UnmanagedTerm(fields[position], text[position]);
    }

    public void Dispose()
    {
        text?.Dispose();

        if (fields != null && Length <= 256 * 1024)
            ArrayPool<string>.Shared.Return(fields, clearArray: true);
    }
}