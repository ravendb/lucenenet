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
using Lucene.Net.Store;
using Lucene.Net.Support;
using Lucene.Net.Util;
using Lucene.Net.Util.Lucene4x;
using Directory = Lucene.Net.Store.Directory;

namespace Lucene.Net.Index
{

    /// <summary>This stores a monotonically increasing set of &lt;Term, TermInfo&gt; pairs in a
	/// Directory.  Pairs are accessed either by Term or by ordinal position the
	/// set.  
	/// </summary>
	
	sealed class TermInfosReader : IDisposable
	{
		private readonly Directory directory;
		private readonly String segment;
		private readonly FieldInfos fieldInfos;

        private bool isDisposed;

		private readonly LightWeightThreadLocal<ThreadResources> threadResources = new LightWeightThreadLocal<ThreadResources>();
		private readonly SegmentTermEnum origEnum;
		private readonly long size;
		
		private Span<Term> indexTerms => _holder.IndexTerms;
		private Span<TermInfo> indexInfos => _holder.InfoArray;
		private Span<long> indexPointers =>_holder.LongArray;
		
		private readonly int totalIndexInterval;
		
		private const int DEFAULT_CACHE_SIZE = 1024;
		
	    private class CloneableTerm : DoubleBarrelLRUCache.CloneableKey
	    {
	        internal Term term;

	        public CloneableTerm(Term t)
	        {
	            this.term = t;
	        }

	        public override bool Equals(object other)
	        {
	            CloneableTerm t = (CloneableTerm)other;
	            return this.term.Equals(t.term);
	        }

	        public override int GetHashCode()
	        {
	            return term.GetHashCode();
	        }

	        public override object Clone()
	        {
	            return new CloneableTerm(term);
	        }
	    }

        private class ArrayHolder : IDisposable
        {
            private readonly int _size;
            private readonly long[] _longArray;
            private readonly Term[] _termArray;
            private readonly TermInfo[] _termInfoArray;

            public ArrayHolder(int size)
            {
                _size = size;
                _longArray = ArrayPool<long>.Shared.Rent(size);
                _termArray = ArrayPool<Term>.Shared.Rent(size);
                _termInfoArray = ArrayPool<TermInfo>.Shared.Rent(size);
            }

            public Span<long> LongArray => _longArray.AsSpan(0, _size);
            public Span<TermInfo> InfoArray => _termInfoArray.AsSpan(0, _size);
            public Span<Term> IndexTerms => _termArray.AsSpan(0, _size);

            public void Dispose()
            {
                GC.SuppressFinalize(this);

                if (_longArray != null)
                    ArrayPool<long>.Shared.Return(_longArray);

                if (_termArray != null)
                    ArrayPool<Term>.Shared.Return(_termArray, clearArray: true);

                if (_termArray != null)
                    ArrayPool<TermInfo>.Shared.Return(_termInfoArray);
            }

            ~ArrayHolder()
            {
				#if DEBUG
                Console.WriteLine("Array holder is leaking...");
				#endif
				Dispose();
            }
        }

        private readonly ArrayHolder _holder;

        private readonly DoubleBarrelLRUCache<CloneableTerm, TermInfo> termInfoCache = new DoubleBarrelLRUCache<CloneableTerm, TermInfo>(DEFAULT_CACHE_SIZE);

		/// <summary> Per-thread resources managed by ThreadLocal</summary>
		private sealed class ThreadResources
		{
			internal SegmentTermEnum termEnum;
		}
		
		internal TermInfosReader(Directory dir, System.String seg, FieldInfos fis, int readBufferSize, int indexDivisor, IState state)
		{
			bool success = false;
			
			if (indexDivisor < 1 && indexDivisor != - 1)
			{
				throw new System.ArgumentException("indexDivisor must be -1 (don't load terms index) or greater than 0: got " + indexDivisor);
			}
			
			try
			{
				directory = dir;
				segment = seg;
				fieldInfos = fis;
				
				origEnum = new SegmentTermEnum(directory.OpenInput(segment + "." + IndexFileNames.TERMS_EXTENSION, readBufferSize, state), fieldInfos, false, state);
				size = origEnum.size;
				
				if (indexDivisor != - 1)
				{
					// Load terms index
					totalIndexInterval = origEnum.indexInterval * indexDivisor;
					var indexEnum = new SegmentTermEnum(directory.OpenInput(segment + "." + IndexFileNames.TERMS_INDEX_EXTENSION, readBufferSize, state), fieldInfos, true, state);
					
					try
					{
						int indexSize = 1 + ((int) indexEnum.size - 1) / indexDivisor; // otherwise read index
						
                        _holder?.Dispose();
                        _holder = new ArrayHolder(indexSize);
						
						for (int i = 0; indexEnum.Next(state); i++)
						{
							indexTerms[i] = indexEnum.Term;
							indexInfos[i] = indexEnum.TermInfo();
							indexPointers[i] = indexEnum.indexPointer;
							
							for (int j = 1; j < indexDivisor; j++)
								if (!indexEnum.Next(state))
									break;
						}
					}
					finally
					{
						indexEnum.Close();
					}
				}
				else
				{
					// Do not load terms index:
					totalIndexInterval = - 1;
                    _holder?.Dispose();
				}
				success = true;
			}
			finally
			{
				// With lock-less commits, it's entirely possible (and
				// fine) to hit a FileNotFound exception above. In
				// this case, we want to explicitly close any subset
				// of things that were opened so that we don't have to
				// wait for a GC to do so.
				if (!success)
				{
					Dispose();
				}
			}
		}

        public int SkipInterval
        {
            get { return origEnum.skipInterval; }
        }

