using System;
using System.Diagnostics;

namespace Lucene.Net.Test.Util
{
    public static class StackTraceHelper
    {
        public static StackTrace Create()
        {
            return new StackTrace();
        }
    }
}