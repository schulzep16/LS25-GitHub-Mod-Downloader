using System;
using System.Net.Http;

namespace LS25ModDownloader
{
    public static class HttpClientProvider
    {
        private static readonly HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static HttpClientProvider()
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LS25-GitHub-Mod-Downloader/1.0");
        }

        public static HttpClient Instance => client;
    }
}
