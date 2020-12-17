using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Lucene.Net.Util
{
    public unsafe class UnmanagedStringArray
    {
        private class Segment : IDisposable
        {
            public static long SegmentNumber;

            public readonly int Size;
            public readonly long Number;

            public byte* Start;
            public byte* CurrentPosition => Start + Used;
            public int Free => Size - Used;
            public int Used;

            public Segment(int size)
            {
                Start = (byte*) Marshal.AllocHGlobal(size);
                Used = 0;
                Size = size;
                Number = Interlocked.Increment(ref SegmentNumber);
            }

            public void Add(ushort size, out byte* position)
            {
                position = CurrentPosition;
                *(ushort*) CurrentPosition = size;

                Used += sizeof(short) + size;
            }

            public void Dispose()
            {
                GC.SuppressFinalize(this);
                if (Start != null)
                    Marshal.FreeHGlobal((IntPtr) Start);
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
            public long Owner;

            public int Size => IsNull ? 0 : *(ushort*) Start;
            public Span<byte> StringAsBytes => new Span<byte>(Start + sizeof(ushort), Size);
            public bool IsNull => Start == default;
            
            public override string ToString()
            {
                return Encoding.UTF8.GetString(StringAsBytes.ToArray());
            }

            public static int CompareOrdinal(UnmanagedString strA, UnmanagedString strB)
            {
                if (strA.IsNull && strB.IsNull)
                    return 0;

                if (strB.IsNull)
                    return 1;

                if (strA.IsNull)
                    return -1;

                return strA.StringAsBytes.SequenceCompareTo(strB.StringAsBytes);
            }

            public static int CompareOrdinal(UnmanagedString strA, string strB)
            {
                if (strA.IsNull && strB == null)
                    return 0;

                if (strB == null)
                    return 1;

                if (strA.IsNull)
                    return -1;

                Span<byte> managed = Encoding.UTF8.GetBytes(strB);
                return strA.StringAsBytes.SequenceCompareTo(managed);
            }

            public static int CompareOrdinal(string strA, UnmanagedString strB)
            {
                return -CompareOrdinal(strB, strA);
            }

            public int CompareTo(object other)
            {
                return CompareOrdinal(this, (UnmanagedString) other);
            }
        }

        private UnmanagedString[] _strings;
        private List<Segment> _segments = new List<Segment>();
        private SortedDictionary<long, Segment> _sortedSegments = new SortedDictionary<long, Segment>();

        public int Length => _index;
        public int _index = 1;

        public UnmanagedStringArray(int size)
        {
            _strings = new UnmanagedString[size];
        }

        private Segment GetSegment(int size)
        {
            if (_segments.Count == 0)
                return GetAndAddNewSegment(4096);

            // naive but simple
            var seg = _segments[_segments.Count - 1];
            if (seg.Free > size)
                return seg;

            return GetAndAddNewSegment(Math.Min(1024 * 1024, seg.Size * 2));
        }

        private Segment GetAndAddNewSegment(int segmentSize)
        {
            var newSegment = new Segment(segmentSize);
            _segments.Add(newSegment);
            _sortedSegments.Add(newSegment.Number, newSegment);
            return newSegment;
        }

        public void Add(Span<char> str)
        {
            fixed (char* s = str)
            {
                var size = (ushort) Encoding.UTF8.GetByteCount(s, str.Length);
                var segment = GetSegment(size + sizeof(ushort));

                segment.Add(size, out var position);
                fixed (byte* r = new Span<byte>(position + sizeof(ushort), size))
                {
                    Encoding.UTF8.GetBytes(s, str.Length, r, size);
                }
                _strings[_index].Start = position;
                _strings[_index].Owner = segment.Number;
                _index++;
            }
        }

        public UnmanagedString this[int position] => _strings[position];

        public void CopyTo(UnmanagedStringArray dest, int destPosition, int position)
        {
            var str = _strings[position];
            if (str.IsNull == false)
            {
                var seg = _sortedSegments[str.Owner];
                // this is unmanaged, so we need to keep the segment around!
                if (dest._sortedSegments.ContainsKey(seg.Number) == false) 
                    dest._sortedSegments.Add(seg.Number, seg);
            }

            dest._strings[destPosition] = str;
        }

        public string[] ToStrings()
        {
            var strings = new string[_strings.Length];
            for (int i = 1; i < _strings.Length; i++)
            {
                strings[i] = _strings[i].ToString();
            }

            return strings;
        }
    }
}