        public int MaxSkipLevels
        {
            get { return origEnum.maxSkipLevels; }
        }

        public void Dispose()
        {
            if (isDisposed) return;

            // Move to protected method if class becomes unsealed
            if (origEnum != null)
                origEnum.Dispose();
            threadResources.Dispose();
            _holder?.Dispose();

            isDisposed = true;
        }
		
		/// <summary>Returns the number of term/value pairs in the set. </summary>
		internal long Size()
		{
			return size;
		}
		
		private ThreadResources GetThreadResources(IState state)
		{
			ThreadResources resources = threadResources.Get(state);
			if (resources == null)
			{
				resources = new ThreadResources { termEnum = Terms(state) };
				// Cache does not have to be thread-safe, it is only used by one thread at the same time
				threadResources.Set(resources);
			}
			return resources;
		}
		
		/// <summary>Returns the offset of the greatest index entry which is less than or equal to term.</summary>
		private int GetIndexOffset(Term term)
		{
			int lo = 0; // binary search indexTerms[]
			int hi = indexTerms.Length - 1;
			
			while (hi >= lo)
			{
				int mid = Number.URShift((lo + hi), 1);
				int delta = term.CompareTo(indexTerms[mid]);
				if (delta < 0)
					hi = mid - 1;
				else if (delta > 0)
					lo = mid + 1;
				else
					return mid;
			}
			return hi;
		}
		
	    internal static Term DeepCopyOf(Term other)
	    {
	        var deepCopyOfTerm = new Term(other.Field, other.Text);
            
            return deepCopyOfTerm;
	    }

        private void SeekEnum(SegmentTermEnum enumerator, int indexOffset, IState state)
		{
			enumerator.Seek(indexPointers[indexOffset], ((long)indexOffset * totalIndexInterval) - 1, indexTerms[indexOffset], indexInfos[indexOffset], state);
		}
		
		/// <summary>Returns the TermInfo for a Term in the set, or null. </summary>
		internal TermInfo Get(Term term, IState state)
		{
			return Get(term, true, state);
		}
		
		/// <summary>Returns the TermInfo for a Term in the set, or null. </summary>
		private TermInfo Get(Term term, bool useCache, IState state)
		{
			if (size == 0)
				return default;
			
			EnsureIndexIsRead();
			
			TermInfo ti;
			ThreadResources resources = GetThreadResources(state);
		    DoubleBarrelLRUCache<CloneableTerm, TermInfo> cache = null;
			
			if (useCache)
			{
				cache = termInfoCache;
				// check the cache first if the term was recently looked up
				ti = cache.Get(new CloneableTerm(DeepCopyOf(term)));
				if (ti.IsEmpty == false)
				{
					return ti;
				}
			}
			
			// optimize sequential access: first try scanning cached enum w/o seeking
			SegmentTermEnum enumerator = resources.termEnum;
			if (enumerator.Term != null && ((enumerator.Prev() != null && term.CompareTo(enumerator.Prev()) > 0) || term.CompareTo(enumerator.Term) >= 0))
			{
				int enumOffset = (int) (enumerator.position / totalIndexInterval) + 1;
				if (indexTerms.Length == enumOffset || term.CompareTo(indexTerms[enumOffset]) < 0)
				{
					// no need to seek
					
					int numScans = enumerator.ScanTo(term, state);
					if (enumerator.Term != null && term.CompareTo(enumerator.Term) == 0)
					{
						ti = enumerator.TermInfo();
						if (cache != null && numScans > 1)
						{
							// we only  want to put this TermInfo into the cache if
							// scanEnum skipped more than one dictionary entry.
							// This prevents RangeQueries or WildcardQueries to 
							// wipe out the cache when they iterate over a large numbers
							// of terms in order
							cache.Put(new CloneableTerm(DeepCopyOf(term)), ti);
						}
					}
					else
					{
						ti = default;
					}
					
					return ti;
				}
			}
			
			// random-access: must seek
			SeekEnum(enumerator, GetIndexOffset(term), state);
			enumerator.ScanTo(term, state);
			if (enumerator.Term != null && term.CompareTo(enumerator.Term) == 0)
			{
				ti = enumerator.TermInfo();
				if (cache != null)
				{
					cache.Put(new CloneableTerm(DeepCopyOf(term)), ti);
				}
			}
			else
			{
				ti = default;
			}
			return ti;
		}
						
		private void  EnsureIndexIsRead()
		{
			if (indexTerms == null)
			{
				throw new SystemException("terms index was not loaded when this reader was created");
			}
		}
		
		/// <summary>Returns the position of a Term in the set or -1. </summary>
		internal long GetPosition(Term term, IState state)
		{
			if (size == 0)
				return - 1;
			
			EnsureIndexIsRead();
			int indexOffset = GetIndexOffset(term);
			
			SegmentTermEnum enumerator = GetThreadResources(state).termEnum;
			SeekEnum(enumerator, indexOffset, state);
			
			while (term.CompareTo(enumerator.Term) > 0 && enumerator.Next(state))
			{
			}
			
			if (term.CompareTo(enumerator.Term) == 0)
				return enumerator.position;
			else
				return - 1;
		}
		
		/// <summary>Returns an enumeration of all the Terms and TermInfos in the set. </summary>
		public SegmentTermEnum Terms(IState state)
		{
			return (SegmentTermEnum) origEnum.Clone(state);
		}
		
		/// <summary>Returns an enumeration of terms starting at or after the named term. </summary>
		public SegmentTermEnum Terms(Term term, IState state)
		{
			// don't use the cache in this call because we want to reposition the
			// enumeration
			Get(term, false, state);
			return (SegmentTermEnum) GetThreadResources(state).termEnum.Clone(state);
		}
	}
}