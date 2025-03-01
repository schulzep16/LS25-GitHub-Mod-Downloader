using System.Net.Http;

namespace LS25ModDownloader
{
    public static class HttpClientProvider
    {
        private static readonly HttpClient client = new HttpClient();

        static HttpClientProvider()
        {
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.UserAgent.ParseAdd("request");
        }

        public static HttpClient Instance => client;
    }
}
