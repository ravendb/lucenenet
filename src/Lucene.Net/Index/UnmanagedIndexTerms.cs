using System;
using System.Buffers;
using Lucene.Net.Util;

namespace Lucene.Net.Index
{
    public class UnmanagedIndexTerms : IDisposable
    {
        private readonly int _size;
        private readonly string[] _fields; // fields are interned
        private readonly UnmanagedStringArray _text;

        public int Length => _text.Length;

        public UnmanagedIndexTerms(int size)
        {
            _size = size;
            _fields = size > ArrayHolder.ArrayPoolThreshold ? new string[size] : ArrayPool<string>.Shared.Rent(size);
            _text = new UnmanagedStringArray(size, startIndex: 0);
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

            if (_size > ArrayHolder.ArrayPoolThreshold || _fields == null)
                return;

            ArrayPool<string>.Shared.Return(_fields, clearArray: true);
        }
    }
}
