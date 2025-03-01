using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace LS25ModDownloader
{
    public class GitHubProject
    {
        public string Username { get; set; }
        public string Repo { get; set; }
    }

    public class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }

        [JsonProperty("assets")]
        public List<GitHubAsset> Assets { get; set; }
    }

    public class GitHubAsset
    {
        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }
    }

    public static class GitHubService
    {
        public static async Task<GitHubRelease> GetLatestReleaseAsync(HttpClient client, string username, string repoName)
        {
            try
            {
                string url = $"https://api.github.com/repos/{username}/{repoName}/releases/latest";
                return await RetryPolicy.ExecuteAsync(async () =>
                {
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<GitHubRelease>(content);
                }, 3, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Abrufen des Releases für {Username}/{RepoName}", username, repoName);
                return null;
            }
        }

        public static async Task<bool> ValidateProjectAsync(HttpClient client, string username, string repoName)
        {
            var release = await GetLatestReleaseAsync(client, username, repoName);
            return release != null;
        }
    }
}
