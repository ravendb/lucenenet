using System;
using Lucene.Net.Util;
using NUnit.Framework;

namespace Lucene.Net.Search;

[TestFixture]
public class ManagedScoreDocArrayTests
{
    // ---------------------------------------------------------
    // 1. Core Logic & Growth Phase Tests
    // ---------------------------------------------------------

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(256)]          // End of first segment
    [TestCase(257)]          // Start of second segment
    [TestCase(16128)]        // End of Growth Phase (Sum of 256..8192)
    [TestCase(16129)]        // Start of Stable Phase (First 16k segment)
    [TestCase(50000)]        // Mid-range
    [TestCase(128000)]       // Target upper bound
    public void Add_And_Indexer_Work_For_Various_Lengths(int count)
    {
        using var array = new ManagedScoreDocArray();

        // Populate
        for (int i = 0; i < count; i++)
        {
            array.Add(i, i * 1.5f);
        }

        // Verify Length
        Assert.That(array.Length, Is.EqualTo(count));

        // Verify Random Access Indexer
        for (int i = 0; i < count; i++)
        {
            var (doc, score) = array[i];
            Assert.That(doc, Is.EqualTo(i));
            Assert.That(score, Is.EqualTo(i * 1.5f));
        }
    }

    [Test]
    public void Add_Resizes_Correctly_Across_Segments()
    {
        // This test focuses on the transition logic of EnsureCapacity
        using var array = new ManagedScoreDocArray();

        // Fill exactly to the end of the Growth phase
        int growthLimit = 16128;
        for (int i = 0; i < growthLimit; i++)
        {
            array.Add(i, 0f);
        }

        // We expect 6 segments (256, 512, 1024, 2048, 4096, 8192)
        Assert.That(array._segments.Count, Is.EqualTo(6));

        // Add one more to trigger the first Stable segment (16k)
        array.Add(999, 999f);

        Assert.That(array._segments.Count, Is.EqualTo(7));
        Assert.That(array._segments[6].Capacity, Is.EqualTo(16384)); // Verify new segment size

        // Verify data integrity across the boundary
        Assert.That(array[0].Doc, Is.EqualTo(0));
        Assert.That(array[16128].Doc, Is.EqualTo(999));
    }

    // ---------------------------------------------------------
    // 2. ScoreDocReader Tests (Forward Iteration)
    // ---------------------------------------------------------

    [TestCase(100)]
    [TestCase(16128)] // Boundary
    [TestCase(35000)]
    public void Reader_Iterates_Correctly(int count)
    {
        using var array = new ManagedScoreDocArray();
        for (int i = 0; i < count; i++) array.Add(i, i + 0.5f);

        var reader = array.GetReader(0);
        int readCount = 0;

        while (reader.Read(out int doc, out float score))
        {
            Assert.That(doc, Is.EqualTo(readCount));
            Assert.That(score, Is.EqualTo(readCount + 0.5f));
            readCount++;
        }

        Assert.That(readCount, Is.EqualTo(count));
    }

    [Test]
    public void Reader_Can_Start_From_Offset()
    {
        int total = 1000;
        int startOffset = 500;
        using var array = new ManagedScoreDocArray();
        for (int i = 0; i < total; i++) array.Add(i, i);

        var reader = array.GetReader(startOffset);

        // Read first item
        bool success = reader.Read(out int doc, out float score);

        Assert.That(success, Is.True);
        Assert.That(doc, Is.EqualTo(500));
    }

    [Test]
    public void Reader_Handles_Empty_Array()
    {
        using var array = new ManagedScoreDocArray();
        var reader = array.GetReader(0);
        Assert.That(reader.Read(out _, out _), Is.False);
    }

    // ---------------------------------------------------------
    // 3. BackwardsWriter Tests
    // ---------------------------------------------------------

    [TestCase(256)]           // Single segment full
    [TestCase(16128)]         // Exact growth phase full
    [TestCase(16129)]         // Crossed into stable
    [TestCase(128000)]        // Large scale
    public void BackwardsWriter_Fills_Array_Correctly(int count)
    {
        // 1. Pre-allocate the array size (simulating how a Collector works)
        using var array = new ManagedScoreDocArray(count, fillFields: false);

        Assert.That(array.Length, Is.EqualTo(count));

        // 2. Get the backwards writer
        var writer = array.GetBackwardsWriter();

        // 3. Fill backwards (simulating a PriorityQueue pop operation)
        // We write: index -> docId
        for (int i = count - 1; i >= 0; i--)
        {
            writer.Write(i, i * 2.0f);
        }

        // 4. Verify using standard forward indexer
        for (int i = 0; i < count; i++)
        {
            var (doc, score) = array[i];
            Assert.That(doc, Is.EqualTo(i));
            Assert.That(score, Is.EqualTo(i * 2.0f));
        }
    }

    [Test]
    public void BackwardsWriter_Throws_If_Over_Written()
    {
        using var array = new ManagedScoreDocArray(10, false);
        var writer = array.GetBackwardsWriter();

        // Write valid items
        for (int i = 0; i < 10; i++) writer.Write(i, 0);

        // Try to write one more (should go below index 0)
        Assert.Throws<IndexOutOfRangeException>(() => writer.Write(99, 0));
    }

    // ---------------------------------------------------------
    // 4. Boundary Stress Test (128k)
    // ---------------------------------------------------------

    [Test]
    public void Stress_Test_128000_Items()
    {
        const int Target = 128000;
        using var array = new ManagedScoreDocArray();

        // 1. Add
        for (int i = 0; i < Target; i++)
        {
            array.Add(i, i);
        }

        // 2. Validate Random Access
        // Check specific boundaries known in the code:
        // 256, 16128, 32512 (16128 + 16384)
        int[] boundaryChecks = { 0, 255, 256, 16127, 16128, 32511, 32512, Target - 1 };

        foreach (var index in boundaryChecks)
        {
            Assert.That(array[index].Doc, Is.EqualTo(index), $"Failed at index {index}");
        }

        // 3. Validate Sequential Read
        var reader = array.GetReader(0);
        int count = 0;
        while (reader.Read(out int d, out float s))
        {
            if (count % 10000 == 0) // Spot check to save time
            {
                Assert.That(d, Is.EqualTo(count));
            }
            count++;
        }
        Assert.That(count, Is.EqualTo(Target));
    }

    // ---------------------------------------------------------
    // 5. Fields / Auxiliary Data Tests
    // ---------------------------------------------------------

    [Test]
    public void Constructor_Allocates_Fields_If_Requested()
    {
        int size = 100;
        using var array = new ManagedScoreDocArray(size, fillFields: true);

        Assert.That(array.Fields, Is.Not.Null);
        Assert.That(array.Fields.Length, Is.EqualTo(size));

        // Test writing to fields via BackwardsWriter
        var writer = array.GetBackwardsWriter();
        var dummyFields = new IComparable[] { "test" };

        // Write at the last index
        writer.Write(size - 1, (99, 1.0f, dummyFields));

        Assert.That(array.Fields[size - 1], Is.EqualTo(dummyFields));
        Assert.That(array[size - 1].Doc, Is.EqualTo(99));
    }

    [Test]
    public void Dispose_Clears_State()
    {
        var array = new ManagedScoreDocArray();
        array.Add(1, 1);

        array.Dispose();

        Assert.That(array.Length, Is.EqualTo(0));
        Assert.That(array._segments, Is.Empty);

        // Ensure accessing after dispose throws
        Assert.Throws<IndexOutOfRangeException>(() => { var _ = array[0]; });
    }

    [Test]
    public void Zero_Length_Array_Edge_Cases()
    {
        // 1. Setup: Test both the explicit constructor and the default constructor
        using var arrayExplicit = new ManagedScoreDocArray(0, fillFields: false);
        using var arrayDefault = new ManagedScoreDocArray();

        // 2. Verify Length property
        Assert.That(arrayExplicit.Length, Is.EqualTo(0));
        Assert.That(arrayDefault.Length, Is.EqualTo(0));

        // 3. Verify Indexer: Accessing index 0 should throw
        Assert.Throws<IndexOutOfRangeException>(() => { var _ = arrayExplicit[0]; });

        // 4. Verify Reader: Should return false immediately
        var reader = arrayExplicit.GetReader(0);
        bool hasData = reader.Read(out int doc, out float score);

        Assert.That(hasData, Is.False, "Reader should not find any data in empty array");
        Assert.That(doc, Is.EqualTo(0), "Out parameter should be default");
        Assert.That(score, Is.EqualTo(0f), "Out parameter should be default");

        // 5. Verify BackwardsWriter: Writing to an empty array is impossible
        // The source code throws InvalidOperationException in Write() if Length == 0
        var writer = arrayExplicit.GetBackwardsWriter();

        var ex = Assert.Throws<InvalidOperationException>(() => writer.Write(1, 1.0f));
        Assert.That(ex.Message, Does.Contain("empty array"));
    }
}