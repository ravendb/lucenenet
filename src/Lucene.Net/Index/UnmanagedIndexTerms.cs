using Lucene.Net.Util;
using System;

namespace Lucene.Net.Index
{
    public class UnmanagedIndexTerms : IDisposable
    {
        private readonly FieldInfos _fieldInfos;
        private readonly IArray<int> _fieldNumber;
        private readonly UnmanagedStringArray _text;

        public int Length => _text.Length;

        public long TotalManagedAllocations => _text.TotalManagedAllocations + _fieldNumber.TotalManagedAllocations;

        public UnmanagedIndexTerms(int size, FieldInfos fieldInfos)
        {
            _fieldInfos = fieldInfos;
            _fieldNumber = HybridArray.Create<int>(size, UnmanagedStringArray.Type.TermCache, clear: false);
            _text = new UnmanagedStringArray(size, 0, UnmanagedStringArray.Type.TermCache, clear: false);
        }

        public void Add(int index, int fieldNumber, Span<char> textAsSpan)
        {
            _fieldNumber[index] = fieldNumber;
            _text.Add(textAsSpan);
        }

        public (string Field, UnmanagedStringArray.UnmanagedString Text) this[int position]
        {
            get => (_fieldInfos.FieldName(_fieldNumber[position]), _text[position]);
        }

        public void Dispose()
        {
            using (_fieldNumber)
            using (_text)
            {
            }
        }
    }
}
