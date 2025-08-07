using Lucene.Net.Index;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Lucene.Net.Util
{
    public unsafe class UnmanagedStringArray : IDisposable
    {
        public enum Type
        {
            TermCache,
            Sorting
        }

        public class Segment: IDisposable
        {
            public readonly int Size;
            private readonly Type _type;

            public byte* Start;
            public byte* CurrentPosition => Start + Used;
            public int Free => Size - Used;
            public int Used;

            public delegate byte* AllocateSegmentDelegate(long size, Type type);
            public delegate void FreeSegmentDelegate(byte* ptr, long size, Type type);

            public static AllocateSegmentDelegate AllocateMemory = (size, _) => (byte*) Marshal.AllocHGlobal((IntPtr) size);
            public static FreeSegmentDelegate FreeMemory = (ptr, _, __) => Marshal.FreeHGlobal((IntPtr) ptr);

            public Segment(int size, Type type)
            {
                Start = AllocateMemory(size, type);
                Used = 0;
                Size = size;
                _type = type;
            }

            public byte* Add(int size)
            {
                var position = CurrentPosition;
                *(int*)CurrentPosition = size;

                Used += sizeof(int) + size;
                if (Used > Size)
                    ThrowOutOfRange(size);

                return position;
            }

            public void Return(int amountToReturn)
            {
                Used -= amountToReturn;

                if (Used < 0)
                    ThrowUnderflow();
            }

            private void ThrowOutOfRange(int size)
            {
                throw new ArgumentOutOfRangeException(nameof(Used), $"Add operation failed: Requested size {size}, Used: {Used}, Max Size: {Size}");
            }

            private void ThrowUnderflow()
            {
                throw new InvalidOperationException($"Return operation failed: Memory usage underflow. Current Used: {Used}");
            }

            public void Dispose()
            {
                GC.SuppressFinalize(this);
                if (Start != null)
                {
                    FreeMemory(Start, Size, _type);
                }
                Start = null;
            }

            ~Segment()
            {
                Dispose();
            }
        }

        public struct UnmanagedString : IComparable
        {
            public byte* Start;

            public int Size => *(int*)Start >> 1;
            public bool StoredAsAscii => (*(int*)Start & 1) == 1;
            public Span<byte> StringAsBytes => new Span<byte>(Start + sizeof(int), Size);
            public Span<char> StringAsChars => new Span<char>(Start + sizeof(int), Size);
            public bool IsNull => Start == default;
            
            public override string ToString()
            {
                if (IsNull)
                    return string.Empty;

                return StoredAsAscii ? Encoding.UTF8.GetString(StringAsBytes) : new string(StringAsChars);
            }

            public static int CompareOrdinal(UnmanagedString strA, UnmanagedString strB)
            {
                if (strA.IsNull && strB.IsNull)
                    return 0;

                if (strB.IsNull)
                    return 1;

                if (strA.IsNull)
                    return -1;

                return strA.StoredAsAscii switch
                {
                    true when strB.StoredAsAscii => strA.StringAsBytes.SequenceCompareTo(strB.StringAsBytes),
                    false when strB.StoredAsAscii == false => strA.StringAsChars.SequenceCompareTo(strB.StringAsChars),
                    false when strB.StoredAsAscii => CompareChars(strA.StringAsChars, strB.StringAsBytes),
                    true when strB.StoredAsAscii == false => -CompareChars(strB.StringAsChars, strA.StringAsBytes),
                    _ => ThrowNotHandledCase(strA, strB)
                };
            }

            private static int CompareChars(Span<char> stringAsChars, Span<byte> asciiAsBytes)
            {
                var minLength = Math.Min(stringAsChars.Length, asciiAsBytes.Length);

                for (var i = 0; i < minLength; i++)
                {
                    var utfChar = stringAsChars[i];
                    var asciiChar = (char)asciiAsBytes[i];

                    if (utfChar != asciiChar)
                        return utfChar - asciiChar;
                }

                return stringAsChars.Length - asciiAsBytes.Length;
            }

            private static int ThrowNotHandledCase(UnmanagedString strA, UnmanagedString strB)
            {
                // shouldn't happen
                throw new ArgumentOutOfRangeException($"strA stored as ascii {strA.StoredAsAscii}, strB stored as ascii {strB.StoredAsAscii}");
            }

            public static int CompareOrdinal(UnmanagedString strA, Span<byte> strBAsBytes, ReadOnlySpan<char> strBAsChars)
            {
                if (strA.IsNull)
                    return -1;

                if (strA.StoredAsAscii == false)
                    return strA.StringAsChars.SequenceCompareTo(strBAsChars);

                // the following comparison works correctly for UTF-8 encoded strings when:
                // - comparing ASCII characters (0-127) with each other (single byte comparison)
                // - comparing ASCII with non-ASCII (ASCII uses single byte starting with '0',
                //   while non-ASCII starts with bytes >=192, so non-ASCII is always greater)
                // therefore, comparing the byte sequences directly produces the correct result.
                return strA.StringAsBytes.SequenceCompareTo(strBAsBytes);
            }

            public static int CompareOrdinal(Span<byte> strA, ReadOnlySpan<char> aAsChars, UnmanagedString strB)
            {
                return -CompareOrdinal(strB, strA, aAsChars);
            }

            public int CompareTo(object other)
            {
                if (other == null)
                    return IsNull ? 0 : 1;

                if (other is UnmanagedString us)
                    return CompareOrdinal(this, us);

                if (other is string s)
                {
                    byte[] arr = null;
                    Span<byte> stringAsBytes;
                    var stringAsSpan = s.AsSpan();

                    var size = Encoding.UTF8.GetMaxByteCount(s.Length);
                    if (size <= 256) // allocate on the stack
                    {
                        Span<byte> stackAlloc = stackalloc byte[size];
                        Encoding.UTF8.TryGetBytes(stringAsSpan, stackAlloc, out var bytesWritten);
                        stringAsBytes = stackAlloc.Slice(0, bytesWritten);
                    }
                    else
                    {
                        var pooledSize = BitUtil.NextHighestPowerOfTwo(size);
                        arr = ArrayPool<byte>.Shared.Rent(pooledSize);
                        Encoding.UTF8.TryGetBytes(stringAsSpan, arr, out var bytesWritten);
                        stringAsBytes = new Span<byte>(arr, 0, bytesWritten);
                    }

                    try
                    {
                        return CompareOrdinal(this, stringAsBytes, stringAsSpan);
                    }
                    finally
                    {
                        if (arr != null)
                            ArrayPool<byte>.Shared.Return(arr);
                    }
                }

                throw new ArgumentException($"Unknown type {other.GetType()} for comparison");
            }
        }

        private HybridArray<UnmanagedString> _strings;
        private List<Segment> _segments = new List<Segment>();

        public int Length => _index;
        public int _index;
        private readonly Type _type;

        public long TotalManagedAllocations => _strings.TotalManagedAllocations;

        public UnmanagedStringArray(int size, int startIndex, Type type, bool clear = false)
        {
            _strings = new HybridArray<UnmanagedString>(size, type, clear);
            _index = startIndex;
            _type = type;
        }

        private Segment GetSegment(int size)
        {
            if (_segments.Count == 0)
            {
                var firstSegmentSize = AdjustSegmentSize(4096, size);
                return GetAndAddNewSegment(firstSegmentSize);
            }

            // naive but simple
            var seg = _segments[^1];
            if (seg.Free > size)
                return seg;

            var segmentSize = Math.Min(1024 * 1024, seg.Size * 2);
            segmentSize = AdjustSegmentSize(segmentSize, size);

            return GetAndAddNewSegment(segmentSize);
        }

        private static int AdjustSegmentSize(int segmentSize, int size)
        {
            if (size > segmentSize)
            {
                // too big, make it 4KB aligned so if there is wasted space in the end, it is just
                // a single memory page. In most cases, if we have one big size, we'll have a lot, so we
                // want to avoid power of two here
                segmentSize = ((size / 4096) + (size % 4096 == 0 ? 0 : 1)) * 4096;
            }

            return segmentSize;
        }

        private Segment GetAndAddNewSegment(int segmentSize)
        {
            var newSegment = new Segment(segmentSize, _type);
            _segments.Add(newSegment);
            return newSegment;
        }

        public void Add(Span<char> str)
        {
            // attempt to encode as ascii bytes
            var sizeForBytes = str.Length + sizeof(int);  // assume 1 byte per char for ascii
            var segment = GetSegment(sizeForBytes);
            var pos = segment.Add(str.Length);
            var outputByteBuffer = new Span<byte>(pos + sizeof(int), str.Length);
            if (Encoding.UTF8.TryGetBytes(str, outputByteBuffer, out var bytesWritten))
            {
                // all characters are ascii, store as bytes
                *((int*)pos) = bytesWritten << 1 | 1;  // 1 flag for ascii
            }
            else
            {
                // the destination was too small to contain all the encoded bytes
                // which means that it contains non-ascii, revert and process as chars
                segment.Return(sizeForBytes);

                var sizeForChars = str.Length * sizeof(char) + sizeof(int);
                if (segment.Free < sizeForChars)
                {
                    segment = GetSegment(sizeForChars);
                }

                pos = segment.Add(str.Length * sizeof(char));

                var outputBuffer = new Span<char>(pos + sizeof(int), str.Length);
                str.CopyTo(outputBuffer);

                *((int*)pos) = str.Length << 1 | 0; // 0 flag for chars
            }

            _strings[_index].Start = pos;
            _index++;
        }

        public void AddDeleted(TermBuffer termBuffer)
        {
            // since we are doing a binary search, we must keep the order of the terms

            if (_index == 1)
            {
                // we must allocate the first one
                Add(termBuffer.TextAsSpan);
                return;
            }

            _strings[_index].Start = _strings[_index - 1].Start;
            _index++;
        }

        public void SetAsNull(int i)
        {
            _strings[i] = new UnmanagedString { Start = null };
        }

        public UnmanagedString this[int position]
        {
            get => _strings[position];
        }

        public void Dispose()
        {
            using (_strings)
            {
                foreach (var segment in _segments)
                {
                    segment.Dispose();
                }

                _segments.Clear();
            }
        }
    }
}
