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
using Lucene.Net.Util;

namespace Lucene.Net.Search
{
    /// <summary> Represents hits returned by <see cref="Searcher.Search(Query,Filter,int)" />
    /// and <see cref="Searcher.Search(Query,int)" />
    /// </summary>

    [Serializable]
    public class TopDocs : IDisposable
    {
        private int _totalHits;
        private ScoreDoc[] _scoreDocs;
        private float _maxScore;

        public ManagedScoreDocArray ScoreDocArray { get; }

        /// <summary>The total number of hits for the query.</summary>
        public int TotalHits
        {
            get { return _totalHits; }
            set { _totalHits = value; }
        }

        /// <summary>
        /// Gets or sets the maximum score value encountered, needed for normalizing.
        /// Note that in case scores are not tracked, this returns <see cref="float.NaN" />.
        /// </summary>
        public float MaxScore
        {
            get { return _maxScore; }
            set { _maxScore = value; }
        }

        public TopDocs()
        {
            ScoreDocArray = new ManagedScoreDocArray();
        }

        public TopDocs(int totalHits, float maxScore, ManagedScoreDocArray scoreDocArray)
        {
            TotalHits = totalHits;
            MaxScore = maxScore;
            ScoreDocArray = scoreDocArray;
        }

        public (int Doc, float Score) GetRawValues(int index)
        {
            var cds = ScoreDocArray[index];
            return (cds.Doc, cds.Score);
        }

        /// <summary>The top hits for the query. </summary>
        /// <remarks>
        /// <para>**WARNING:** This property materializes the entire ScoreDoc collection 
        /// into a standard array, which can be **inefficient and memory-intensive** /// for large result sets. **Do not use this property in production code.**</para>
        /// <para>This property is intended only for **testing and debugging** /// or when working with small, verified result sets.</para>
        /// <para>For production use, utilize the efficient <see cref="ScoreDocArray"/> 
        /// property and its associated reader methods (like <see cref="GetRawValues(int)"/>)
        /// to access the scores without full materialization.</para>
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [Obsolete("This property is for testing/debugging ONLY and should not be used in production due to potential memory/performance issues. Use the ScoreDocArray property instead.", error: false)]
        public ScoreDoc[] ScoreDocs
        {
            get
            {
                if (_scoreDocs == null)
                {
                    var reader = ScoreDocArray.GetReader(0);
                    var list = new List<ScoreDoc>();

                    while (reader.Read(out var doc, out var score))
                    {
                        list.Add(new ScoreDoc(doc, score));
                    }

                    _scoreDocs = list.ToArray();
                }
                return _scoreDocs;
            }
        }

        public void Dispose()
        {
            ScoreDocArray?.Dispose();
        }
    }
}