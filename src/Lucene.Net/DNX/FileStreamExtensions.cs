#if DNXCORE50
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Lucene.Net.DNX
{
    public static class FileStreamExtensions
    {
        public static void Lock(this FileStream stream, long position, long length)
        {
            if (position < 0 || length < 0)
                throw new ArgumentOutOfRangeException((position < 0 ? "position" : "length"), "Neither position not length cannot be negative");
            
            if (stream.SafeFileHandle.IsClosed)
                throw new InvalidOperationException("File is closed");

            int positionLow = unchecked((int)(position));
            int positionHigh = unchecked((int)(position >> 32));
            int lengthLow = unchecked((int)(length));
            int lengthHigh = unchecked((int)(length >> 32));

            if (Win32NativeFileMethods.LockFile(stream.SafeFileHandle, positionLow, positionHigh, lengthLow, lengthHigh) == false)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public static void Unlock(this FileStream stream, long position, long length)
        {
            if (position < 0 || length < 0)
                throw new ArgumentOutOfRangeException((position < 0 ? "position" : "length"), "Neither position not length cannot be negative");

            if (stream.SafeFileHandle.IsClosed)
                throw new InvalidOperationException("File is closed");

            int positionLow = unchecked((int)(position));
            int positionHigh = unchecked((int)(position >> 32));
            int lengthLow = unchecked((int)(length));
            int lengthHigh = unchecked((int)(length >> 32));

            if (Win32NativeFileMethods.UnlockFile(stream.SafeFileHandle, positionLow, positionHigh, lengthLow, lengthHigh) == false)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
#endif