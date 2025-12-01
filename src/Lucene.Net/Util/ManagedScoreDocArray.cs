using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucene.Net.Util;

public class ManagedScoreDocArray : IDisposable
{
    public static readonly ManagedScoreDocArray Empty = new();

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
    public int Length => _length;

    public ManagedScoreDocArray()
    {
    }

    public ManagedScoreDocArray(int totalItems, bool hasFields)
    {
        _length = totalItems;

        int remainingToAllocate = totalItems;
        int currentSize = InitialItems;

        while (_segments.Count < StablePhaseSegmentStartIndex && remainingToAllocate > 0)
        {
            AllocateSegment(currentSize, hasFields);
            remainingToAllocate -= currentSize;
            currentSize *= 2;
        }

        if (remainingToAllocate > 0)
        {
            int stableSegmentsNeeded = (remainingToAllocate + (MaxItemsPerSegment - 1)) >> MaxItemsLog2;

            for (int i = 0; i < stableSegmentsNeeded; i++)
            {
                AllocateSegment(MaxItemsPerSegment, hasFields);
            }
        }

        // we must point _currentDocs to the last segment and calculate exactly 
        // how many items are used in that last segment.
        if (_segments.Count > 0)
        {
            var lastIndex = _segments.Count - 1;
            var lastSeg = _segments[lastIndex];

            _currentDocs = lastSeg.Docs;
            _currentScores = lastSeg.Scores;
            _currentSegmentCapacity = lastSeg.Capacity;

            int itemsInPreviousSegments = 0;
            for (int i = 0; i < lastIndex; i++)
            {
                itemsInPreviousSegments += _segments[i].Capacity;
            }

            _currentSegmentUsed = totalItems - itemsInPreviousSegments;

            // update the struct in the list so readers see the correct limit
            lastSeg.Used = _currentSegmentUsed;
        }
        else
        {
            // totalItems was 0
            _currentDocs = null;
            _currentScores = null;
            _currentSegmentCapacity = 0;
            _currentSegmentUsed = 0;
        }
    }

    private void AllocateSegment(int size, bool hasFields)
    {
        var docs = ArrayPool<int>.Shared.Rent(size);
        var scores = ArrayPool<float>.Shared.Rent(size);

        var segment = new Segment
        {
            Docs = docs,
            Scores = scores,
            Capacity = size,
            Used = size // default to full, we fix the last one in the constructor
        };

        if (hasFields)
            segment.Fields = ArrayPool<IComparable[]>.Shared.Rent(size);

        _segments.Add(segment);
    }

