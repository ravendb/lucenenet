using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucene.Net.Util;

// =========================================================================
// SEGMENT ARCHITECTURE — Two-Phase Segmented Array
// =========================================================================
//
// This data structure stores packed (doc, score) pairs as long values in a
// segmented array. Each item is 8 bytes: 4 bytes for doc (int) + 4 bytes for
// score (float), packed into a single long via Unsafe reinterpretation.
//
// The array is split into segments to avoid Large Object Heap (LOH) allocations.
// Any single array >= 85,000 bytes goes on the LOH, causing GC pressure.
// We cap each segment at 64KB (8,192 items × 8 bytes), staying well under.
//
// Two growth phases ensure both small and large collections are efficient:
//
// ── PHASE 1: Growth Phase (Segments 0–5) ──────────────────────────────────
//
// Segment sizes double each time, starting from InitialItems (128):
//
//   Seg Index │ Capacity │ Bytes  │ Cumulative Items │ Global Index Range
//   ──────────┼──────────┼────────┼──────────────────┼────────────────────
//       0     │    128   │   1 KB │        128       │ [0 .. 127]
//       1     │    256   │   2 KB │        384       │ [128 .. 383]
//       2     │    512   │   4 KB │        896       │ [384 .. 895]
//       3     │  1,024   │   8 KB │      1,920       │ [896 .. 1,919]
//       4     │  2,048   │  16 KB │      3,968       │ [1,920 .. 3,967]
//       5     │  4,096   │  32 KB │      8,064       │ [3,968 .. 8,063]
//             │          │        │                  │
//             │  Total:  │  63 KB │  8,064 items     │
//
// This doubling strategy means small result sets (common case: top-10, top-100)
// waste minimal memory, while still scaling up quickly.
//
// O(1) random access in the growth phase uses bit tricks:
//   segIndex     = Log2((globalIndex >> 7) + 1)
//   segmentStart = 128 * ((1 << segIndex) - 1)       // geometric sum
//   localIndex   = globalIndex - segmentStart
//
// ── PHASE 2: Stable Phase (Segments 6+) ───────────────────────────────────
//
// Once the growth phase is exhausted (after 8,064 items), all subsequent
// segments are fixed at MaxItemsPerSegment (8,192 items = 64KB each).
//
//   Seg Index │ Capacity │ Bytes  │ Cumulative Items │ Global Index Range
//   ──────────┼──────────┼────────┼──────────────────┼────────────────────
//       6     │  8,192   │  64 KB │     16,256       │ [8,064 .. 16,255]
//       7     │  8,192   │  64 KB │     24,448       │ [16,256 .. 24,447]
//       8     │  8,192   │  64 KB │     32,640       │ [24,448 .. 32,639]
//      ...    │   ...    │  ...   │       ...        │       ...
//
// O(1) random access in the stable phase is simple division/modulo via shifts:
//   relativeIndex = globalIndex - GrowthPhaseTotalItems (8,064)
//   segIndex      = StablePhaseSegmentStartIndex + (relativeIndex >> 13)
//   localIndex    = relativeIndex & (8,191)             // mask = 8192-1
//
// ── WHY NOT JUST USE ONE BIG ARRAY? ───────────────────────────────────────
//
// A single array for 128K items = 1MB → LOH → Gen2 GC pressure.
// With segments: max 64KB per array → always SOH → collected cheaply in Gen0/1.
// Arrays are rented from ArrayPool, so repeated searches reuse buffers.
//
// ── MEMORY LAYOUT PER ITEM ────────────────────────────────────────────────
//
//   long[i] = | int doc (4 bytes) | float score (4 bytes) |
//
// Accessed via Unsafe.As reinterpretation: the long[] is treated as an int[]
// of twice the length. Item N is at int offset N*2 (doc) and N*2+1 (score).
//
// =========================================================================

public class ManagedScoreDocArray : IDisposable
{
    public static readonly ManagedScoreDocArray Empty = new();

