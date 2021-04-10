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
using System.Diagnostics;
using Lucene.Net.Memory;
using Lucene.Net.Store;
using BufferedIndexInput = Lucene.Net.Store.BufferedIndexInput;
using IndexInput = Lucene.Net.Store.IndexInput;

namespace Lucene.Net.Index
{
	
	/// <summary> This abstract class reads skip lists with multiple levels.
	/// 
	/// See <see cref="MultiLevelSkipListWriter" /> for the information about the encoding 
	/// of the multi level skip lists. 
	/// 
	/// Subclasses must implement the abstract method <see cref="ReadSkipData(int, IndexInput)" />
	/// which defines the actual format of the skip data.
	/// </summary>
	abstract class MultiLevelSkipListReader : IDisposable
	{
		// the maximum number of skip levels possible for this index
		protected readonly int maxNumberOfSkipLevels;
		
		// number of levels in this skip list
		private int numberOfSkipLevels;
		
		// Expert: defines the number of top skip levels to buffer in memory.
		// Reducing this number results in less memory usage, but possibly
		// slower performance due to more random I/Os.
		// Please notice that the space each level occupies is limited by
		// the skipInterval. The top level can not contain more than
		// skipLevel entries, the second top level can not contain more
		// than skipLevel^2 entries and so forth.
		private const int numberOfLevelsToBuffer = 1;

		private int docCount;
		private bool haveSkipped;

	    private bool isDisposed;
		
		private readonly IndexInput[] skipStream; // skipStream for each level
		private IMemoryOwner<long> skipPointer; // the start pointer of each skip level
		private IMemoryOwner<int> skipInterval; // skipInterval of each level
		private IMemoryOwner<int> numSkipped; // number of docs skipped per level
		
		private IMemoryOwner<int> skipDoc; // doc id of current skip entry per level 
		private int lastDoc; // doc id of last read skip entry with docId <= target
		private IMemoryOwner<long> childPointer; // child pointer of current skip entry per level
		private long lastChildPointer; // childPointer of last read skip entry with docId <= target
		
		private readonly bool inputIsBuffered;


		protected MultiLevelSkipListReader(IndexInput skipStream, int maxSkipLevels, int skipInterval)
		{
            this.maxNumberOfSkipLevels = maxSkipLevels;
			this.skipStream = new IndexInput[maxSkipLevels];
            this.skipPointer = LuceneMemoryPool.Instance.RentLongs(maxSkipLevels);
			this.childPointer = LuceneMemoryPool.Instance.RentLongs(maxSkipLevels);
			this.numSkipped = LuceneMemoryPool.Instance.RentInts(maxSkipLevels);
			this.skipInterval = LuceneMemoryPool.Instance.RentInts(maxSkipLevels);
			this.skipStream[0] = skipStream;
			this.inputIsBuffered = (skipStream is BufferedIndexInput);
			this.skipInterval.Memory.Span[0] = skipInterval;
			for (int i = 1; i < maxSkipLevels; i++)
			{
				// cache skip intervals
				this.skipInterval.Memory.Span[i] = this.skipInterval.Memory.Span[i - 1] * skipInterval;
			}
			skipDoc = LuceneMemoryPool.Instance.RentInts(maxSkipLevels);
		}
		
		
		/// <summary>Returns the id of the doc to which the last call of <see cref="SkipTo(int)" />
		/// has skipped.  
		/// </summary>
		internal virtual int GetDoc()
		{
			return lastDoc;
		}
		
		
		/// <summary>Skips entries to the first beyond the current whose document number is
		/// greater than or equal to <i>target</i>. Returns the current doc count. 
		/// </summary>
		internal virtual int SkipTo(int target, IState state)
		{
			if (!haveSkipped)
			{
				// first time, load skip levels
				LoadSkipLevels(state);
				haveSkipped = true;
			}
			
			// walk up the levels until highest level is found that has a skip
			// for this target
			int level = 0;
			while (level < numberOfSkipLevels - 1 && target > skipDoc.Memory.Span[level + 1])
			{
				level++;
			}
			
			while (level >= 0)
			{
				if (target > skipDoc.Memory.Span[level])
				{
					if (!LoadNextSkip(level, state))
					{
						continue;
					}
				}
				else
				{
					// no more skips on this level, go down one level
					if (level > 0 && lastChildPointer > skipStream[level - 1].FilePointer(state))
					{
						SeekChild(level - 1, state);
					}
					level--;
				}
			}
			
			return numSkipped.Memory.Span[0] - skipInterval.Memory.Span[0] - 1;
		}
		
		private bool LoadNextSkip(int level, IState state)
		{
            Debug.Assert(level <= maxNumberOfSkipLevels, "level <= maxNumberOfSkipLevels");

			// we have to skip, the target document is greater than the current
			// skip list entry        
			SetLastSkipData(level);
			
			numSkipped.Memory.Span[level] += skipInterval.Memory.Span[level];
			
			if (numSkipped.Memory.Span[level] > docCount)
			{
				// this skip list is exhausted
				skipDoc.Memory.Span[level] = System.Int32.MaxValue;
				if (numberOfSkipLevels > level)
					numberOfSkipLevels = level;
				return false;
			}
			
			// read next skip entry
			skipDoc.Memory.Span[level] += ReadSkipData(level, skipStream[level], state);
			
			if (level != 0)
			{
				// read the child pointer if we are not on the leaf level
				childPointer.Memory.Span[level] = skipStream[level].ReadVLong(state) + skipPointer.Memory.Span[level - 1];
			}
			
			return true;
		}
		
