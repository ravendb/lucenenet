using System.Runtime.InteropServices;

namespace Lucene.Net.DNX
{
    public static class PlatformDetection
    {
        public static bool RunningOnPosix => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                                             RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }
}