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
using Lucene.Net.Store;
using NUnit.Framework;

using IndexReader = Lucene.Net.Index.IndexReader;
using LuceneTestCase = Lucene.Net.Util.LuceneTestCase;

namespace Lucene.Net.Search
{
	
    [TestFixture]
	public class TestScoreCachingWrappingScorer:LuceneTestCase
	{
		
		private sealed class SimpleScorer:Scorer
		{
			private int idx = 0;
			private int doc = - 1;
			
			public SimpleScorer():base(null)
			{
			}
			
			public override float Score(IState state)
			{
				// advance idx on purpose, so that consecutive calls to score will get
				// different results. This is to emulate computation of a score. If
				// ScoreCachingWrappingScorer is used, this should not be called more than
				// once per document.
			    return idx == scores.Length ? float.NaN : scores[idx++];
			}

			public override int DocID()
			{
				return doc;
			}
			
			public override int NextDoc(IState state)
			{
				return ++doc < scores.Length?doc:NO_MORE_DOCS;
			}
			
			public override int Advance(int target, IState state)
			{
				doc = target;
				return doc < scores.Length?doc:NO_MORE_DOCS;
			}
		}
		
		private sealed class ScoreCachingCollector:Collector
		{
			
			private int idx = 0;
			private Scorer scorer;
			internal float[] mscores;
			
			public ScoreCachingCollector(int numToCollect)
			{
				mscores = new float[numToCollect];
			}
			
			public override void  Collect(int doc, IState state)
			{
				// just a sanity check to avoid IOOB.
				if (idx == mscores.Length)
				{
					return ;
				}
				
				// just call score() a couple of times and record the score.
				mscores[idx] = scorer.Score(null);
				mscores[idx] = scorer.Score(null);
				mscores[idx] = scorer.Score(null);
				++idx;
			}
			
			public override void  SetNextReader(IndexReader reader, int docBase, IState state)
			{
			}
			
			public override void  SetScorer(Scorer scorer)
			{
				this.scorer = new ScoreCachingWrappingScorer(scorer);
			}

		    public override bool AcceptsDocsOutOfOrder
		    {
		        get { return true; }
		    }
		}
		
		private static readonly float[] scores = new float[]{0.7767749f, 1.7839992f, 8.9925785f, 7.9608946f, 0.07948637f, 2.6356435f, 7.4950366f, 7.1490803f, 8.108544f, 4.961808f, 2.2423935f, 7.285586f, 4.6699767f};
		
        [Test]
		public virtual void  TestGetScores()
		{
			
			Scorer s = new SimpleScorer();
			ScoreCachingCollector scc = new ScoreCachingCollector(scores.Length);
			scc.SetScorer(s);
			
			// We need to iterate on the scorer so that its doc() advances.
			int doc;
			while ((doc = s.NextDoc(null)) != DocIdSetIterator.NO_MORE_DOCS)
			{
				scc.Collect(doc, null);
			}
			
			for (int i = 0; i < scores.Length; i++)
			{
				Assert.AreEqual(scores[i], scc.mscores[i], 0f);
			}
		}
	}
}