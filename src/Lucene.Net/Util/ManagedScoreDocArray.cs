using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucene.Net.Util;

public class ManagedScoreDocArray : IDisposable
{
    public static ManagedScoreDocArray Empty = new();

    private const int SingleItemSize = sizeof(int);
    private const int MaxItemsPerSegment = 64 * 1024 / SingleItemSize; // 16,384 items - 64KB limit to avoid LOH
    private const int InitialItems = 1 * 1024 / SingleItemSize; // 256 items - 1KB

    // math constants for the "Stable" phase
    // log2(16384) = 14. we can use bit shifting instead of division.
    private const int MaxItemsLog2 = 14;

    // sequence: 256 -> 512 -> 1024 -> 2048 -> 4096 -> 8192
    // the next one (16384) is the start of stable phase.
    // sum = 256 + 512 + ... + 8192 = 16128 items.
    private const int GrowthPhaseTotalItems = 16128;

    // there are 6 segments in the growth phase (0 to 5)
    // segment 6 is the first "Stable" (max Size) segment.
    private const int StablePhaseSegmentStartIndex = 6;

    // used to shift indexes for the Log2 calculation in the growth phase
    private const int GrowthPhaseShift = 8;

    public readonly List<Segment> _segments = new();

    // hot path caching
    private int[] _currentDocs;
    private float[] _currentScores;
    public int _currentSegmentUsed;
    private int _currentSegmentCapacity;

    private int _length;
    private IComparable[][] _fields;

    public int Length => _length;

    public IComparable[][] Fields => _fields;

    public ManagedScoreDocArray()
    {
    }

    public ManagedScoreDocArray(int totalItems, bool fillFields)
    {
        // 1. Initialize State
        _length = totalItems;

        if (fillFields)
            _fields = new IComparable[totalItems][];

        int remainingToAllocate = totalItems;
        int currentSize = InitialItems; // 256

        // 2. Growth Phase Allocation (Small Segments)
        // We allocate full segments (256, 512...) as long as we have items left
        // and we haven't reached the 16k limit.
        while (_segments.Count < StablePhaseSegmentStartIndex && remainingToAllocate > 0)
        {
            AllocateSegment(currentSize);
            remainingToAllocate -= currentSize;
            currentSize *= 2;
        }

        // 3. Stable Phase Allocation (16k Segments)
        if (remainingToAllocate > 0)
        {
            // Ceiling division: (remaining + 16383) / 16384
            // Determines how many 16k blocks we need to cover the rest.
            int stableSegmentsNeeded = (remainingToAllocate + (MaxItemsPerSegment - 1)) >> MaxItemsLog2;

            for (int i = 0; i < stableSegmentsNeeded; i++)
            {
                AllocateSegment(MaxItemsPerSegment);
            }
        }

        // 4. CRITICAL: Sync "Hot Path" State
        // We must point _currentDocs to the last segment and calculate exactly 
        // how many items are used in that last segment.
        if (_segments.Count > 0)
        {
            var lastIndex = _segments.Count - 1;
            var lastSeg = _segments[lastIndex];

            // Set the pointers
            _currentDocs = lastSeg.Docs;
            _currentScores = lastSeg.Scores;
            _currentSegmentCapacity = lastSeg.Capacity;

            // Calculate exact usage of the last segment.
            // Logic: TotalItems - (Capacity of all previous segments)
            int itemsInPreviousSegments = 0;
            for (int i = 0; i < lastIndex; i++)
            {
                itemsInPreviousSegments += _segments[i].Capacity;
            }

            // Example: Total 20,000. Prev segments hold 16,128.
            // _currentSegmentUsed = 3,872.
            _currentSegmentUsed = totalItems - itemsInPreviousSegments;

            // Update the struct in the list so readers see the correct limit
            lastSeg.Used = _currentSegmentUsed;
            _segments[lastIndex] = lastSeg;
        }
        else
        {
            // Edge case: totalItems was 0
            _currentDocs = null;
            _currentScores = null;
            _currentSegmentCapacity = 0;
            _currentSegmentUsed = 0;
        }
    }

    private void AllocateSegment(int size)
    {
        var docs = ArrayPool<int>.Shared.Rent(size);
        var scores = ArrayPool<float>.Shared.Rent(size);

        _segments.Add(new Segment
        {
            Docs = docs,
            Scores = scores,
            Capacity = size,
            Used = size // Default to full, we fix the last one in the constructor
        });
    }

