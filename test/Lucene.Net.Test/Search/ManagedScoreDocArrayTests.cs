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
    [TestCase(128)]          // End of first segment (Segment 0 = 128 items)
    [TestCase(129)]          // Start of second segment
    [TestCase(8064)]         // End of Growth Phase (Sum of 128+256+512+1024+2048+4096)
    [TestCase(8065)]         // Start of Stable Phase (First 8192-item segment)
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
        using var array = new ManagedScoreDocArray();

        // Fill exactly to the end of the Growth phase
        // Growth segments: 128 + 256 + 512 + 1024 + 2048 + 4096 = 8064
        int growthLimit = 8064;
        for (int i = 0; i < growthLimit; i++)
        {
            array.Add(i, 0f);
        }

        // 6 growth segments (indices 0–5)
        Assert.That(array.SegmentCount, Is.EqualTo(6));

        // Add one more to trigger the first Stable segment (8192 items)
        array.Add(999, 999f);

        // Now we have 7 segments: 6 growth + 1 stable
        Assert.That(array.SegmentCount, Is.EqualTo(7));
        Assert.That(array.SegmentCapacity(6), Is.EqualTo(8192));

        // Verify data integrity across the boundary
        Assert.That(array[0].Doc, Is.EqualTo(0));
        Assert.That(array[8064].Doc, Is.EqualTo(999));
    }

    // ---------------------------------------------------------
    // 2. ScoreDocReader Tests
    // ---------------------------------------------------------

    [TestCase(100)]
    [TestCase(8064)]  // Growth phase boundary
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
    // 3. BackwardsWriter Tests (Standard & Complex Boundaries)
    // ---------------------------------------------------------

    [TestCase(256)]
    [TestCase(8064)]
    [TestCase(8065)]
    [TestCase(128000)]
    public void BackwardsWriter_Fills_Array_Correctly(int count)
    {
        using var array = new ManagedScoreDocArray(count, hasFields: false);
        Assert.That(array.Length, Is.EqualTo(count));

        var writer = array.GetBackwardsWriter();

        // Fill backwards
        for (int i = count - 1; i >= 0; i--)
        {
            writer.Write(i, i * 2.0f);
        }

        // Verify using standard indexer
        for (int i = 0; i < count; i++)
        {
            var (doc, score) = array[i];
            Assert.That(doc, Is.EqualTo(i));
            Assert.That(score, Is.EqualTo(i * 2.0f));
        }
    }

    [Test]
    public void BackwardsWriter_Crosses_Growth_Segments_Correctly()
    {
        // Segment 0 size: 128, Segment 1 size: 256.
        // We allocate 200 items. This spans Segment 0 (128) and Segment 1 (size 256, used 72).
        int count = 200;
        using var array = new ManagedScoreDocArray(count, hasFields: false);
        var writer = array.GetBackwardsWriter();

        for (int i = count - 1; i >= 0; i--)
        {
            writer.Write(i, i);
        }

        // Verify the boundary specifically
        // Index 127 should be in Segment 0
        // Index 128 should be in Segment 1
        Assert.That(array[127].Doc, Is.EqualTo(127));
        Assert.That(array[128].Doc, Is.EqualTo(128));
    }

    [Test]
    public void BackwardsWriter_Crosses_Growth_To_Stable_Boundary()
    {
        // Growth phase total: 8064 items (segments 0–5).
        // We allocate 8066 items. 
        // Index 8064 and 8065 are in the first Stable Segment (segment 6).
        // Index 8063 is in the last Growth Segment (segment 5).
        int count = 8066;
        using var array = new ManagedScoreDocArray(count, hasFields: false);
        var writer = array.GetBackwardsWriter();

        for (int i = count - 1; i >= 0; i--)
        {
            writer.Write(i, (float)i);
        }

        // Verify the specific boundary
        Assert.That(array[8063].Doc, Is.EqualTo(8063)); // Last of growth
        Assert.That(array[8064].Doc, Is.EqualTo(8064)); // First of stable
        Assert.That(array[8065].Doc, Is.EqualTo(8065));
    }

    [Test]
    public void BackwardsWriter_Tuple_Overload_Works()
    {
        using var array = new ManagedScoreDocArray(3, hasFields: false);
        var writer = array.GetBackwardsWriter();

        writer.Write(100, 1.0f);
        writer.Write(200, 2.0f);
        writer.Write(300, 3.0f);

        // Verify
        Assert.That(array[0].Doc, Is.EqualTo(300));
        Assert.That(array[1].Doc, Is.EqualTo(200));
        Assert.That(array[2].Doc, Is.EqualTo(100));
    }

    [Test]
    public void BackwardsWriter_Throws_If_Over_Written()
    {
        using var array = new ManagedScoreDocArray(10, false);
        var writer = array.GetBackwardsWriter();

        for (int i = 0; i < 10; i++) writer.Write(i, 0);

        try
        {
            writer.Write(99, 0);
            Assert.Fail("Expected IndexOutOfRangeException");
        }
        catch (IndexOutOfRangeException)
        {
            // expected
        }
    }

    // ---------------------------------------------------------
    // 4. Fields / Auxiliary Data Tests 
    // ---------------------------------------------------------

    [Test]
    public void End_To_End_Fields_Read_Write()
    {
        int size = 300; // Large enough to cross the first segment boundary (256)

        using var array = new ManagedScoreDocArray(size, hasFields: true);
        var writer = array.GetBackwardsWriter();

        for (int i = size - 1; i >= 0; i--)
        {
            var dummyFields = new IComparable[] { $"val_{i}", i };
            writer.Write(doc: i, score: i * 1.0f, fields: dummyFields);
        }

        var reader = array.GetReader(0);
        int readCount = 0;

        while (reader.Read(out int doc, out float score, out IComparable[] fields))
        {
            Assert.That(doc, Is.EqualTo(readCount));
            Assert.That(fields, Is.Not.Null);
            Assert.That(fields.Length, Is.EqualTo(2));
            Assert.That(fields[0], Is.EqualTo($"val_{readCount}"));
            Assert.That(fields[1], Is.EqualTo(readCount));

            readCount++;
        }

        Assert.That(readCount, Is.EqualTo(size));
    }

    // ---------------------------------------------------------
    // 5. Stress & Integrity Tests
    // ---------------------------------------------------------

    [Test]
    public void Stress_Test_128000_Items()
    {
        const int Target = 128000;
        using var array = new ManagedScoreDocArray();

        for (int i = 0; i < Target; i++)
        {
            array.Add(i, i);
        }

        // Validate Random Access at known boundaries
        // Growth phase: [0..8063], Stable segments: [8064..16255], [16256..24447], ...
        int[] boundaryChecks = { 0, 127, 128, 8063, 8064, 16255, 16256, Target - 1 };

        foreach (var index in boundaryChecks)
        {
            Assert.That(array[index].Doc, Is.EqualTo(index), $"Failed at index {index}");
        }

        // Validate Sequential Read
        var reader = array.GetReader(0);
        int count = 0;
        while (reader.Read(out int d, out float s))
        {
            if (count % 10000 == 0)
            {
                Assert.That(d, Is.EqualTo(count));
            }
            count++;
        }
        Assert.That(count, Is.EqualTo(Target));
    }

    [Test]
    public void Data_Integrity_Random_Vs_Sequential()
    {
        // This test ensures that iterating via Reader yields the exact same data
        // as accessing via the Random Access Indexer.
        int count = 5000;
        using var array = new ManagedScoreDocArray();

        // Populate with pseudo-random data
        for (int i = 0; i < count; i++)
        {
            array.Add(i * 2, i * 0.33f);
        }

        var reader = array.GetReader(0);
        int idx = 0;

        while (reader.Read(out int rDoc, out float rScore))
        {
            var (iDoc, iScore) = array[idx];

            Assert.That(rDoc, Is.EqualTo(iDoc), $"Doc Mismatch at {idx}");
            Assert.That(rScore, Is.EqualTo(iScore), $"Score Mismatch at {idx}");

            idx++;
        }
    }

    [Test]
    public void Fields_With_Multiple_Sort_Criteria_Persist_Correctly()
    {
        // Arrange
        // Use enough items to cross the first segment boundary (256 items) to ensure 
        // the parallel field arrays are managed correctly across segments.
        int count = 300;
        using var array = new ManagedScoreDocArray(count, hasFields: true);
        var writer = array.GetBackwardsWriter();

        // Act: Write data backwards (typical TopDocs collector behavior)
        for (int i = count - 1; i >= 0; i--)
        {
            // Simulate 3 sort fields:
            // 0: String (e.g., Category)
            // 1: Int (e.g., Price - creating a descending sort simulation)
            // 2: Float (e.g., Relevance/Score)
            var multiFields = new IComparable[]
            {
                $"Category_{i % 10}", // String
                i * 100,              // Int
                (float)i / 0.5f       // Float
            };

            writer.Write(i, i * 1.0f, multiFields);
        }

        // Assert: Read forwards
        var reader = array.GetReader(0);
        int readCount = 0;

        while (reader.Read(out int doc, out float score, out IComparable[] fields))
        {
            Assert.That(doc, Is.EqualTo(readCount));

            // Verify structure
            Assert.That(fields, Is.Not.Null);
            Assert.That(fields.Length, Is.EqualTo(3), "Should hold exactly 3 field values");

            // Verify specific values for all 3 fields
            Assert.That(fields[0], Is.EqualTo($"Category_{readCount % 10}"));
            Assert.That(fields[1], Is.EqualTo(readCount * 100));
            Assert.That(fields[2], Is.EqualTo((float)readCount / 0.5f));

            readCount++;
        }

        Assert.That(readCount, Is.EqualTo(count));
    }

    // ---------------------------------------------------------
    // 6. Cleanup & Edge Cases
    // ---------------------------------------------------------

    [Test]
    public void Dispose_Clears_State()
    {
        var array = new ManagedScoreDocArray();
        array.Add(1, 1);
        array.Dispose();

        Assert.That(array.Length, Is.EqualTo(0));
        Assert.AreEqual(0, array.SegmentCount);
        Assert.Throws<IndexOutOfRangeException>(() => { var _ = array[0]; });
    }

    [Test]
    public void Zero_Length_Array_Edge_Cases()
    {
        using var arrayExplicit = new ManagedScoreDocArray(0, hasFields: false);

        // Length
        Assert.That(arrayExplicit.Length, Is.EqualTo(0));

        // Indexer
        try
        {
            var _ = arrayExplicit[0];
            Assert.Fail("Expected IndexOutOfRangeException");
        }
        catch (IndexOutOfRangeException)
        {
            // expected
        }

        // Reader
        var reader = arrayExplicit.GetReader(0);
        Assert.That(reader.Read(out _, out _), Is.False);

        // Writer
        var writer = arrayExplicit.GetBackwardsWriter();
        try
        {
            writer.Write(1, 1.0f);
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException ex)
        {
            Assert.That(ex.Message, Does.Contain("empty array"));
        }
    }
}