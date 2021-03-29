﻿using System;
using System.Buffers;

namespace Lucene.Net.Memory
{
    public abstract class LuceneMemoryPool
    {
        public static LuceneMemoryPool Instance = new DefaultLuceneMemoryPool();

        public abstract IMemoryOwner<byte> RentBytes(int minSize, string stackTrace = null);
    }

    internal class DefaultLuceneMemoryPool : LuceneMemoryPool
    {
        private readonly MemoryPool<byte> _bytePool = MemoryPool<byte>.Shared;

        public override IMemoryOwner<byte> RentBytes(int minSize, string stackTrace = null)
        {
            return new TrackingMemoryOwner<byte>(_bytePool.Rent(minSize), stackTrace);
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