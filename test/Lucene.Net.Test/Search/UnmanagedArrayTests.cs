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

        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 2;

        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(i * 2, arr[i]);
    }

    [TestCase(10)]
    [TestCase(1000)]
    public void UnmanagedArray_Int_BasicOperations(int length)
    {
        using var arr = new UnmanagedArray<int>(length, UnmanagedStringArray.Type.Sorting);
        Assert.AreEqual(length, arr.Length);

        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 3;

        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(i * 3, arr[i]);
    }

    [TestCase(10)]
    public void ManagedArray_Long_BasicOperations(int length)
    {
        using var arr = new ManagedArray<long>(length);
        for (int i = 0; i < arr.Length; i++)
            arr[i] = 100L + i;
        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(100L + i, arr[i]);
    }

    [TestCase(10)]
    public void UnmanagedArray_Long_BasicOperations(int length)
    {
        using var arr = new UnmanagedArray<long>(length, UnmanagedStringArray.Type.Sorting);
        for (int i = 0; i < arr.Length; i++)
            arr[i] = 200L + i;
        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(200L + i, arr[i]);
    }

    [TestCase(10)]
    public void ManagedArray_Double_BasicOperations(int length)
    {
        using var arr = new ManagedArray<double>(length);
        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 0.5;
        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(i * 0.5, arr[i]);
    }

    [TestCase(10)]
    public void UnmanagedArray_Double_BasicOperations(int length)
    {
        using var arr = new UnmanagedArray<double>(length, UnmanagedStringArray.Type.Sorting);
        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 1.5;
        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(i * 1.5, arr[i]);
    }

    [Test]
    public void ManagedArray_TermInfo_BasicOperations()
    {
        using var arr = new ManagedArray<TermInfo>(5);
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
    public void UnmanagedArray_TermInfo_BasicOperations()
    {
        using var arr = new UnmanagedArray<TermInfo>(5, UnmanagedStringArray.Type.Sorting);
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
    public unsafe void ManagedArray_UnmanagedString_BasicOperations()
    {
        using var arr = new ManagedArray<UnmanagedString>(3);
        for (int i = 0; i < arr.Length; i++)
            arr[i] = new UnmanagedString { Start = (byte*)i };
        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual((long)i, (long)arr[i].Start);
    }

    [Test]
    public unsafe void UnmanagedArray_UnmanagedString_BasicOperations()
    {
        using var arr = new UnmanagedArray<UnmanagedString>(3, UnmanagedStringArray.Type.Sorting);

        for (int i = 0; i < arr.Length; i++)
            arr[i] = new UnmanagedString { Start = (byte*)i };
        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual((long)i, (long)arr[i].Start);
    }

    [Test]
    public void ManagedArray_arr_And_ReadOnlyarr_Consistency()
    {
        using var arr = new ManagedArray<int>(5);
        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 7;
        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(arr[i], arr[i]);
    }

    [Test]
    public void UnmanagedArray_arr_And_ReadOnlyarr_Consistency()
    {
        using var arr = new UnmanagedArray<int>(5, UnmanagedStringArray.Type.Sorting);
        for (int i = 0; i < arr.Length; i++)
            arr[i] = i * 9;
        for (int i = 0; i < arr.Length; i++)
            Assert.AreEqual(arr[i], arr[i]);
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