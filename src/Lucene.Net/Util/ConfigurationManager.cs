#if NETSTANDARD2_0
using Microsoft.Extensions.Configuration;

#endif

namespace Lucene.Net.Util
{
    internal static class ConfigurationManager
    {
#if NETSTANDARD2_0
        private static readonly IConfigurationRoot configuration;

        static ConfigurationManager()
        {
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true);
            configuration = builder.Build();
        }
#endif

        public static string GetAppSetting(string key)
        {
#if NETSTANDARD2_0
            return configuration[key];
#else
            return System.Configuration.ConfigurationManager.AppSettings[key];
#endif
        }
    }
}