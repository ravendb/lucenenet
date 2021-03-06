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

using SimpleAnalyzer = Lucene.Net.Analysis.SimpleAnalyzer;
using Document = Lucene.Net.Documents.Document;
using Field = Lucene.Net.Documents.Field;
using IndexReader = Lucene.Net.Index.IndexReader;
using IndexWriter = Lucene.Net.Index.IndexWriter;
using Term = Lucene.Net.Index.Term;
using RAMDirectory = Lucene.Net.Store.RAMDirectory;
using LuceneTestCase = Lucene.Net.Util.LuceneTestCase;

namespace Lucene.Net.Search
{
	
	/// <summary>Similarity unit test.</summary>
    [TestFixture]
	public class TestSimilarity:LuceneTestCase
	{
		private class AnonymousClassCollector:Collector
		{
			public AnonymousClassCollector(TestSimilarity enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			private void  InitBlock(TestSimilarity enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestSimilarity enclosingInstance;
			public TestSimilarity Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			private Scorer scorer;
			public override void  SetScorer(Scorer scorer)
			{
				this.scorer = scorer;
			}
			public override void  Collect(int doc, IState state)
			{
				Assert.AreEqual(1.0f, scorer.Score(null));
			}
			public override void  SetNextReader(IndexReader reader, int docBase, IState state)
			{
			}

		    public override bool AcceptsDocsOutOfOrder
		    {
		        get { return true; }
		    }
		}
		private class AnonymousClassCollector1:Collector
		{
			public AnonymousClassCollector1(TestSimilarity enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			private void  InitBlock(TestSimilarity enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestSimilarity enclosingInstance;
			public TestSimilarity Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			private int base_Renamed = 0;
			private Scorer scorer;
			public override void  SetScorer(Scorer scorer)
			{
				this.scorer = scorer;
			}
			public override void  Collect(int doc, IState state)
			{
				//System.out.println("Doc=" + doc + " score=" + score);
				Assert.AreEqual((float) doc + base_Renamed + 1, scorer.Score(null));
			}
			public override void  SetNextReader(IndexReader reader, int docBase, IState state)
			{
				base_Renamed = docBase;
			}

		    public override bool AcceptsDocsOutOfOrder
		    {
		        get { return true; }
		    }
		}
		private class AnonymousClassCollector2:Collector
		{
			public AnonymousClassCollector2(TestSimilarity enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			private void  InitBlock(TestSimilarity enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestSimilarity enclosingInstance;
			public TestSimilarity Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			private Scorer scorer;
			public override void  SetScorer(Scorer scorer)
			{
				this.scorer = scorer;
			}
			public override void  Collect(int doc, IState state)
			{
				//System.out.println("Doc=" + doc + " score=" + score);
				Assert.AreEqual(1.0f, scorer.Score(null));
			}
			public override void  SetNextReader(IndexReader reader, int docBase, IState state)
			{
			}

		    public override bool AcceptsDocsOutOfOrder
		    {
		        get { return true; }
		    }
		}
		private class AnonymousClassCollector3:Collector
		{
			public AnonymousClassCollector3(TestSimilarity enclosingInstance)
			{
				InitBlock(enclosingInstance);
			}
			private void  InitBlock(TestSimilarity enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestSimilarity enclosingInstance;
			public TestSimilarity Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			private Scorer scorer;
			public override void  SetScorer(Scorer scorer)
			{
				this.scorer = scorer;
			}
			public override void  Collect(int doc, IState state)
			{
				//System.out.println("Doc=" + doc + " score=" + score);
				Assert.AreEqual(2.0f, scorer.Score(null));
			}
			public override void  SetNextReader(IndexReader reader, int docBase, IState state)
			{
			}

		    public override bool AcceptsDocsOutOfOrder
		    {
		        get { return true; }
		    }
		}
		
        private class AnonymousIDFExplanation : Explanation.IDFExplanation
        {
            public override float Idf
            {
                get { return 1.0f; }
            }

            public override string Explain()
            {
                return "Inexplicable";
            }
        }

		[Serializable]
		public class SimpleSimilarity : Similarity
		{
			public override float LengthNorm(System.String field, int numTerms)
			{
				return 1.0f;
			}
			public override float QueryNorm(float sumOfSquaredWeights)
			{
				return 1.0f;
			}
			public override float Tf(float freq)
			{
				return freq;
			}
			public override float SloppyFreq(int distance)
			{
				return 2.0f;
			}
			public override float Idf(int docFreq, int numDocs)
			{
				return 1.0f;
			}
			public override float Coord(int overlap, int maxOverlap)
			{
				return 1.0f;
			}
            public override Explanation.IDFExplanation IdfExplain(System.Collections.Generic.ICollection<Term> terms, Searcher searcher, IState state)
            {
                return new AnonymousIDFExplanation();
            }
		}
		
		[Test]
		public virtual void  TestSimilarity_Renamed()
		{
			RAMDirectory store = new RAMDirectory();
			IndexWriter writer = new IndexWriter(store, new SimpleAnalyzer(), true, IndexWriter.MaxFieldLength.LIMITED, null);
			writer.SetSimilarity(new SimpleSimilarity());
			
			Document d1 = new Document();
			d1.Add(new Field("field", "a c", Field.Store.YES, Field.Index.ANALYZED));
			
			Document d2 = new Document();
			d2.Add(new Field("field", "a b c", Field.Store.YES, Field.Index.ANALYZED));
			
			writer.AddDocument(d1, null);
			writer.AddDocument(d2, null);
			writer.Optimize(null);
			writer.Close();
			
			Searcher searcher = new IndexSearcher(store, true, null);
			searcher.Similarity = new SimpleSimilarity();
			
			Term a = new Term("field", "a");
			Term b = new Term("field", "b");
			Term c = new Term("field", "c");
			
			searcher.Search(new TermQuery(b), new AnonymousClassCollector(this), null);
			
			BooleanQuery bq = new BooleanQuery();
			bq.Add(new TermQuery(a), Occur.SHOULD);
			bq.Add(new TermQuery(b), Occur.SHOULD);
			//System.out.println(bq.toString("field"));
			searcher.Search(bq, new AnonymousClassCollector1(this), null);
			
			PhraseQuery pq = new PhraseQuery();
			pq.Add(a);
			pq.Add(c);
			//System.out.println(pq.toString("field"));
			searcher.Search(pq, new AnonymousClassCollector2(this), null);
			
			pq.Slop = 2;
			//System.out.println(pq.toString("field"));
			searcher.Search(pq, new AnonymousClassCollector3(this), null);
		}
	}
}