    public void Add(int doc, float score)
    {
        if (_currentSegmentUsed == _currentSegmentCapacity)
        {
            EnsureCapacity();
        }

        // OPTIMIZATION: Unsafe ref arithmetic
        ref int docsRef = ref MemoryMarshal.GetArrayDataReference(_currentDocs);
        ref float scoresRef = ref MemoryMarshal.GetArrayDataReference(_currentScores);

        Unsafe.Add(ref docsRef, _currentSegmentUsed) = doc;
        Unsafe.Add(ref scoresRef, _currentSegmentUsed) = score;

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

            if (seg.Fields != null)
                ArrayPool<IComparable[]>.Shared.Return(seg.Fields);
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

    public class Segment
    {
        public int[] Docs;
        public float[] Scores;
        public IComparable[][] Fields;
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

        private int[] _currentDocs;
        private float[] _currentScores;
        private IComparable[][] _currentFields;

        private int _segIndex;
        private int _indexInSegment;

        public BackwardsWriter(ManagedScoreDocArray parent)
        {
            _parent = parent;

            if (_parent.Length == 0)
                return;

            int startIndex = parent._length - 1;

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

            var seg = _parent._segments[_segIndex];
            _currentDocs = seg.Docs;
            _currentScores = seg.Scores;
            _currentFields = seg.Fields;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int doc, float score, IComparable[] fields = null)
        {
            if (_parent.Length == 0)
                ThrowOnEmptyArray();

            // FAST PATH: we are still inside the current segment
            // we use '>= 0' because we are moving backwards
            if (_indexInSegment >= 0)
            {
                // OPTIMIZATION: Unsafe ref arithmetic
                ref int docsRef = ref MemoryMarshal.GetArrayDataReference(_currentDocs);
                ref float scoresRef = ref MemoryMarshal.GetArrayDataReference(_currentScores);

                Unsafe.Add(ref docsRef, _indexInSegment) = doc;
                Unsafe.Add(ref scoresRef, _indexInSegment) = score;

                if (fields != null)
                {
                    ref IComparable[] fieldsRef = ref MemoryMarshal.GetArrayDataReference(_currentFields);
                    Unsafe.Add(ref fieldsRef, _indexInSegment) = fields;
                }

                _indexInSegment--;
                return;
            }

            // SLOW PATH: we crossed a boundary, switch to previous segment
            SwitchToPreviousSegment(doc, score, fields);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SwitchToPreviousSegment(int doc, float score, IComparable[] fields = null)
        {
            _segIndex--;

            if (_segIndex < 0)
                ThrowWriterOutOfRange();

            var seg = _parent._segments[_segIndex];
            _currentDocs = seg.Docs;
            _currentScores = seg.Scores;
            _currentFields = seg.Fields;

            _indexInSegment = seg.Capacity - 1;

            _currentDocs[_indexInSegment] = doc;
            _currentScores[_indexInSegment] = score;

            if (fields != null)
                _currentFields[_indexInSegment] = fields;

            _indexInSegment--;
        }

        private static void ThrowOnEmptyArray()
        {
            throw new InvalidOperationException("Cannot write to an empty array");
        }

        private static void ThrowWriterOutOfRange()
        {
            throw new IndexOutOfRangeException("Writer went below index 0");
        }
    }

    public ScoreDocReader GetReader(int start)
    {
        return new ScoreDocReader(this, start);
    }

    public struct ScoreDocReader
    {
        private readonly ManagedScoreDocArray _managedParent;
        private int[] _currentDocs;
        private float[] _currentScores;
        private IComparable[][] _currentFields;
        private int _currentSegUsedCount;

        private int _segIndex;
        private int _indexInSegment;

        public ScoreDocReader(ManagedScoreDocArray parent, int startIndex)
        {
            _managedParent = parent;

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

            if (_segIndex < parent._segments.Count)
            {
                var seg = parent._segments[_segIndex];
                _currentDocs = seg.Docs;
                _currentScores = seg.Scores;
                _currentFields = seg.Fields;

                _currentSegUsedCount = (_segIndex == parent._segments.Count - 1)
                    ? parent._currentSegmentUsed
                    : seg.Used;
            }
            else
            {
                _currentDocs = null;
                _currentScores = null;
                _currentFields = null;
                _currentSegUsedCount = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out int doc, out float score)
        {
            // FAST PATH: We are inside the boundaries of the current segment
            if (_indexInSegment < _currentSegUsedCount)
            {
                // OPTIMIZATION: Unsafe ref arithmetic
                ref int docsStart = ref MemoryMarshal.GetArrayDataReference(_currentDocs);
                ref float scoresStart = ref MemoryMarshal.GetArrayDataReference(_currentScores);

                doc = Unsafe.Add(ref docsStart, _indexInSegment);
                score = Unsafe.Add(ref scoresStart, _indexInSegment);

                _indexInSegment++;
                return true;
            }

            return ReadNextManagedSegment(out doc, out score);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool ReadNextManagedSegment(out int doc, out float score)
        {
            _segIndex++;
            _indexInSegment = 0;

            if (_managedParent == null || _segIndex >= _managedParent._segments.Count)
            {
                doc = 0;
                score = 0;
                return false;
            }

            var seg = _managedParent._segments[_segIndex];
            _currentDocs = seg.Docs;
            _currentScores = seg.Scores;

            _currentSegUsedCount = (_segIndex == _managedParent._segments.Count - 1)
                ? _managedParent._currentSegmentUsed
                : seg.Used;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out int doc, out float score, out IComparable[] fields)
        {
            // FAST PATH: We are inside the boundaries of the current segment
            if (_indexInSegment < _currentSegUsedCount)
            {
                // OPTIMIZATION: Unsafe ref arithmetic
                ref int docsStart = ref MemoryMarshal.GetArrayDataReference(_currentDocs);
                ref float scoresStart = ref MemoryMarshal.GetArrayDataReference(_currentScores);
                ref IComparable[] fieldsStart = ref MemoryMarshal.GetArrayDataReference(_currentFields);

                doc = Unsafe.Add(ref docsStart, _indexInSegment);
                score = Unsafe.Add(ref scoresStart, _indexInSegment);
                fields = Unsafe.Add(ref fieldsStart, _indexInSegment);

                _indexInSegment++;
                return true;
            }

            return ReadNextManagedSegment(out doc, out score, out fields);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool ReadNextManagedSegment(out int doc, out float score, out IComparable[] fields)
        {
            _segIndex++;
            _indexInSegment = 0;

            if (_managedParent == null || _segIndex >= _managedParent._segments.Count)
            {
                doc = 0;
                score = 0;
                fields = null;
                return false;
            }

            var seg = _managedParent._segments[_segIndex];
            _currentDocs = seg.Docs;
            _currentScores = seg.Scores;
            _currentFields = seg.Fields;

            _currentSegUsedCount = (_segIndex == _managedParent._segments.Count - 1)
                ? _managedParent._currentSegmentUsed
                : seg.Used;

            if (_indexInSegment < _currentSegUsedCount)
            {
                doc = _currentDocs[_indexInSegment];
                score = _currentScores[_indexInSegment];
                fields = _currentFields[_indexInSegment];
                _indexInSegment++;
                return true;
            }

            doc = 0;
            score = 0;
            fields = null;
            return false;
        }
    }
}