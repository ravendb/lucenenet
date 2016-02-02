using System;
using System.Diagnostics;

namespace Lucene.Net.Test.Util
{
    public static class StackTraceHelper
    {
        public static StackTrace Create()
        {
            try
            {
                throw new Exception();
            }
            catch (Exception e)
            {
                return new StackTrace(e, false);
            }
        }
    }
}