using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LS25ModDownloader
{
    public class GitHubProject
    {
        public required string Username { get; set; }
        public required string Repo { get; set; }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    public class GitHubAsset
    {
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }

    public static class GitHubService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<GitHubRelease?> GetLatestReleaseAsync(HttpClient client, string username, string repoName, CancellationToken cancellationToken = default)
        {
            try
            {
                string url = $"https://api.github.com/repos/{username}/{repoName}/releases/latest";
                return await RetryPolicy.ExecuteAsync(async () =>
                {
                    var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken);
                }, 3, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Abrufen des Releases für {Username}/{RepoName}", username, repoName);
                return null;
            }
        }

        public static async Task<bool> ValidateProjectAsync(HttpClient client, string username, string repoName, CancellationToken cancellationToken = default)
        {
            var release = await GetLatestReleaseAsync(client, username, repoName, cancellationToken);
            return release != null;
        }
    }
}
