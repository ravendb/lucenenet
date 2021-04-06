using System;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;

namespace Lucene.Net.Search
{
    public class NonDisposableSearcher : Searcher
    {
        private readonly Searcher _searcher;

        public NonDisposableSearcher(Searcher searcher)
        {
            _searcher = searcher ?? throw new ArgumentNullException(nameof(searcher));
        }

        public override void Search(Weight weight, Filter filter, Collector results, IState state)
        {
            _searcher.Search(weight, filter, results, state);
        }

        protected override void Dispose(bool disposing)
        {
        }

        public override int DocFreq(Term term, IState state)
        {
            return _searcher.DocFreq(term, state);
        }

        public override int MaxDoc => _searcher.MaxDoc;

        public override TopDocs Search(Weight weight, Filter filter, int n, IState state)
        {
            return _searcher.Search(weight, filter, n, state);
        }

        public override Document Doc(int i, IState state)
        {
            return _searcher.Doc(i, state);
        }

        public override Document Doc(int docid, FieldSelector fieldSelector, IState state)
        {
            return _searcher.Doc(docid, fieldSelector, state);
        }

        public override Query Rewrite(Query query, IState state)
        {
            return _searcher.Rewrite(query, state);
        }

        public override Explanation Explain(Weight weight, int doc, IState state)
        {
            return _searcher.Explain(weight, doc, state);
        }

        public override TopFieldDocs Search(Weight weight, Filter filter, int n, Sort sort, IState state)
        {
            return _searcher.Search(weight, filter, n, sort, state);
        }
    }
}