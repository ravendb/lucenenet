#if DNXCORE50
using System.Runtime.InteropServices;

namespace Lucene.Net.DNX
{
    public class PosixNativeFileMethods
    {
        internal const string LIBC_6 = "libc.so.6";

        [DllImport(LIBC_6, SetLastError = true)]
        public static extern int flock(long fd, int operation);
    }
    
    public class PosixFileLockOptions
    {
        public const int LOCK_SH = 0x01; /* shared file lock */
        public const int LOCK_EX = 0x02; /* exclusive file lock */
        public const int LOCK_NB = 0x04; /* don't block when locking */
        public const int LOCK_UN = 0x08; /* unlock file */
    }
}
#endif