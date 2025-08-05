using System;
using System.Threading;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace Lucene.Net.Index
{
    public class ArrayHolder : IDisposable
    {
        private readonly Directory _directory;
        private readonly string _name;
        private readonly IArray<long> _indexPointers;
        private readonly IArray<TermInfo> _termInfos;
        private readonly UnmanagedIndexTerms _unmanagedIndexTerms;

        private int _usages;
        private long _managedAllocations;

        public IArray<long> IndexPointers => _indexPointers;
        public IArray<TermInfo> TermInfos => _termInfos;
        public UnmanagedIndexTerms UnmanagedIndexTerms => _unmanagedIndexTerms;

        public static Action<long> OnArrayHolderCreated;

        public static Action<long> OnArrayHolderDisposed;

        public long TotalManagedAllocations => _indexPointers.TotalManagedAllocations + _termInfos.TotalManagedAllocations + _unmanagedIndexTerms.TotalManagedAllocations;

        public ArrayHolder(int size, Directory directory, string name, FieldInfos fieldInfos)
        {
            _directory = directory;
            _name = name;

            _indexPointers = HybridArray.Create<long>(size, UnmanagedStringArray.Type.TermCache);
            _termInfos = HybridArray.Create<TermInfo>(size, UnmanagedStringArray.Type.TermCache);

            _unmanagedIndexTerms = new UnmanagedIndexTerms(size, fieldInfos);
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

                var holder = new ArrayHolder(indexSize, directory, name, fieldInfos);

                for (int i = 0; indexEnum.Next(state); i++)
                {
                    holder.UnmanagedIndexTerms.Add(i, indexEnum.FieldNumber, indexEnum.TextAsSpan);
                    holder.TermInfos[i] = indexEnum.TermInfo();
                    holder.IndexPointers[i] = indexEnum.indexPointer;

                    for (int j = 1; j < indexDivisor; j++)
                        if (!indexEnum.Next(state))
                            break;
                }

                holder._managedAllocations = holder.TotalManagedAllocations;

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

            using (_unmanagedIndexTerms)
            using (_indexPointers)
            using (_termInfos)
            {
                OnArrayHolderDisposed?.Invoke(_managedAllocations);
            }
        }

        ~ArrayHolder()
        {
            Dispose();
        }
    }
}
