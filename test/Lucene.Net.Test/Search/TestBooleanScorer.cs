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
using System.Collections.Generic;
using Lucene.Net.Store;
using NUnit.Framework;

using WhitespaceAnalyzer = Lucene.Net.Analysis.WhitespaceAnalyzer;
using Document = Lucene.Net.Documents.Document;
using Field = Lucene.Net.Documents.Field;
using IndexWriter = Lucene.Net.Index.IndexWriter;
using Term = Lucene.Net.Index.Term;
using RAMDirectory = Lucene.Net.Store.RAMDirectory;
using LuceneTestCase = Lucene.Net.Util.LuceneTestCase;

namespace Lucene.Net.Search
{
    [TestFixture]
	public class TestBooleanScorer:LuceneTestCase
	{
		private class AnonymousClassScorer:Scorer
		{
			private void  InitBlock(TestBooleanScorer enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}
			private TestBooleanScorer enclosingInstance;
			public TestBooleanScorer Enclosing_Instance
			{
				get
				{
					return enclosingInstance;
				}
				
			}
			internal AnonymousClassScorer(TestBooleanScorer enclosingInstance, Lucene.Net.Search.Similarity Param1):base(Param1)
			{
				InitBlock(enclosingInstance);
			}
			private int doc = - 1;

			public override float Score(IState state)
			{
				return 0;
			}
			public override int DocID()
			{
				return doc;
			}
			
			public override int NextDoc(IState state)
			{
			    return doc = doc == -1 ? 3000 : NO_MORE_DOCS;
			}

			public override int Advance(int target, IState state)
			{
			    return doc = target <= 3000 ? 3000 : NO_MORE_DOCS;
			}
		}
		
		/*public TestBooleanScorer(System.String name):base(name)
		{
		}*/
		
		private const System.String FIELD = "category";
		
		[Test]
		public virtual void  TestMethod()
		{
			RAMDirectory directory = new RAMDirectory();
			
			System.String[] values = new System.String[]{"1", "2", "3", "4"};
			
			try
			{
				IndexWriter writer = new IndexWriter(directory, new WhitespaceAnalyzer(), true, IndexWriter.MaxFieldLength.LIMITED, null);
				for (int i = 0; i < values.Length; i++)
				{
					Document doc = new Document();
					doc.Add(new Field(FIELD, values[i], Field.Store.YES, Field.Index.NOT_ANALYZED));
					writer.AddDocument(doc, null);
				}
				writer.Close();
				
				BooleanQuery booleanQuery1 = new BooleanQuery();
				booleanQuery1.Add(new TermQuery(new Term(FIELD, "1")), Occur.SHOULD);
				booleanQuery1.Add(new TermQuery(new Term(FIELD, "2")), Occur.SHOULD);
				
				BooleanQuery query = new BooleanQuery();
				query.Add(booleanQuery1, Occur.MUST);
				query.Add(new TermQuery(new Term(FIELD, "9")), Occur.MUST_NOT);
				
				IndexSearcher indexSearcher = new IndexSearcher(directory, true, null);
				ScoreDoc[] hits = indexSearcher.Search(query, null, 1000, null).ScoreDocs;
				Assert.AreEqual(2, hits.Length, "Number of matched documents");
			}
			catch (System.IO.IOException e)
			{
				Assert.Fail(e.Message);
			}
		}
		
		[Test]
		public virtual void  TestEmptyBucketWithMoreDocs()
		{
			// This test checks the logic of nextDoc() when all sub scorers have docs
			// beyond the first bucket (for example). Currently, the code relies on the
			// 'more' variable to work properly, and this test ensures that if the logic
			// changes, we have a test to back it up.
			
			Similarity sim = Similarity.Default;
			Scorer[] scorers = new Scorer[]{new AnonymousClassScorer(this, sim)};
			BooleanScorer bs = new BooleanScorer(sim, 1, new List<Scorer>(scorers), null, null);
			
			Assert.AreEqual(3000, bs.NextDoc(null), "should have received 3000");
			Assert.AreEqual(DocIdSetIterator.NO_MORE_DOCS, bs.NextDoc(null), "should have received NO_MORE_DOCS");
		}
	}
}