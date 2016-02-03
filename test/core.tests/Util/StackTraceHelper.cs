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
            return Activator.CreateInstance<StackTrace>();
#endif
        }
    }
}