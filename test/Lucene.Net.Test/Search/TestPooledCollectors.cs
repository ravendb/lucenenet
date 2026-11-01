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
using System.Linq;
using NUnit.Framework;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Directory = Lucene.Net.Store.Directory;
using LuceneTestCase = Lucene.Net.Util.LuceneTestCase;

namespace Lucene.Net.Search
{
	/// <summary>
	/// The top-docs collectors rent their per-hit storage (the hit queue heap and the comparator slot arrays) from a
	/// pool at the requested size and return it when they are disposed, and the score collector no longer pre-populates
	/// its queue with sentinel objects. These tests pin what that relies on: a queue that never fills up because fewer
	/// documents match than slots were requested (a query without a limit), a request for zero hits, storage returned by
	/// one search and rented by the next, the string value comparator that clears only the slots it wrote, and disposal.
	/// </summary>
	[TestFixture]
	public class TestPooledCollectors : LuceneTestCase
	{
		private const int NumDocs = 200;
		private const int GroupSize = NumDocs / 4;

		public TestPooledCollectors()
		{
		}

		public TestPooledCollectors(System.String name) : base(name)
		{
		}

		/// <summary>
		/// 200 documents in 4 groups of 50. Every sortable value runs opposite to the document order, so a sorted search
		/// has to reorder everything and a stale slot would stand out.
		/// </summary>
		private static Directory BuildIndex()
		{
			var dir = new RAMDirectory();
			var writer = new IndexWriter(dir, new KeywordAnalyzer(), IndexWriter.MaxFieldLength.UNLIMITED, null);
			for (int i = 0; i < NumDocs; i++)
			{
				var doc = new Document();
				doc.Add(new Field("id", i.ToString(), Field.Store.YES, Field.Index.NOT_ANALYZED_NO_NORMS));
				doc.Add(new Field("group", (i % 4).ToString(), Field.Store.NO, Field.Index.NOT_ANALYZED_NO_NORMS));
				doc.Add(new Field("value", NumericUtils.LongToPrefixCoded(NumDocs - i), Field.Store.NO, Field.Index.NOT_ANALYZED_NO_NORMS));
				doc.Add(new Field("name", "name-" + (NumDocs - i).ToString("D4"), Field.Store.NO, Field.Index.NOT_ANALYZED_NO_NORMS));
				writer.AddDocument(doc, null);
			}
			writer.Commit(null);
			writer.Dispose();
			return dir;
		}

		private static Query Group(int group)
		{
			return new TermQuery(new Term("group", group.ToString()));
		}

		private static int[] Ids(IndexSearcher searcher, ScoreDoc[] hits)
		{
			return hits.Select(h => int.Parse(searcher.Doc(h.Doc, null).Get("id", null))).ToArray();
		}

		private static int[] ExpectedGroupIdsDescending(int group)
		{
			return Enumerable.Range(0, NumDocs).Where(i => i % 4 == group).OrderByDescending(i => i).ToArray();
		}

