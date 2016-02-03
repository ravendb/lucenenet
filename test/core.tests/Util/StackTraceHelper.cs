using System;
using System.Diagnostics;

namespace Lucene.Net.Test.Util
{
    public static class StackTraceHelper
    {
        public static StackTrace Create()
        {
#if !DNXCORE50
            return new StackTrace();
#else
            try
            {
                throw new Exception();
            }
            catch (Exception e)
            {
                return new StackTrace(e, false);
            }
#endif
        }
    }
}