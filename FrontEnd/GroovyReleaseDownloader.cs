using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace MiSTerCast
{
    public class GroovyReleaseInfo
    {
        public string TagName { get; set; }
        public string Name { get; set; }
        public string PublishedAt { get; set; }
        public string CacheDirectory { get; set; }
        public string MisterAssetName { get; set; }
        public string MisterAssetUrl { get; set; }
        public string RbfAssetName { get; set; }
        public string RbfAssetUrl { get; set; }
        public string MisterBinaryPath { get; set; }
        public string GroovyRbfPath { get; set; }

        public string DisplayName
        {
            get
            {
                if (String.IsNullOrWhiteSpace(Name))
                    return TagName ?? "";
                if (String.IsNullOrWhiteSpace(TagName))
                    return Name;
                return Name + " (" + TagName + ")";
            }
        }

        public bool IsCached
        {
            get
            {
                bool misterCached = String.IsNullOrWhiteSpace(MisterBinaryPath) || File.Exists(MisterBinaryPath);
                return misterCached && File.Exists(GroovyRbfPath);
            }
        }
    }

    class GroovyReleaseDownloader
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/iequalshane/Groovy_MiSTer/releases/latest";

        public string CacheRoot { get; private set; }

        public GroovyReleaseDownloader()
            : this(GetDefaultCacheRoot())
        {
        }

        public GroovyReleaseDownloader(string cacheRoot)
        {
            CacheRoot = cacheRoot;
        }

        public async Task<GroovyReleaseInfo> DownloadLatestAsync(Action<string, bool> log)
        {
            Log(log, "Checking latest Groovy_MiSTer release...");
            GroovyReleaseInfo release = await GetLatestReleaseAsync();
            Directory.CreateDirectory(release.CacheDirectory);

            if (String.IsNullOrWhiteSpace(release.MisterAssetUrl))
            {
                Log(log, "Latest Groovy_MiSTer release does not include MiSTer_groovy; keeping the configured/local binary path.");
            }
            else if (!File.Exists(release.MisterBinaryPath))
            {
                Log(log, "Downloading " + release.MisterAssetName + "...");
                await DownloadFileAsync(release.MisterAssetUrl, release.MisterBinaryPath);
            }
            else
            {
                Log(log, "Using cached " + release.MisterAssetName + ".");
            }

            if (!File.Exists(release.GroovyRbfPath))
            {
                Log(log, "Downloading " + release.RbfAssetName + "...");
                await DownloadFileAsync(release.RbfAssetUrl, release.GroovyRbfPath);
            }
            else
            {
                Log(log, "Using cached " + release.RbfAssetName + ".");
            }

            Log(log, "Groovy_MiSTer release ready: " + release.DisplayName);
            return release;
        }

        public async Task<GroovyReleaseInfo> GetLatestReleaseAsync()
        {
            EnsureTls12();
            using (var client = CreateWebClient())
            {
                string json = await client.DownloadStringTaskAsync(LatestReleaseUrl);
                return ParseLatestReleaseJson(json, CacheRoot);
            }
        }

        public static GroovyReleaseInfo ParseLatestReleaseJson(string json, string cacheRoot)
        {
            if (String.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("GitHub release response was empty.");

            GitHubReleaseResponse response;
            var serializer = new DataContractJsonSerializer(typeof(GitHubReleaseResponse));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                response = (GitHubReleaseResponse)serializer.ReadObject(stream);
            }

            if (response == null)
                throw new InvalidOperationException("GitHub release response could not be parsed.");

            List<GitHubReleaseAsset> assets = response.Assets ?? new List<GitHubReleaseAsset>();
            GitHubReleaseAsset misterAsset = assets.FirstOrDefault(a =>
                String.Equals(a.Name, "MiSTer_groovy", StringComparison.OrdinalIgnoreCase));
            GitHubReleaseAsset rbfAsset = assets
                .Where(a => a.Name != null &&
                    a.Name.StartsWith("Groovy", StringComparison.OrdinalIgnoreCase) &&
                    a.Name.EndsWith(".rbf", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (rbfAsset == null || String.IsNullOrWhiteSpace(rbfAsset.BrowserDownloadUrl))
                throw new InvalidOperationException("Latest Groovy_MiSTer release does not include a Groovy RBF.");

            string tag = String.IsNullOrWhiteSpace(response.TagName) ? "latest" : response.TagName;
            string cacheDirectory = Path.Combine(cacheRoot, SanitizePathSegment(tag));
            string misterAssetName = misterAsset == null ? "" : misterAsset.Name;
            return new GroovyReleaseInfo
            {
                TagName = response.TagName,
                Name = response.Name,
                PublishedAt = response.PublishedAt,
                CacheDirectory = cacheDirectory,
                MisterAssetName = misterAssetName,
                MisterAssetUrl = misterAsset == null ? "" : misterAsset.BrowserDownloadUrl,
                RbfAssetName = rbfAsset.Name,
                RbfAssetUrl = rbfAsset.BrowserDownloadUrl,
                MisterBinaryPath = String.IsNullOrWhiteSpace(misterAssetName) ? "" : Path.Combine(cacheDirectory, misterAssetName),
                GroovyRbfPath = Path.Combine(cacheDirectory, rbfAsset.Name)
            };
        }

        private static async Task DownloadFileAsync(string url, string destinationPath)
        {
            EnsureTls12();
            string tempPath = destinationPath + ".tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using (var client = CreateWebClient())
            {
                await client.DownloadFileTaskAsync(url, tempPath);
            }

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(tempPath, destinationPath);
        }

        private static WebClient CreateWebClient()
        {
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "MiSTerCast";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static string GetDefaultCacheRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiSTerCast",
                "Groovy");
        }

        private static string SanitizePathSegment(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "latest";

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder();
            foreach (char c in value)
                builder.Append(invalid.Contains(c) ? '_' : c);
            return builder.ToString();
        }

        private static void EnsureTls12()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        private static void Log(Action<string, bool> log, string message)
        {
            if (log != null)
                log(message, false);
        }

        [DataContract]
        private class GitHubReleaseResponse
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "published_at")]
            public string PublishedAt { get; set; }

            [DataMember(Name = "assets")]
            public List<GitHubReleaseAsset> Assets { get; set; }
        }

        [DataContract]
        private class GitHubReleaseAsset
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}
