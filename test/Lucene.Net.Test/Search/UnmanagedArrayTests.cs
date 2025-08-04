using Lucene.Net.Index;
using Lucene.Net.Util;
using NUnit.Framework;
using static Lucene.Net.Util.UnmanagedStringArray;

namespace Lucene.Net.Search;

public class UnmanagedArrayTests
{
    [TestCase(10)]
    [TestCase(1000)]
    public void ManagedArray_Int_BasicOperations(int length)
    {
        using var arr = new ManagedArray<int>(length);
        Assert.AreEqual(length, arr.Length);

        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = i * 2;

        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual(i * 2, span[i]);
    }

    [TestCase(10)]
    [TestCase(1000)]
    public void UnmanagedArray_Int_BasicOperations(int length)
    {
        using var arr = new UnmanagedArray<int>(length, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(length, arr.Length);

        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = i * 3;

        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual(i * 3, span[i]);
    }

    [TestCase(10)]
    public void ManagedArray_Long_BasicOperations(int length)
    {
        using var arr = new ManagedArray<long>(length);
        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = 100L + i;
        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual(100L + i, span[i]);
    }

    [TestCase(10)]
    public void UnmanagedArray_Long_BasicOperations(int length)
    {
        using var arr = new UnmanagedArray<long>(length, UnmanagedStringArray.Type.Sorting);
        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = 200L + i;
        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual(200L + i, span[i]);
    }

    [TestCase(10)]
    public void ManagedArray_Double_BasicOperations(int length)
    {
        using var arr = new ManagedArray<double>(length);
        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = i * 0.5;
        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual(i * 0.5, span[i]);
    }

    [TestCase(10)]
    public void UnmanagedArray_Double_BasicOperations(int length)
    {
        using var arr = new UnmanagedArray<double>(length, UnmanagedStringArray.Type.Sorting);
        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = i * 1.5;
        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual(i * 1.5, span[i]);
    }

    [Test]
    public void ManagedArray_TermInfo_BasicOperations()
    {
        using var arr = new ManagedArray<TermInfo>(5);
        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = new TermInfo { docFreq = i, freqPointer = i * 10, proxPointer = i * 100, skipOffset = i * 1000 };
        for (int i = 0; i < span.Length; i++)
        {
            Assert.AreEqual(i, span[i].docFreq);
            Assert.AreEqual(i * 10, span[i].freqPointer);
            Assert.AreEqual(i * 100, span[i].proxPointer);
            Assert.AreEqual(i * 1000, span[i].skipOffset);
        }
    }

    [Test]
    public void UnmanagedArray_TermInfo_BasicOperations()
    {
        using var arr = new UnmanagedArray<TermInfo>(5, UnmanagedStringArray.Type.Sorting);
        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = new TermInfo { docFreq = i, freqPointer = i * 10, proxPointer = i * 100, skipOffset = i * 1000 };
        for (int i = 0; i < span.Length; i++)
        {
            Assert.AreEqual(i, span[i].docFreq);
            Assert.AreEqual(i * 10, span[i].freqPointer);
            Assert.AreEqual(i * 100, span[i].proxPointer);
            Assert.AreEqual(i * 1000, span[i].skipOffset);
        }
    }

    [Test]
    public unsafe void ManagedArray_UnmanagedString_BasicOperations()
    {
        using var arr = new ManagedArray<UnmanagedString>(3);
        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = new UnmanagedString { Start = (byte*)i };
        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual((long)i, (long)span[i].Start);
    }

    [Test]
    public unsafe void UnmanagedArray_UnmanagedString_BasicOperations()
    {
        using var arr = new UnmanagedArray<UnmanagedString>(3, UnmanagedStringArray.Type.Sorting);

        var span = arr.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = new UnmanagedString { Start = (byte*)i };
        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual((long)i, (long)span[i].Start);
    }

    [Test]
    public void ManagedArray_Span_And_ReadOnlySpan_Consistency()
    {
        using var arr = new ManagedArray<int>(5);
        var span = arr.AsSpan();
        var roSpan = arr.AsSpanReadOnlySpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = i * 7;
        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual(span[i], roSpan[i]);
    }

    [Test]
    public void UnmanagedArray_Span_And_ReadOnlySpan_Consistency()
    {
        using var arr = new UnmanagedArray<int>(5, UnmanagedStringArray.Type.Sorting);
        var span = arr.AsSpan();
        var roSpan = arr.AsSpanReadOnlySpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = i * 9;
        for (int i = 0; i < span.Length; i++)
            Assert.AreEqual(span[i], roSpan[i]);
    }

    [Test]
    public void HybridArray_Creates_Correct_Type()
    {
        var small = HybridArray.Create<int>(10, UnmanagedStringArray.Type.Sorting);
        Assert.IsInstanceOf<ManagedArray<int>>(small);
        small.Dispose();

        var large = HybridArray.Create<int>(100_000, UnmanagedStringArray.Type.Sorting);
        Assert.IsInstanceOf<UnmanagedArray<int>>(large);
        large.Dispose();
    }
}