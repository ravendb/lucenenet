using System.Runtime.InteropServices;

namespace Lucene.Net.DNX
{
    public static class PlatformDetection
    {
#if DNXCORE50
        public static bool RunningOnPosix => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                                             RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#else
        public static bool RunningOnPosix = false;
#endif
    }
}