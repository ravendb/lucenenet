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

using System.Buffers;
using System.Diagnostics;
using Lucene.Net.Memory;
using Lucene.Net.Store;
using Lucene.Net.Support;
using IndexInput = Lucene.Net.Store.IndexInput;

namespace Lucene.Net.Index
{

    /// <summary> Implements the skip list reader for the default posting list format
    /// that stores positions and payloads.
    /// 
    /// </summary>
    // PERF: Internal and noone is extending from it. On CoreCLR 2.0 we can achieve some devirtualization for it. 
    sealed class DefaultSkipListReader : MultiLevelSkipListReader
	{
		private bool currentFieldStoresPayloads;
		private IMemoryOwner<long> freqPointer;
		private IMemoryOwner<long> proxPointer;
		private IMemoryOwner<int> payloadLength;
		
		private long lastFreqPointer;
		private long lastProxPointer;
		private int lastPayloadLength;
		
		internal DefaultSkipListReader(IndexInput skipStream, int maxSkipLevels, int skipInterval):base(skipStream, maxSkipLevels, skipInterval)
        {
            freqPointer = LuceneMemoryPool.Instance.RentLongs(maxSkipLevels);
			proxPointer = LuceneMemoryPool.Instance.RentLongs(maxSkipLevels);
			payloadLength = LuceneMemoryPool.Instance.RentInts(maxSkipLevels);
		}
		
		internal void Init(long skipPointer, long freqBasePointer, long proxBasePointer, int df, bool storesPayloads)
		{
			base.Init(skipPointer, df);
			this.currentFieldStoresPayloads = storesPayloads;
			lastFreqPointer = freqBasePointer;
			lastProxPointer = proxBasePointer;

            for (var i = 0; i < maxNumberOfSkipLevels; i++)
            {
                freqPointer.Memory.Span[i] = freqBasePointer;
                proxPointer.Memory.Span[i] = proxBasePointer;
                payloadLength.Memory.Span[i] = 0;
            }
        }
		
		/// <summary>Returns the freq pointer of the doc to which the last call of 
		/// <see cref="MultiLevelSkipListReader.SkipTo(int)" /> has skipped.  
		/// </summary>
		internal long GetFreqPointer()
		{
			return lastFreqPointer;
		}
		
		/// <summary>Returns the prox pointer of the doc to which the last call of 
		/// <see cref="MultiLevelSkipListReader.SkipTo(int)" /> has skipped.  
		/// </summary>
		internal long GetProxPointer()
		{
			return lastProxPointer;
		}
		
		/// <summary>Returns the payload length of the payload stored just before 
		/// the doc to which the last call of <see cref="MultiLevelSkipListReader.SkipTo(int)" /> 
		/// has skipped.  
		/// </summary>
		internal int GetPayloadLength()
		{
			return lastPayloadLength;
		}
		
		protected internal override void  SeekChild(int level, IState state)
		{
			Debug.Assert(level <= maxNumberOfSkipLevels, "level <= maxNumberOfSkipLevels");

			base.SeekChild(level, state);
			freqPointer.Memory.Span[level] = lastFreqPointer;
			proxPointer.Memory.Span[level] = lastProxPointer;
			payloadLength.Memory.Span[level] = lastPayloadLength;
		}
		
		protected internal override void  SetLastSkipData(int level)
		{
            Debug.Assert(level <= maxNumberOfSkipLevels, "level <= maxNumberOfSkipLevels");

			base.SetLastSkipData(level);

			lastFreqPointer = freqPointer.Memory.Span[level];
			lastProxPointer = proxPointer.Memory.Span[level];
			lastPayloadLength = payloadLength.Memory.Span[level];
		}
		
		
		protected internal override int ReadSkipData(int level, IndexInput skipStream, IState state)
		{
            Debug.Assert(level <= maxNumberOfSkipLevels, "level <= maxNumberOfSkipLevels");

			int delta;
			if (currentFieldStoresPayloads)
			{
				// the current field stores payloads.
				// if the doc delta is odd then we have
				// to read the current payload length
				// because it differs from the length of the
				// previous payload
				delta = skipStream.ReadVInt(state);
				if ((delta & 1) != 0)
				{
					payloadLength.Memory.Span[level] = skipStream.ReadVInt(state);
				}
				delta = Number.URShift(delta, 1);
			}
			else
			{
				delta = skipStream.ReadVInt(state);
			}
			freqPointer.Memory.Span[level] += skipStream.ReadVInt(state);
			proxPointer.Memory.Span[level] += skipStream.ReadVInt(state);
			
			return delta;
		}

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

			freqPointer?.Dispose();
            freqPointer = null;

			proxPointer?.Dispose();
            proxPointer = null;

			payloadLength?.Dispose();
            payloadLength = null;
        }
    }
}