using System.Runtime.CompilerServices;

namespace Lucene.Net.Test.Util
{
    public static class TestMethodHelper
    {
        public static string CallerName([CallerMemberName]string caller = "")
        {
            return caller;
        }
    }
}