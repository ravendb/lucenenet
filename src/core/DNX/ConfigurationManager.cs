#if DNXCORE50
using Microsoft.Extensions.Configuration;
#endif

namespace Lucene.Net
{
    public static class ConfigurationManager
    {
#if DNXCORE50
        private static readonly IConfigurationRoot configuration;

        static ConfigurationManager()
        {
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true);
            configuration = builder.Build();
        }
#endif

        public static string GetAppSetting(string key)
        {
#if DNXCORE50
            return configuration[key];
#else
            return System.Configuration.ConfigurationManager.AppSettings[key];
#endif
        }
    }
}