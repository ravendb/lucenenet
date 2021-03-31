/* 
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 * 
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Buffers;
using System.Threading;
using Lucene.Net.Memory;
using Lucene.Net.Util;

namespace Lucene.Net.Store
{
	
	/// <summary>Base implementation class for buffered <see cref="IndexInput" />. </summary>
	public abstract class BufferedIndexInput : IndexInput, ILuceneCloneable
	{
		
		/// <summary>Default buffer size </summary>
		public const int BUFFER_SIZE = 1024;
		
		private int _bufferSize = BUFFER_SIZE;
		
		protected internal IMemoryOwner<byte> buffer;
		
		private long bufferStart = 0; // position in file of buffer
		private int bufferLength = 0; // end of valid bytes
		private int bufferPosition = 0; // next byte to read
		
		public override byte ReadByte(IState state)
		{
			if (bufferPosition >= bufferLength)
				Refill(state);
			return buffer.Memory.Span[bufferPosition++];
		}

        private string _stackTrace;

        private static int _counter;

	    protected BufferedIndexInput()
        {
            var counter = Interlocked.Increment(ref _counter);
			if (counter == 6)
            {

            }

            _stackTrace = counter + Environment.StackTrace;
        }
		
		/// <summary>Inits BufferedIndexInput with a specific bufferSize </summary>
		protected BufferedIndexInput(int bufferSize) : this()
		{
			CheckBufferSize(bufferSize);
			this._bufferSize = bufferSize;
		}
		
		/// <summary>Change the buffer size used by this IndexInput </summary>
		public virtual void  SetBufferSize(int newSize)
		{
			System.Diagnostics.Debug.Assert(buffer == null || buffer.Memory.Length >= _bufferSize, "buffer=" + buffer + " bufferSize=" + _bufferSize + " buffer.length=" +(buffer != null ? buffer.Memory.Length: 0));
			if (newSize != _bufferSize)
			{
				CheckBufferSize(newSize);
				_bufferSize = newSize;
				if (buffer != null)
				{
					// Resize the existing buffer and carefully save as
					// many bytes as possible starting from the current
					// bufferPosition
					IMemoryOwner<byte> newBuffer = LuceneMemoryPool.Instance.RentBytes(newSize);
					int leftInBuffer = bufferLength - bufferPosition;
					int numToCopy;
					if (leftInBuffer > newSize)
						numToCopy = newSize;
					else
						numToCopy = leftInBuffer;

					buffer.Memory.Span.Slice(bufferPosition, numToCopy).CopyTo(newBuffer.Memory.Span);
					bufferStart += bufferPosition;
					bufferPosition = 0;
					bufferLength = numToCopy;
					NewBuffer(newBuffer);
				}
			}
		}
		
		protected internal virtual void  NewBuffer(IMemoryOwner<byte> newBuffer)
		{
			buffer?.Dispose();

			// Subclasses can do something here
			buffer = newBuffer;
		}

	    /// <seealso cref="SetBufferSize">
	    /// </seealso>
	    public virtual int BufferSize
	    {
	        get { return _bufferSize; }
	    }

	    private void  CheckBufferSize(int bufferSize)
		{
			if (bufferSize <= 0)
				throw new System.ArgumentException("bufferSize must be greater than 0 (got " + bufferSize + ")");
		}
		
		public override void  ReadBytes(Span<byte> b, IState state)
		{
			ReadBytes(b, true, state);
		}
		
		public override void  ReadBytes(Span<byte> b, bool useBuffer, IState state)
		{
			var len = b.Length;
			var offset = 0;
			if (len <= (bufferLength - bufferPosition))
			{
				// the buffer contains enough data to satisfy this request
				if (len > 0)
				// to allow b to be null if len is 0...
					buffer.Memory.Span.Slice(bufferPosition, len).CopyTo(b.Slice(offset));
				bufferPosition += len;
			}
			else
			{
				// the buffer does not have enough data. First serve all we've got.
				int available = bufferLength - bufferPosition;
				if (available > 0)
				{
					buffer.Memory.Span.Slice(bufferPosition, available).CopyTo(b.Slice(offset));
					offset += available;
					len -= available;
					bufferPosition += available;
				}
				// and now, read the remaining 'len' bytes:
				if (useBuffer && len < _bufferSize)
				{
					// If the amount left to read is small enough, and
					// we are allowed to use our buffer, do it in the usual
					// buffered way: fill the buffer and copy from it:
					Refill(state);
					if (bufferLength < len)
					{
						// Throw an exception when refill() could not read len bytes:
						buffer.Memory.Span.Slice(0, bufferLength).CopyTo(b.Slice(offset));
						throw new System.IO.IOException("read past EOF");
					}
					else
					{
						buffer.Memory.Span.Slice(0, len).CopyTo(b.Slice(offset));
						bufferPosition = len;
					}
				}
				else
				{
					// The amount left to read is larger than the buffer
					// or we've been asked to not use our buffer -
					// there's no performance reason not to read it all
					// at once. Note that unlike the previous code of
					// this function, there is no need to do a seek
					// here, because there's no need to reread what we
					// had in the buffer.
					long after = bufferStart + bufferPosition + len;
					if (after > Length(state))
						throw new System.IO.IOException("read past EOF");
					ReadInternal(b.Slice(offset, len), state);
					bufferStart = after;
					bufferPosition = 0;
					bufferLength = 0; // trigger refill() on read
				}
			}
		}
		
		private void  Refill(IState state)
		{
			long start = bufferStart + bufferPosition;
			long end = start + _bufferSize;
			if (end > Length(state))
			// don't read past EOF
				end = Length(state);
			int newLength = (int) (end - start);
			if (newLength <= 0)
				throw new System.IO.IOException("read past EOF");
			
			if (buffer == null)
			{
				NewBuffer(LuceneMemoryPool.Instance.RentBytes(_bufferSize, _stackTrace)); // allocate buffer lazily
				SeekInternal(bufferStart);
			}
			ReadInternal(buffer.Memory.Span.Slice(0, newLength), state);
			bufferLength = newLength;
			bufferStart = start;
			bufferPosition = 0;
		}
		
		/// <summary>Expert: implements buffer refill.  Reads bytes from the current position
		/// in the input.
		/// </summary>
		/// <param name="b">the array to read bytes into
		/// </param>
		/// <param name="offset">the offset in the array to start storing bytes
		/// </param>
		/// <param name="length">the number of bytes to read
		/// </param>
		public abstract void  ReadInternal(Span<byte> b, IState state);

	    public override long FilePointer(IState state)
	    {
	        return bufferStart + bufferPosition;
	    }

	    public override void  Seek(long pos, IState state)
		{
			if (pos >= bufferStart && pos < (bufferStart + bufferLength))
				bufferPosition = (int) (pos - bufferStart);
			// seek within buffer
			else
			{
				bufferStart = pos;
				bufferPosition = 0;
				bufferLength = 0; // trigger refill() on read()
				SeekInternal(pos);
			}
		}
		
		/// <summary>Expert: implements seek.  Sets current position in this file, where the
		/// next <see cref="ReadInternal(byte[],int,int)" /> will occur.
		/// </summary>
		/// <seealso cref="ReadInternal(byte[],int,int)">
		/// </seealso>
		public abstract void  SeekInternal(long pos);
		
		public override System.Object Clone(IState state)
		{
			BufferedIndexInput clone = (BufferedIndexInput) base.Clone(state);
			
			clone.buffer = null;
			clone.bufferLength = 0;
			clone.bufferPosition = 0;
			clone.bufferStart = FilePointer(state);
			
			return clone;
		}

        protected override void Dispose(bool disposing)
        {
			buffer?.Dispose();
        }
    }
}