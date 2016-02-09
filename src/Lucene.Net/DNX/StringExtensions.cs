namespace Lucene.Net
{
    using System;

    public static class StringExtensions
    {
        public static string Intern(this string input)
        {
#if !DNXCORE50
            return String.Intern(input);
#else
		    return input;
#endif
        }
    }
}