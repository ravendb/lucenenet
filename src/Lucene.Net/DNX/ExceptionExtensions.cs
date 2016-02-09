namespace Lucene.Net
{
    using System;

    public static class ExceptionExtensions
    {
        public static Exception ExtractSingleInnerException(this AggregateException e)
        {
            if (e == null)
                return null;
            while (true)
            {
                if (e.InnerExceptions.Count != 1)
                    return e;

                var aggregateException = e.InnerExceptions[0] as AggregateException;
                if (aggregateException == null)
                    break;
                e = aggregateException;
            }

            return e.InnerExceptions[0];
        }
    }
}