    public static ArrayPool<long> LongArrayPool = ArrayPool<long>.Shared;
    public static ArrayPool<IComparable[]> FieldsArrayPool = ArrayPool<IComparable[]>.Shared;

    // Each packed item is one long: int doc (4 bytes) + float score (4 bytes) = 8 bytes.
    private const int SingleItemSize = sizeof(long);

    // Max segment size: 8,192 items × 8 bytes = 64KB. Stays under the 85KB LOH threshold.
    private const int MaxItemsPerSegment = 64 * 1024 / SingleItemSize;

    // Initial segment size: 128 items × 8 bytes = 1KB. Small enough for top-10/top-100 results.
    private const int InitialItems = 1 * 1024 / SingleItemSize;

    // Log2(MaxItemsPerSegment) = Log2(8192) = 13. Used for bit-shift division in the stable phase.
    private const int MaxItemsLog2 = 13;

    // Growth phase segment sizes: 128, 256, 512, 1024, 2048, 4096 (6 segments, indices 0–5).
    // Total items in growth phase = 128 + 256 + 512 + 1024 + 2048 + 4096 = 8,064.
    // Any global index < 8,064 falls in the growth phase; >= 8,064 falls in the stable phase.
    private const int GrowthPhaseTotalItems = 8064;

    // The stable phase begins at segment index 6 (the 7th segment).
    // Segments 0–5 are growth phase (doubling sizes), segments 6+ are all 8,192 items each.
    private const int StablePhaseSegmentStartIndex = 6;

    // Log2(InitialItems) = Log2(128) = 7. Used in the growth phase O(1) index calculation:
    //   segIndex = Log2((globalIndex >> GrowthPhaseShift) + 1)
    private const int GrowthPhaseShift = 7;

    private readonly List<Segment> _segments = new();

    // Hot path caching - points to the packed long array
    private long[] _currentPacked;
    private int _currentSegmentUsed;
    private int _currentSegmentCapacity;

    private int _length;

    /// <summary>Gets the length of the array</summary>
    public int Length => _length;

    /// <summary>Gets the number of allocated segments.</summary>
    public int SegmentCount => _segments.Count;

    /// <summary>Gets the segment capacity</summary>
    public int SegmentCapacity(int num) => _segments[num].Capacity;

    public ManagedScoreDocArray()
    {
    }

    public ManagedScoreDocArray(int totalItems, bool hasFields) : this()
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

