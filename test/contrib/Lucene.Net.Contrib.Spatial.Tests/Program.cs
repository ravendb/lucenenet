using System.IO;
using NUnit.Common;
using NUnitLite;

namespace Lucene.Net.Contrib.Spatial.Tests
{
    public class Program
    {
        public static void Main(string[] args)
        {
#if !NETCOREAPP2_0
            new AutoRun().Execute(args);
#else
            new AutoRun(typeof(Program).Assembly).Execute(args, new ColorConsoleWriter(colorEnabled: true), TextReader.Null);
#endif          
        }
    }
}