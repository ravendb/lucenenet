#if DNXCORE50
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lucene.Net.DNX
{
    public static class Win32NativeFileMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool LockFile(SafeFileHandle handle, int offsetLow, int offsetHigh, int countLow, int countHigh);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool UnlockFile(SafeFileHandle handle, int offsetLow, int offsetHigh, int countLow, int countHigh);
    }
}
#endif