    public void Add(int doc, float score)
    {
        if (_currentSegmentUsed == _currentSegmentCapacity)
        {
            EnsureCapacity();
        }

        _currentDocs[_currentSegmentUsed] = doc;
        _currentScores[_currentSegmentUsed] = score;

        _currentSegmentUsed++;
        _length++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureCapacity()
    {
        if (_segments.Count > 0)
        {
            // if we have an active segment, update its 'Used' count before switching
            // we have to copy the value type back into the list because Segment is a struct,
            // and we modified the local field _currentSegmentUsed
            var lastSeg = _segments[^1];
            lastSeg.Used = _currentSegmentUsed;
            _segments[^1] = lastSeg;
        }

        int newSize;
        if (_segments.Count == 0)
        {
            newSize = InitialItems;
        }
        else
        {
            newSize = _currentSegmentCapacity == MaxItemsPerSegment
                ? MaxItemsPerSegment
                : Math.Min(_currentSegmentCapacity * 2, MaxItemsPerSegment);
        }

        var docs = ArrayPool<int>.Shared.Rent(newSize);
        var scores = ArrayPool<float>.Shared.Rent(newSize);

        var newSegment = new Segment
        {
            Docs = docs,
            Scores = scores,
            Used = 0,
            Capacity = newSize
        };

        _segments.Add(newSegment);

        // update hot-path cache
        _currentDocs = docs;
        _currentScores = scores;
        _currentSegmentCapacity = newSize;
        _currentSegmentUsed = 0;
    }

    public (int Doc, float Score) this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_length)
                ThrowIndexOutOfRangeException();

            // stable region (index >= 16128)
            if (index >= GrowthPhaseTotalItems)
            {
                int relativeIndex = index - GrowthPhaseTotalItems;

                int segmentOffset = relativeIndex >> MaxItemsLog2;
                int indexInSegment = relativeIndex & (MaxItemsPerSegment - 1);

                // offset by the number of growth segments (6 segments: 256..8192)
                var seg = _segments[StablePhaseSegmentStartIndex + segmentOffset];
                return (seg.Docs[indexInSegment], seg.Scores[indexInSegment]);
            }