		[Test]
		public virtual void ScoreCollectorWithMoreSlotsThanHits()
		{
			// a query without a limit asks for MaxDoc slots; with 50 matches the queue never fills up, so the results
			// are exactly the hits, ordered by score and then by doc id, and nothing stands in for the empty slots
			using (var dir = BuildIndex())
			{
				var searcher = new IndexSearcher(dir, true, null);
				foreach (var inOrder in new[] { true, false })
				{
					using (var collector = TopScoreDocCollector.Create(NumDocs * 10, inOrder))
					{
						searcher.Search(Group(1), collector, null);
						Assert.AreEqual(GroupSize, collector.TotalHits);

						var topDocs = collector.TopDocs();
						Assert.AreEqual(GroupSize, topDocs.TotalHits);
						var hits = topDocs.ScoreDocs;
						Assert.AreEqual(GroupSize, hits.Length);
						Assert.AreEqual(hits[0].Score, topDocs.MaxScore);
						for (int i = 1; i < hits.Length; i++)
						{
							Assert.IsTrue(hits[i - 1].Score >= hits[i].Score, "scores must not increase");
							if (hits[i - 1].Score == hits[i].Score)
								Assert.Less(hits[i - 1].Doc, hits[i].Doc, "equal scores are ordered by doc id");
						}
						// all the hits share the same term, so the order is simply the doc order
						CollectionAssert.AreEqual(Enumerable.Range(0, NumDocs).Where(i => i % 4 == 1).ToArray(), Ids(searcher, hits));
					}

					// a page from the middle of a queue that never filled up
					using (var collector = TopScoreDocCollector.Create(NumDocs * 10, inOrder))
					{
						searcher.Search(Group(1), collector, null);
						var hits = collector.TopDocs(10, 20).ScoreDocs;
						Assert.AreEqual(20, hits.Length);
						CollectionAssert.AreEqual(Enumerable.Range(0, NumDocs).Where(i => i % 4 == 1).Skip(10).Take(20).ToArray(), Ids(searcher, hits));
					}
				}
				searcher.Dispose();
			}
		}

		[Test]
		public virtual void ScoreCollectorWithZeroSlotsOnlyCounts()
		{
			using (var dir = BuildIndex())
			{
				var searcher = new IndexSearcher(dir, true, null);
				foreach (var inOrder in new[] { true, false })
				{
					using (var collector = TopScoreDocCollector.Create(0, inOrder))
					{
						searcher.Search(Group(2), collector, null);
						Assert.AreEqual(GroupSize, collector.TotalHits);
						Assert.AreEqual(0, collector.TopDocs().ScoreDocs.Length);
					}
				}
				searcher.Dispose();
			}
		}

		[Test]
		public virtual void SortedSearchWithMoreSlotsThanHits()
		{
			// the field collector's queue and comparator slots are sized for MaxDoc * 10 but hold 50 hits
			using (var dir = BuildIndex())
			{
				var searcher = new IndexSearcher(dir, true, null);
				var sorts = new[]
				{
					new Sort(new SortField("value", SortField.LONG)),
					new Sort(new SortField("name", SortField.STRING)),
					new Sort(new SortField("name", SortField.STRING_VAL)),
					new Sort(new[] { new SortField("value", SortField.LONG), new SortField("name", SortField.STRING_VAL) }),
				};
				foreach (var sort in sorts)
				{
					using (var topDocs = searcher.Search(Group(3), null, NumDocs * 10, sort, false, null))
					{
						Assert.AreEqual(GroupSize, topDocs.TotalHits, sort.ToString());
						// the sort values run opposite to the doc order, so ascending value means descending id
						CollectionAssert.AreEqual(ExpectedGroupIdsDescending(3), Ids(searcher, topDocs.ScoreDocs), sort.ToString());
					}

					using (var topDocs = searcher.Search(Group(3), null, NumDocs * 10, new Sort(Reverse(sort)), false, null))
					{
						CollectionAssert.AreEqual(Enumerable.Reverse(ExpectedGroupIdsDescending(3)).ToArray(), Ids(searcher, topDocs.ScoreDocs), "reverse " + sort);
					}
				}
				searcher.Dispose();
			}
		}

		private static SortField[] Reverse(Sort sort)
		{
			return sort.GetSort().Select(f => new SortField(f.Field, f.Type, !f.Reverse)).ToArray();
		}