		/// <summary>Seeks the skip entry on the given level </summary>
		protected internal virtual void  SeekChild(int level, IState state)
		{
			skipStream[level].Seek(lastChildPointer, state);
			numSkipped.Memory.Span[level] = numSkipped.Memory.Span[level + 1] - skipInterval.Memory.Span[level + 1];
			skipDoc.Memory.Span[level] = lastDoc;
			if (level > 0)
			{
				childPointer.Memory.Span[level] = skipStream[level].ReadVLong(state) + skipPointer.Memory.Span[level - 1];
			}
		}

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (isDisposed) return;

            if (disposing)
            {
				childPointer?.Dispose();
                childPointer = null;

				numSkipped?.Dispose();
                numSkipped = null;

				skipDoc?.Dispose();
                skipDoc = null;

				skipInterval?.Dispose();
                skipInterval = null;

				skipPointer?.Dispose();
                skipPointer = null;

				skipStream[0]?.Dispose(); // initial skip stream

                for (int i = 1; i < skipStream.Length; i++)
                {
                    if (skipStream[i] != null)
                    {
                        skipStream[i].Close();
                    }
                }
            }

            isDisposed = true;
        }
		
		/// <summary>initializes the reader </summary>
		internal virtual void  Init(long skipPointer, int df)
		{
			this.skipPointer.Memory.Span[0] = skipPointer;
			this.docCount = df;

			skipDoc.Memory.Span.Clear();
			numSkipped.Memory.Span.Clear();
			childPointer.Memory.Span.Clear();

            haveSkipped = false;
			for (int i = 1; i < numberOfSkipLevels; i++)
			{
				skipStream[i]?.Dispose();
				skipStream[i] = null;
			}
		}
		
		/// <summary>Loads the skip levels  </summary>
		private void  LoadSkipLevels(IState state)
		{
			numberOfSkipLevels = docCount == 0?0:(int) System.Math.Floor(System.Math.Log(docCount) / System.Math.Log(skipInterval.Memory.Span[0]));
			if (numberOfSkipLevels > maxNumberOfSkipLevels)
			{
				numberOfSkipLevels = maxNumberOfSkipLevels;
			}
			
			skipStream[0].Seek(skipPointer.Memory.Span[0], state);
			
			int toBuffer = numberOfLevelsToBuffer;
			
			for (int i = numberOfSkipLevels - 1; i > 0; i--)
			{
				// the length of the current level
				long length = skipStream[0].ReadVLong(state);
				
				// the start pointer of the current level
				skipPointer.Memory.Span[i] = skipStream[0].FilePointer(state);
				if (toBuffer > 0)
				{
					// buffer this level
					skipStream[i]?.Dispose();
					skipStream[i] = new SkipBuffer(skipStream[0], (int) length, state);
					toBuffer--;
				}
				else
				{
					// clone this stream, it is already at the start of the current level
					skipStream[i]?.Dispose();
					skipStream[i] = (IndexInput) skipStream[0].Clone(state);
					if (inputIsBuffered && length < BufferedIndexInput.BUFFER_SIZE)
					{
						((BufferedIndexInput) skipStream[i]).SetBufferSize((int) length);
					}
					
					// move base stream beyond the current level
					skipStream[0].Seek(skipStream[0].FilePointer(state) + length, state);
				}
			}
			
			// use base stream for the lowest level
			skipPointer.Memory.Span[0] = skipStream[0].FilePointer(state);
		}
		
		/// <summary> Subclasses must implement the actual skip data encoding in this method.
		/// 
		/// </summary>
		/// <param name="level">the level skip data shall be read from
		/// </param>
		/// <param name="skipStream">the skip stream to read from
		/// </param>
		protected internal abstract int ReadSkipData(int level, IndexInput skipStream, IState state);
		
		/// <summary>Copies the values of the last read skip entry on this level </summary>
		protected internal virtual void  SetLastSkipData(int level)
		{
			lastDoc = skipDoc.Memory.Span[level];
			lastChildPointer = childPointer.Memory.Span[level];
		}
		
		
		/// <summary>used to buffer the top skip levels </summary>
		private sealed class SkipBuffer : IndexInput
		{
			private IMemoryOwner<byte> data;
			private readonly long pointer;
            private readonly int length;
            private int pos;

		    private bool isDisposed;
			
			internal SkipBuffer(IndexInput input, int length, IState state)
			{
				data = LuceneMemoryPool.Instance.RentBytes(length);
				pointer = input.FilePointer(state);
				input.ReadBytes(data.Memory.Span.Slice(0, length), state);
                this.length = length;
            }

            protected override void Dispose(bool disposing)
            {
                if (isDisposed) return;
                if (disposing)
                {
					data?.Dispose();
                    data = null;
                }

                isDisposed = true;
            }

		    public override long FilePointer(IState state)
		    {
		        return pointer + pos;
		    }

		    public override long Length(IState state)
			{
				return length;
			}
			
			public override byte ReadByte(IState state)
			{
				return data.Memory.Span[pos++];
			}
			
			public override void  ReadBytes(Span<byte> b, IState state)
			{
				data.Memory.Span.Slice(pos, b.Length).CopyTo(b);
				pos += b.Length;
			}
			
			public override void  Seek(long pos, IState state)
			{
				this.pos = (int) (pos - pointer);
			}
			
			public override System.Object Clone(IState state)
			{
                System.Diagnostics.Debug.Fail("Port issue:", "Lets see if we need this FilterIndexReader.Clone()"); // {{Aroush-2.9}}
				return null;
			}
		}
	}
}