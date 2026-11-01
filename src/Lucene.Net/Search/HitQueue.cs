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
using Lucene.Net.Util;

namespace Lucene.Net.Search
{
	
	public sealed class HitQueue : PriorityQueue<ScoreDoc>
	{
		
		/// <summary> Creates a new instance with <c>size</c> elements. The heap is rented from a pool and goes back
		/// to it when the queue is disposed, so searches reuse the same buffers instead of allocating a fresh array
		/// each time. The queue is never pre-populated with sentinel objects; callers must check whether it is full
		/// before comparing a hit against its top.
		/// </summary>
		/// <param name="size">the requested size of this queue.</param>
		public /*internal*/ HitQueue(int size)
		{
			InitializePooled(size);
		}

	    public override bool LessThan(ScoreDoc hitA, ScoreDoc hitB)
		{
			if (hitA.Score == hitB.Score)
				return hitA.Doc > hitB.Doc;
			else
				return hitA.Score < hitB.Score;
		}
	}
}