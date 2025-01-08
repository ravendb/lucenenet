using Microsoft.Extensions.Configuration;

namespace Lucene.Net.Util
{
    internal static class ConfigurationManager
    {
        private static readonly IConfigurationRoot configuration;

        static ConfigurationManager()
        {
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true);
            configuration = builder.Build();
        }

        public static string GetAppSetting(string key)
        {
            return configuration[key];
        }
    }
}