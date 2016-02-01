namespace Lucene.Net.Test.Util
{
    using System.Collections;

    public static class BitArrayExtensions
    {
        public static BitArray Clone(this BitArray bits)
        {
            return new BitArray(bits);
        }
    }
}