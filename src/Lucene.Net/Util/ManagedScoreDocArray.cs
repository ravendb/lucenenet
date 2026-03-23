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

    public static ArrayPool<long> LongArrayPool = ArrayPool<long>.Shared;
    public static ArrayPool<IComparable[]> FieldsArrayPool = ArrayPool<IComparable[]>.Shared;

    // size of a single packed item (int doc + float score) = 8 bytes
    private const int SingleItemSize = sizeof(long);

    // 8,192 items * 8 bytes = 64KB. 
    // we keep a single array under the 85KB LOH threshold.
    private const int MaxItemsPerSegment = 64 * 1024 / SingleItemSize;

    // 128 items * 8 bytes = 1KB
    private const int InitialItems = 1 * 1024 / SingleItemSize;

    // Log2(8192) = 13
    private const int MaxItemsLog2 = 13;

    // Sequence: 128 -> 256 -> 512 -> 1024 -> 2048 -> 4096
    // The next one (8192) is the start of stable phase.
    // Sum = 128 + 256 + ... + 4096 = 8064 items.
    private const int GrowthPhaseTotalItems = 8064;

    // There are 6 segments in the growth phase (0 to 5)
    // Segment 6 is the first "Stable" (max Size) segment.
    private const int StablePhaseSegmentStartIndex = 6;

    // Log2(128) = 7. Used for shifting in growth phase.
    private const int GrowthPhaseShift = 7;

    public readonly List<Segment> _segments = new();

    // Hot path caching - points to the packed long array
    private long[] _currentPacked;
    public int _currentSegmentUsed;
    private int _currentSegmentCapacity;

    private int _length;
    public int Length => _length;

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