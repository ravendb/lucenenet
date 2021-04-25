using System;
using System.Buffers;

namespace Lucene.Net.Memory
{
    public abstract class LuceneMemoryPool
    {
        public static LuceneMemoryPool Instance = new DefaultLuceneMemoryPool();

        public abstract IMemoryOwner<byte> RentBytes(int minSize, string stackTrace = null);

        public abstract IMemoryOwner<char> RentChars(int minSize, string stackTrace = null);

        public abstract IMemoryOwner<long> RentLongs(int minSize, string stackTrace = null);

        public abstract IMemoryOwner<int> RentInts(int minSize, bool clear = false, string stackTrace = null);
    }

    internal class DefaultLuceneMemoryPool : LuceneMemoryPool
    {
        private readonly MemoryPool<byte> _bytePool = MemoryPool<byte>.Shared;

        private readonly MemoryPool<char> _charPool = MemoryPool<char>.Shared;

        private readonly MemoryPool<long> _longPool = MemoryPool<long>.Shared;

        private readonly MemoryPool<int> _intPool = MemoryPool<int>.Shared;

        public override IMemoryOwner<byte> RentBytes(int minSize, string stackTrace = null)
        {
            return new TrackingMemoryOwner<byte>(_bytePool.Rent(minSize), stackTrace);
        }

        public override IMemoryOwner<char> RentChars(int minSize, string stackTrace = null)
        {
            return new TrackingMemoryOwner<char>(_charPool.Rent(minSize), stackTrace);
        }

        public override IMemoryOwner<long> RentLongs(int minSize, string stackTrace = null)
        {
            return new TrackingMemoryOwner<long>(_longPool.Rent(minSize), stackTrace);
        }

        public override IMemoryOwner<int> RentInts(int minSize, bool clear = false, string stackTrace = null)
        {
            var memory = _intPool.Rent(minSize);
            if (clear)
                memory.Memory.Span.Clear();

            return new TrackingMemoryOwner<int>(memory, stackTrace);
        }
    }

    public class TrackingMemoryOwner<T> : IMemoryOwner<T>
    {
        private IMemoryOwner<T> _memoryOwner;

        private readonly string _additionalStackTrace;
        private readonly string _stackTrace;
        private string _disposeStackTrace;

        private bool _disposed;

        public TrackingMemoryOwner(IMemoryOwner<T> memoryOwner, string additionalStackTrace)
        {
            _memoryOwner = memoryOwner ?? throw new ArgumentNullException(nameof(memoryOwner));
            _additionalStackTrace = additionalStackTrace;
            _stackTrace = Environment.StackTrace;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            if (_disposed)
            {

            }

            _memoryOwner?.Dispose();
            _memoryOwner = null;
            _disposed = true;
            _disposeStackTrace = Environment.StackTrace;
        }

        public Memory<T> Memory
        {
            get
            {
                if (_disposed)
                {

                }
                
                return _memoryOwner.Memory;
            }
        }

        ~TrackingMemoryOwner()
        {
            var message = $"Releasing memory from finalizer. Allocation stack: {_stackTrace}";
            Console.WriteLine(message);

            throw new InvalidOperationException(message);

            _memoryOwner?.Dispose();
            _memoryOwner = null;
        }
    }
}