        if (_segments.Count > 0)
        {
            var lastIndex = _segments.Count - 1;
            var lastSeg = _segments[lastIndex];

            _currentPacked = lastSeg.PackedDocsAndScores;
            _currentSegmentCapacity = lastSeg.Capacity;

            int itemsInPreviousSegments = 0;
            for (int i = 0; i < lastIndex; i++)
            {
                itemsInPreviousSegments += _segments[i].Capacity;
            }

            _currentSegmentUsed = totalItems - itemsInPreviousSegments;
            lastSeg.Used = _currentSegmentUsed;
        }
        else
        {
            _currentPacked = null;
            _currentSegmentCapacity = 0;
            _currentSegmentUsed = 0;
        }
    }

    private void AllocateSegment(int size, bool hasFields)
    {
        var packed = LongArrayPool.Rent(size);

        var segment = new Segment
        {
            PackedDocsAndScores = packed,
            Capacity = size,
            Used = size
        };

        if (hasFields)
            segment.Fields = FieldsArrayPool.Rent(size);

        _segments.Add(segment);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int doc, float score)
    {
        if (_currentSegmentUsed == _currentSegmentCapacity)
        {
            EnsureCapacity();
        }

        // OPTIMIZATION: Ref aliasing
        // 1. Get reference to the start of the long[] array
        ref long longStart = ref MemoryMarshal.GetArrayDataReference(_currentPacked);

        // 2. Reinterpret that memory as int[]
        ref int intStart = ref Unsafe.As<long, int>(ref longStart);

        // 3. Calculate offset: each "Item" is 2 ints long (8 bytes)
        //    Offset for Doc = index * 2
        //    Offset for Score = index * 2 + 1
        int offset = _currentSegmentUsed * 2;

        Unsafe.Add(ref intStart, offset) = doc;

        // 4. Reinterpret the specific slot for score as float and write
        Unsafe.Add(ref Unsafe.As<int, float>(ref intStart), offset + 1) = score;

        _currentSegmentUsed++;
        _length++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureCapacity()
    {
        if (_segments.Count > 0)
        {
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

        var packed = LongArrayPool.Rent(newSize);

        var newSegment = new Segment
        {
            PackedDocsAndScores = packed,
            Used = 0,
            Capacity = newSize
        };

        _segments.Add(newSegment);

        _currentPacked = packed;
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

            if (index >= GrowthPhaseTotalItems)
            {
                int relativeIndex = index - GrowthPhaseTotalItems;
                int segmentOffset = relativeIndex >> MaxItemsLog2;
                int indexInSegment = relativeIndex & (MaxItemsPerSegment - 1);

                var seg = _segments[StablePhaseSegmentStartIndex + segmentOffset];
                return GetPackedValue(seg.PackedDocsAndScores, indexInSegment);
            }

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
        return GetPackedValue(seg.PackedDocsAndScores, indexInSegment);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int, float) GetPackedValue(long[] packed, int index)
    {
        ref long longStart = ref MemoryMarshal.GetArrayDataReference(packed);
        ref int intStart = ref Unsafe.As<long, int>(ref longStart);
        int offset = index * 2;

        int doc = Unsafe.Add(ref intStart, offset);
        float score = Unsafe.Add(ref Unsafe.As<int, float>(ref intStart), offset + 1);

        return (doc, score);
    }

    public void Dispose()
    {
        // never dispose the static Empty instance
        if (ReferenceEquals(this, Empty))
            return;

        GC.SuppressFinalize(this);

        foreach (var seg in _segments)
        {
            LongArrayPool.Return(seg.PackedDocsAndScores);

            if (seg.Fields != null)
                FieldsArrayPool.Return(seg.Fields);
        }

        _segments.Clear();
        _currentPacked = null;
        _length = _currentSegmentUsed = _currentSegmentCapacity = 0;
    }

    ~ManagedScoreDocArray()
    {
        Dispose();
    }

    private static void ThrowIndexOutOfRangeException() => throw new IndexOutOfRangeException();

    public class Segment
    {
        public long[] PackedDocsAndScores;
        public IComparable[][] Fields;
        public int Used;
        public int Capacity;
    }

    public BackwardsWriter GetBackwardsWriter() => new(this);

    public ref struct BackwardsWriter
    {
        private readonly ManagedScoreDocArray _parent;
        private ref int _intStart;
        private ref IComparable[] _fieldsStart;
        private bool _hasFields;

        private int _segIndex;
        private int _indexInSegment;

        public BackwardsWriter(ManagedScoreDocArray parent)
        {
            _parent = parent;
            _hasFields = false;

            if (_parent.Length == 0)
            {
                _intStart = ref Unsafe.NullRef<int>();
                _fieldsStart = ref Unsafe.NullRef<IComparable[]>();
                return;
            }

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
            _intStart = ref Unsafe.As<long, int>(ref MemoryMarshal.GetArrayDataReference(seg.PackedDocsAndScores));

            if (seg.Fields != null)
            {
                _fieldsStart = ref MemoryMarshal.GetArrayDataReference(seg.Fields);
                _hasFields = true;
            }
            else
            {
                _fieldsStart = ref Unsafe.NullRef<IComparable[]>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int doc, float score, IComparable[] fields = null)
        {
            if (_parent.Length == 0)
                ThrowOnEmptyArray();

            if (_indexInSegment >= 0)
            {
                int offset = _indexInSegment * 2;
                Unsafe.Add(ref _intStart, offset) = doc;
                Unsafe.Add(ref Unsafe.As<int, float>(ref _intStart), offset + 1) = score;

                if (fields != null)
                {
                    Unsafe.Add(ref _fieldsStart, _indexInSegment) = fields;
                }

                _indexInSegment--;
                return;
            }

            SwitchToPreviousSegment(doc, score, fields);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SwitchToPreviousSegment(int doc, float score, IComparable[] fields = null)
        {
            _segIndex--;
            if (_segIndex < 0) ThrowWriterOutOfRange();

            var seg = _parent._segments[_segIndex];
            _intStart = ref Unsafe.As<long, int>(ref MemoryMarshal.GetArrayDataReference(seg.PackedDocsAndScores));

            if (seg.Fields != null)
            {
                _fieldsStart = ref MemoryMarshal.GetArrayDataReference(seg.Fields);
            }

            _indexInSegment = seg.Capacity - 1;

            Write(doc, score, fields); // Recursively call Write to hit the fast path
        }

        private static void ThrowOnEmptyArray() => throw new InvalidOperationException("Cannot write to an empty array");
        private static void ThrowWriterOutOfRange() => throw new IndexOutOfRangeException("Writer went below index 0");
    }

    public ScoreDocReader GetReader(int start) => new(this, start);

    public struct ScoreDocReader
    {
        private readonly ManagedScoreDocArray _managedParent;
        private long[] _currentPacked;
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
                _currentPacked = seg.PackedDocsAndScores;
                _currentFields = seg.Fields;
                _currentSegUsedCount = (_segIndex == parent._segments.Count - 1) ? parent._currentSegmentUsed : seg.Used;
            }
            else
            {
                _currentPacked = null;
                _currentFields = null;
                _currentSegUsedCount = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out int doc, out float score)
        {
            if (_indexInSegment < _currentSegUsedCount)
            {
                ref long longStart = ref MemoryMarshal.GetArrayDataReference(_currentPacked);
                ref int intStart = ref Unsafe.As<long, int>(ref longStart);
                int offset = _indexInSegment * 2;

                doc = Unsafe.Add(ref intStart, offset);
                score = Unsafe.Add(ref Unsafe.As<int, float>(ref intStart), offset + 1);

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
            _currentPacked = seg.PackedDocsAndScores;
            _currentSegUsedCount = (_segIndex == _managedParent._segments.Count - 1) ? _managedParent._currentSegmentUsed : seg.Used;

            if (_indexInSegment < _currentSegUsedCount)
            {
                ref long longStart = ref MemoryMarshal.GetArrayDataReference(_currentPacked);
                ref int intStart = ref Unsafe.As<long, int>(ref longStart);
                int offset = _indexInSegment * 2;

                doc = Unsafe.Add(ref intStart, offset);
                score = Unsafe.Add(ref Unsafe.As<int, float>(ref intStart), offset + 1);

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
            // Fast path logic duplicated to allow inlining of the "with fields" variant
            if (_indexInSegment < _currentSegUsedCount)
            {
                ref long longStart = ref MemoryMarshal.GetArrayDataReference(_currentPacked);
                ref int intStart = ref Unsafe.As<long, int>(ref longStart);
                ref IComparable[] fieldsStart = ref MemoryMarshal.GetArrayDataReference(_currentFields);

                int offset = _indexInSegment * 2;

                doc = Unsafe.Add(ref intStart, offset);
                score = Unsafe.Add(ref Unsafe.As<int, float>(ref intStart), offset + 1);
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
            _currentPacked = seg.PackedDocsAndScores;
            _currentFields = seg.Fields;
            _currentSegUsedCount = (_segIndex == _managedParent._segments.Count - 1) ? _managedParent._currentSegmentUsed : seg.Used;

            if (_indexInSegment < _currentSegUsedCount)
            {
                ref long longStart = ref MemoryMarshal.GetArrayDataReference(_currentPacked);
                ref int intStart = ref Unsafe.As<long, int>(ref longStart);

                int offset = _indexInSegment * 2;
                doc = Unsafe.Add(ref intStart, offset);
                score = Unsafe.Add(ref Unsafe.As<int, float>(ref intStart), offset + 1);
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