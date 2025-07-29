using System;
using System.Buffers;
using Lucene.Net.Util;

namespace Lucene.Net.Index
{
    public class UnmanagedIndexTerms : IDisposable
    {
        private readonly string[] _fields; // fields are interned
        private readonly UnmanagedStringArray _text;

        private static readonly int ReferenceSize = IntPtr.Size == sizeof(int) ? 4 : 8;

        public int Length => _text.Length;

        public long TotalManagedAllocations => _text.TotalManagedAllocations + _fields.Length * ReferenceSize;

        public UnmanagedIndexTerms(int size)
        {
            _fields = ArrayPool<string>.Shared.Rent(size);
            _text = new UnmanagedStringArray(size, 0, UnmanagedStringArray.Type.TermCache);
        }

        public void Add(int index, string field, Span<char> textAsSpan)
        {
            _fields[index] = field;
            _text.Add(textAsSpan);
        }

        public UnmanagedTerm this[int position]
        {
            get => new UnmanagedTerm(_fields[position], _text[position]);
        }

        public void Dispose()
        {
            _text?.Dispose();

            if (_fields != null)
                ArrayPool<string>.Shared.Return(_fields, clearArray: true);
        }
    }
}
