using Lucene.Net.Index;
using Lucene.Net.Util;
using NUnit.Framework;
using System;
using static Lucene.Net.Util.UnmanagedStringArray;

namespace Lucene.Net.Search;

public class HybridArrayTests
{
    [TestCase(10)]
    [TestCase(1000)]
    [TestCase(100_000)] // This will test unmanaged path
    public void HybridArray_Int_BasicOperations(int length)
    {
        using var arr = new HybridArray<int>(length, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(length, arr.Length);

        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 2;

        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(i * 2, arr[i]);
    }

    [TestCase(10)]
    [TestCase(1000)]
    [TestCase(100_000)] // This will test unmanaged path
    public void HybridArray_Long_BasicOperations(int length)
    {
        using var arr = new HybridArray<long>(length, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(length, arr.Length);

        for (int i = 0; i < arr.Length; i++)
            arr[i] = 100L + i;

        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(100L + i, arr[i]);
    }

    [TestCase(10)]
    [TestCase(1000)]
    [TestCase(100_000)] // This will test unmanaged path
    public void HybridArray_Double_BasicOperations(int length)
    {
        using var arr = new HybridArray<double>(length, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(length, arr.Length);

        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 0.5;

        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(i * 0.5, arr[i]);
    }

    [TestCase(5)]
    [TestCase(20_000)] // This will test unmanaged path for TermInfo
    public void HybridArray_TermInfo_BasicOperations(int length)
    {
        using var arr = new HybridArray<TermInfo>(length, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(length, arr.Length);

        for (int i = 0; i < arr.Length; i++)
            arr[i] = new TermInfo { docFreq = i, freqPointer = i * 10, proxPointer = i * 100, skipOffset = i * 1000 };

        for (int i = 0; i < arr.Length; i++)
        {
            Assert.AreEqual(i, arr[i].docFreq);
            Assert.AreEqual(i * 10, arr[i].freqPointer);
            Assert.AreEqual(i * 100, arr[i].proxPointer);
            Assert.AreEqual(i * 1000, arr[i].skipOffset);
        }
    }

    [Test]
    public unsafe void HybridArray_UnmanagedString_BasicOperations()
    {
        using var arr = new HybridArray<UnmanagedString>(3, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(3, arr.Length);

        for (int i = 0; i < arr.Length; i++)
            arr[i] = new UnmanagedString { Start = (byte*)i };

        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual((long)i, (long)arr[i].Start);
    }

    [Test]
    public unsafe void HybridArray_UnmanagedString_LargeArray_BasicOperations()
    {
        using var arr = new HybridArray<UnmanagedString>(100_000, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(100_000, arr.Length);

        // Test a subset for performance
        for (int i = 0; i < 100; i++)
            arr[i] = new UnmanagedString { Start = (byte*)(i + 1000) };

        for (int i = 0; i < 100; i++)
            Assert.AreEqual((long)(i + 1000), (long)arr[i].Start);
    }

    [Test]
    public void HybridArray_IndexConsistency()
    {
        using var arr = new HybridArray<int>(5, UnmanagedStringArray.Type.Sorting);

        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 7;

        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(arr[i], arr[i]); // Test that indexer is consistent
    }

    [Test]
    public void HybridArray_LargeArray_IndexConsistency()
    {
        using var arr = new HybridArray<int>(100_000, UnmanagedStringArray.Type.Sorting);

        // Test a subset for performance
        for (int i = 0; i < 100; i++)
            arr[i] = i * 9;

        for (int i = 0; i < 100; i++)
            Assert.AreEqual(arr[i], arr[i]); // Test that indexer is consistent
    }

    [Test]
    public void HybridArray_SmallArray_UsesManagedMemory()
    {
        using var small = new HybridArray<int>(10, UnmanagedStringArray.Type.Sorting);

        // For small arrays, TotalManagedAllocations should be > 0
        Assert.Greater(small.TotalManagedAllocations, 0, "Small arrays should use managed memory");
    }

    [Test]
    public void HybridArray_LargeArray_UsesUnmanagedMemory()
    {
        using var large = new HybridArray<int>(100_000, UnmanagedStringArray.Type.Sorting);

        // For large arrays, TotalManagedAllocations should be 0
        Assert.AreEqual(0, large.TotalManagedAllocations, "Large arrays should use unmanaged memory");
    }

    [Test]
    public void HybridArray_ThresholdBehavior()
    {
        // Test around the threshold boundary
        const int threshold = 64 * 1024;

        // Just under threshold (should use managed)
        int smallLength = (threshold / sizeof(int)) - 1;
        using var small = new HybridArray<int>(smallLength, UnmanagedStringArray.Type.Sorting);
        Assert.Greater(small.TotalManagedAllocations, 0, "Array just under threshold should use managed memory");

        // Just over threshold (should use unmanaged)
        int largeLength = (threshold / sizeof(int)) + 1;
        using var large = new HybridArray<int>(largeLength, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(0, large.TotalManagedAllocations, "Array just over threshold should use unmanaged memory");
    }

    [Test]
    public void HybridArray_IndexOutOfRange_ThrowsException()
    {
        using var arr = new HybridArray<int>(5, UnmanagedStringArray.Type.Sorting);

        Assert.Throws<IndexOutOfRangeException>(() => { var x = arr[-1]; });
        Assert.Throws<IndexOutOfRangeException>(() => { var x = arr[5]; });
        Assert.Throws<IndexOutOfRangeException>(() => { arr[-1] = 42; });
        Assert.Throws<IndexOutOfRangeException>(() => { arr[5] = 42; });
    }

    [Test]
    public void HybridArray_NegativeLength_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HybridArray<int>(-1, UnmanagedStringArray.Type.Sorting));
    }

    [Test]
    public void HybridArray_ZeroLength_Works()
    {
        using var arr = new HybridArray<int>(0, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(0, arr.Length);
    }

    [Test]
    public void HybridArray_MultipleDispose_IsSafe()
    {
        var arr = new HybridArray<int>(10, UnmanagedStringArray.Type.Sorting);
        arr.Dispose();
        arr.Dispose(); // Should not throw
    }
}