            // growth region
            return GetFromGrowthSegment(index);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int Doc, float Score) GetFromGrowthSegment(int index)
    {
        int segIndex = BitOperations.Log2((uint)(index >> GrowthPhaseShift) + 1);
        int segmentStart = InitialItems * ((1 << segIndex) - 1);
        int indexInSegment = index - segmentStart;
        var seg = _segments[segIndex];
        return (seg.Docs[indexInSegment], seg.Scores[indexInSegment]);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (var seg in _segments)
        {
            ArrayPool<int>.Shared.Return(seg.Docs);
            ArrayPool<float>.Shared.Return(seg.Scores);
        }

        _segments.Clear();
        _currentDocs = null;
        _currentScores = null;
        _length = _currentSegmentUsed = _currentSegmentCapacity = 0;
    }

    ~ManagedScoreDocArray()
    {
        Dispose();
    }

    private static void ThrowIndexOutOfRangeException() => throw new IndexOutOfRangeException();

    public struct Segment
    {
        public int[] Docs;
        public float[] Scores;
        public int Used;
        public int Capacity;
    }

    /// <summary>
    /// Returns a writer optimized for filling the array backwards from (length - 1) down to 0.
    /// </summary>
    public BackwardsWriter GetBackwardsWriter()
    {
        return new BackwardsWriter(this);
    }

    public struct BackwardsWriter
    {
        private readonly ManagedScoreDocArray _parent;

        // Cache the current segment arrays
        private int[] _currentDocs;
        private float[] _currentScores;

        // State
        private int _segIndex;
        private int _indexInSegment;

        public BackwardsWriter(ManagedScoreDocArray parent)
        {
            _parent = parent;

            if (_parent.Length == 0)
                return;

            // Start at the last valid index
            int startIndex = parent._length - 1;

            // 1. Calculate Initial Segment & Offset (Same math as Set/Get)
            if (startIndex >= GrowthPhaseTotalItems)
            {
                int relativeIndex = startIndex - GrowthPhaseTotalItems;
                _segIndex = StablePhaseSegmentStartIndex + (relativeIndex >> MaxItemsLog2);
                _indexInSegment = relativeIndex & (MaxItemsPerSegment - 1);
            }
            else
            {
                _segIndex = BitOperations.Log2((uint)(startIndex >> GrowthPhaseShift) + 1);
                int segmentStart = InitialItems * ((1 << _segIndex) - 1);
                _indexInSegment = startIndex - segmentStart;
            }

            // 2. Load the arrays
            var seg = _parent._segments[_segIndex];
            _currentDocs = seg.Docs;
            _currentScores = seg.Scores;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int doc, float score)
        {
            if (_parent.Length == 0)
                ThrowOnEmptyArray();

            // FAST PATH: We are still inside the current segment
            // We use '>= 0' because we are moving backwards
            if (_indexInSegment >= 0)
            {
                _currentDocs[_indexInSegment] = doc;
                _currentScores[_indexInSegment] = score;

                _indexInSegment--; // Move backwards
                return;
            }

            // SLOW PATH: We crossed a boundary, switch to previous segment
            SwitchToPreviousSegment(doc, score);
        }

        private static void ThrowOnEmptyArray()
        {
            throw new InvalidOperationException("Cannot write to an empty array");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int index, (int Doc, float Score, IComparable[] fields) fieldDoc)
        {
            Write(fieldDoc.Doc, fieldDoc.Score);

            _parent._fields[index] = fieldDoc.fields;
    }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SwitchToPreviousSegment(int doc, float score)
        {
            // 1. Move to previous segment
            _segIndex--;

            // Safety check (should not happen if loop logic is correct)
            if (_segIndex < 0) throw new IndexOutOfRangeException("Writer went below index 0");

            var seg = _parent._segments[_segIndex];
            _currentDocs = seg.Docs;
            _currentScores = seg.Scores;

            // 2. Set index to the END of this new (previous) segment
            // Note: We use 'Capacity' because Initialize() allocated the logical size.
            // For growth segments, this finds the correct size (e.g., 8192 - 1).
            _indexInSegment = seg.Capacity - 1;

            // 3. Perform the write
            _currentDocs[_indexInSegment] = doc;
            _currentScores[_indexInSegment] = score;

            _indexInSegment--;
        }
    }

    public ScoreDocReader GetReader(int start)
    {
        return new ScoreDocReader(this, start);
    }

    public struct ScoreDocReader
    {
        // --- Managed Segments State ---
        private readonly ManagedScoreDocArray _managedParent;
        private int[] _currentDocs;
        private float[] _currentScores;
        private int _currentSegUsedCount;

        // cursor State
        private int _segIndex;
        private int _indexInSegment;

        // ---------------------------------------------------------
        // CONSTRUCTOR: Managed Mode Only
        // ---------------------------------------------------------
        public ScoreDocReader(ManagedScoreDocArray parent, int startIndex)
        {
            _managedParent = parent;

            // 1. Calculate Initial Segment & Offset using Geometric Math
            // (Reusing the O(1) math from the class constants)
            if (startIndex >= GrowthPhaseTotalItems)
            {
                int relativeIndex = startIndex - GrowthPhaseTotalItems;
                _segIndex = StablePhaseSegmentStartIndex + (relativeIndex >> MaxItemsLog2);
                _indexInSegment = relativeIndex & (MaxItemsPerSegment - 1);
            }
            else
            {
                // k = Log2(index/256 + 1)
                _segIndex = BitOperations.Log2((uint)(startIndex >> GrowthPhaseShift) + 1);
                int segmentStart = InitialItems * ((1 << _segIndex) - 1);
                _indexInSegment = startIndex - segmentStart;
            }

            // 2. Load the initial segment arrays
            if (_segIndex < parent._segments.Count)
            {
                var seg = parent._segments[_segIndex];
                _currentDocs = seg.Docs;
                _currentScores = seg.Scores;

                // Sync with the parent's "Hot Path" count if this is the active segment
                _currentSegUsedCount = (_segIndex == parent._segments.Count - 1)
                    ? parent._currentSegmentUsed
                    : seg.Used;
            }
            else
            {
                _currentDocs = null;
                _currentScores = null;
                _currentSegUsedCount = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out int doc, out float score)
        {
            // FAST PATH: We are inside the boundaries of the current segment
            if (_indexInSegment < _currentSegUsedCount)
            {
                // 1. Get a reference to the start of the array (Header)
                // MemoryMarshal.GetArrayDataReference skips the check for index 0.
                // It returns a 'ref int' to the first element.
                ref int docsStart = ref MemoryMarshal.GetArrayDataReference(_currentDocs);
                ref float scoresStart = ref MemoryMarshal.GetArrayDataReference(_currentScores);

                // 2. Perform Pointer Arithmetic (Managed)
                // Unsafe.Add(ref start, offset) calculates address: start + (offset * sizeof(T))
                // NO BOUNDS CHECK is performed.
                doc = Unsafe.Add(ref docsStart, _indexInSegment);
                score = Unsafe.Add(ref scoresStart, _indexInSegment);

                _indexInSegment++;
                return true;
            }

            // SLOW PATH
            return ReadNextManagedSegment(out doc, out score);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool ReadNextManagedSegment(out int doc, out float score)
        {
            // 1. Advance Segment Index
            _segIndex++;
            _indexInSegment = 0;

            // 2. Check Bounds
            if (_managedParent == null || _segIndex >= _managedParent._segments.Count)
            {
                doc = 0;
                score = 0;
                return false;
            }

            // 3. Load New Segment Arrays
            var seg = _managedParent._segments[_segIndex];
            _currentDocs = seg.Docs;
            _currentScores = seg.Scores;

            // 4. Sync Active Count
            _currentSegUsedCount = (_segIndex == _managedParent._segments.Count - 1)
                ? _managedParent._currentSegmentUsed
                : seg.Used;

            // 5. Retry Read (Double check in case next segment is empty, though unlikely)
            if (_indexInSegment < _currentSegUsedCount)
            {
                doc = _currentDocs[_indexInSegment];
                score = _currentScores[_indexInSegment];
                _indexInSegment++;
                return true;
            }

            doc = 0;
            score = 0;
            return false;
        }
    }
}