#if DNXCORE50
namespace System
{
    public class ApplicationException : Exception
    {
        public ApplicationException()
        {
        }

        public ApplicationException(string message) : base(message)
        {
        }
    }
}
#endif