		[Test]
		public virtual void StorageReturnedByOneSearchIsSafeForTheNext()
		{
			// the first search fills 200 slots; the following ones, on the same thread, rent that storage back and use
			// a fraction of it. A comparator that read a slot it did not write, or a heap slot left pointing at an old
			// entry, would corrupt the order of the smaller result sets.
			using (var dir = BuildIndex())
			{
				var searcher = new IndexSearcher(dir, true, null);
				foreach (var type in new[] { SortField.LONG, SortField.STRING, SortField.STRING_VAL })
				{
					var field = type == SortField.LONG ? "value" : "name";

					using (var all = searcher.Search(new MatchAllDocsQuery(), null, NumDocs * 10, new Sort(new SortField(field, type)), false, null))
					{
						Assert.AreEqual(NumDocs, all.ScoreDocs.Length);
						CollectionAssert.AreEqual(Enumerable.Range(0, NumDocs).Reverse().ToArray(), Ids(searcher, all.ScoreDocs));
					}

					using (var fewer = searcher.Search(Group(0), null, NumDocs * 10, new Sort(new SortField(field, type, true)), false, null))
					{
						Assert.AreEqual(GroupSize, fewer.ScoreDocs.Length);
						CollectionAssert.AreEqual(Enumerable.Reverse(ExpectedGroupIdsDescending(0)).ToArray(), Ids(searcher, fewer.ScoreDocs));
					}

					var handful = new BooleanQuery();
					foreach (var id in new[] { 7, 42, 101, 150, 199 })
						handful.Add(new TermQuery(new Term("id", id.ToString())), Occur.SHOULD);
					using (var few = searcher.Search(handful, null, NumDocs * 10, new Sort(new SortField(field, type)), false, null))
					{
						CollectionAssert.AreEqual(new[] { 199, 150, 101, 42, 7 }, Ids(searcher, few.ScoreDocs));
					}
				}

				// the score collector's heap goes through the same pool
				for (int round = 0; round < 3; round++)
				{
					using (var collector = TopScoreDocCollector.Create(NumDocs * 10, true))
					{
						searcher.Search(Group(round), collector, null);
						CollectionAssert.AreEqual(Enumerable.Range(0, NumDocs).Where(i => i % 4 == round).ToArray(), Ids(searcher, collector.TopDocs().ScoreDocs));
					}
				}
				searcher.Dispose();
			}
		}

		[Test]
		public virtual void DisposingCollectorsIsSafeInAnyOrder()
		{
			using (var dir = BuildIndex())
			{
				var searcher = new IndexSearcher(dir, true, null);

				// disposed twice, after the results were read
				var scoreCollector = TopScoreDocCollector.Create(10, true);
				searcher.Search(Group(0), scoreCollector, null);
				Assert.AreEqual(10, scoreCollector.TopDocs().ScoreDocs.Length);
				scoreCollector.Dispose();
				scoreCollector.Dispose();

				// disposed without the results ever being read
				var fieldCollector = TopFieldCollector.Create(new Sort(new SortField("name", SortField.STRING_VAL)), NumDocs * 10, false, true, false, true);
				searcher.Search(Group(0), fieldCollector, null);
				fieldCollector.Dispose();
				fieldCollector.Dispose();

				// a queue that was never searched
				using (TopScoreDocCollector.Create(10, false))
				{
				}
				using (new HitQueue(NumDocs))
				{
				}

				// and the searcher still works afterwards
				using (var topDocs = searcher.Search(Group(0), null, NumDocs * 10, new Sort(new SortField("value", SortField.LONG)), false, null))
				{
					Assert.AreEqual(GroupSize, topDocs.TotalHits);
				}
				searcher.Dispose();
			}
		}

		[Test]
		public virtual void HitQueueSizeIsTheNumberOfHitsNotTheBound()
		{
			// without sentinels the queue reports how many hits it holds, which is what TopDocs() reads out
			using (var queue = new HitQueue(1000))
			{
				Assert.AreEqual(0, queue.Size());
				for (int i = 0; i < 50; i++)
					Assert.IsNull(queue.InsertWithOverflow(new ScoreDoc(i, i)));
				Assert.AreEqual(50, queue.Size());
				Assert.AreEqual(0, queue.Top().Doc);
				Assert.AreEqual(0f, queue.Top().Score);
			}
		}
	}
}
