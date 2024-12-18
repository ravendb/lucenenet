using System;
using System.Buffers;
using System.Threading;
using Lucene.Net.Store;

namespace Lucene.Net.Index
{
    public class ArrayHolder : IDisposable
    {
        private readonly int _size;
        private readonly Directory _directory;
        private readonly string _name;
        private readonly long[] _longArray;
        private readonly TermInfo[] _termInfoArray;
        private readonly UnmanagedIndexTerms _unmanagedIndexTerms;

        private int _usages;
        private long _managedAllocations;

        public Span<long> LongArray => _longArray.AsSpan(0, _size);
        public Span<TermInfo> InfoArray => _termInfoArray.AsSpan(0, _size);
        public UnmanagedIndexTerms UnmanagedIndexTerms => _unmanagedIndexTerms;
        public int ActualLongArraySize => _longArray.Length;

        public static Action<long> OnArrayHolderCreated;

        public static Action<long> OnArrayHolderDisposed;

        public ArrayHolder(int size, Directory directory, string name)
        {
            _size = size;
            _directory = directory;
            _name = name;
            _longArray = ArrayPool<long>.Shared.Rent(size);
            _termInfoArray = ArrayPool<TermInfo>.Shared.Rent(size);
            _unmanagedIndexTerms = new UnmanagedIndexTerms(size);
        }

        public void AddRef()
        {
            Interlocked.Increment(ref _usages);
        }

        public void ReleaseRef()
        {
            if (Interlocked.Decrement(ref _usages) == 0)
                _directory.RemoveFromTermsIndexCache(_name);
        }

        public static ArrayHolder GenerateArrayHolder(Directory directory, string name, FieldInfos fieldInfos, int readBufferSize, int indexDivisor, IState state)
        {
            var indexEnum = new SegmentTermEnum(directory.OpenInput(name, readBufferSize, state), fieldInfos, true, state);

            try
            {
                int indexSize = 1 + ((int)indexEnum.size - 1) / indexDivisor; // otherwise read index

                var holder = new ArrayHolder(indexSize, directory, name);

                for (int i = 0; indexEnum.Next(state); i++)
                {
                    holder.UnmanagedIndexTerms.Add(i, indexEnum.Field, indexEnum.TextAsSpan);
                    holder.InfoArray[i] = indexEnum.TermInfo();
                    holder.LongArray[i] = indexEnum.indexPointer;

                    for (int j = 1; j < indexDivisor; j++)
                        if (!indexEnum.Next(state))
                            break;
                }

                holder._managedAllocations = (holder.ActualLongArraySize * (TermInfo.SizeOf + sizeof(long)));

                OnArrayHolderCreated?.Invoke(holder._managedAllocations);

                return holder;

            }
            finally
            {

                indexEnum?.Close();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            OnArrayHolderDisposed?.Invoke(_managedAllocations);

            _unmanagedIndexTerms?.Dispose();

            if (_size > 256 * 1024)
                return;

            if (_longArray != null)
                ArrayPool<long>.Shared.Return(_longArray);

            if (_termInfoArray != null)
                ArrayPool<TermInfo>.Shared.Return(_termInfoArray);
        }

        ~ArrayHolder()
        {
            Dispose();
        }
    }
}
