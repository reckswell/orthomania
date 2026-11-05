using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Releases
{
    /// <summary>
    /// orthomania's GitHub releases. Their tags say what each is:
    /// <code>
    /// lazer-2026.1026.0-0.1.0   build 0.1.0 for lazer 2026.1026.0: orthomania.json and the files it lists
    /// patcher-0.1.0             the patcher itself
    /// </code>
    /// Drafts are skipped. Nothing downloaded here is trusted until <see cref="BuildStore.Add"/> has checked it against its manifest,
    /// and the manifest must name the release and build its tag does.
    /// </summary>
    public sealed class GitHubReleaseFeed : IReleaseFeed
    {
        public const string BUILD_TAG_PREFIX = "lazer-";
        public const string PATCHER_TAG_PREFIX = "patcher-";

        /// <summary>
        /// No build file is anywhere near this; a larger download is not one.
        /// </summary>
        public const long MAX_FILE_SIZE = 64 * 1024 * 1024;

        private readonly HttpClient http;
        private readonly string repository;
        private readonly Uri api;

        /// <param name="repository"><c>owner/name</c>.</param>
        /// <param name="api">GitHub's API, or a stand-in for tests.</param>
        public GitHubReleaseFeed(HttpClient http, string repository, Uri? api = null)
        {
            this.http = http;
            this.repository = repository;
            this.api = api ?? new Uri("https://api.github.com/");
        }

        public static HttpClient CreateHttpClient(Version patcherVersion)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("orthomania-patcher", patcherVersion.ToString(3)));
            return client;
        }

        public async Task<PublishedBuild?> FindBuildAsync(LazerVersion lazer, CancellationToken cancellationToken)
        {
            var releases = await getReleasesAsync(cancellationToken).ConfigureAwait(false);

            return releases.Select(asBuild)
                           .OfType<PublishedBuild>()
                           .Where(b => b.Lazer == lazer)
                           .OrderByDescending(b => b.Build, Comparer<string>.Create(RulesetBuild.CompareBuilds))
                           .FirstOrDefault();
        }

        public async Task<PublishedPatcher?> FindPatcherAsync(CancellationToken cancellationToken)
        {
            var releases = await getReleasesAsync(cancellationToken).ConfigureAwait(false);

            return releases.Where(r => r.TagName.StartsWith(PATCHER_TAG_PREFIX, StringComparison.Ordinal) && !r.Prerelease)
                           .Select(r => Version.TryParse(r.TagName[PATCHER_TAG_PREFIX.Length..], out var v) && r.HtmlUrl != null
                               ? new PublishedPatcher(v, r.TagName, r.HtmlUrl)
                               : null)
                           .OfType<PublishedPatcher>()
                           .OrderByDescending(p => p.Version)
                           .FirstOrDefault();
        }

        public async Task DownloadAsync(PublishedBuild build, string staging, Action<double> progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(staging);

            if (!build.Files.TryGetValue(ReleaseManifest.FILE_NAME, out var manifestUri))
                throw new ReleaseFeedException($"{build} has no {ReleaseManifest.FILE_NAME}.");

            string manifestPath = Path.Combine(staging, ReleaseManifest.FILE_NAME);
            await downloadAsync(manifestUri, manifestPath, null, _ => { }, cancellationToken).ConfigureAwait(false);

            ReleaseManifest manifest;

            try
            {
                manifest = ReleaseManifest.Read(manifestPath);
            }
            catch (Exception e) when (e is InvalidDataException or JsonException)
            {
                throw new ReleaseFeedException($"{build} has an unreadable manifest ({e.Message}).", e);
            }

            if (manifest.LazerVersion != build.Lazer || manifest.Build != build.Build)
                throw new ReleaseFeedException($"{build}'s manifest is for orthomania {manifest.Build} on osu! {manifest.Lazer}. Not using it.");

            long total = Math.Max(1, manifest.Files.Sum(f => f.Size));
            long done = 0;

            foreach (var file in manifest.Files)
            {
                if (!build.Files.TryGetValue(file.Name, out var uri))
                    throw new ReleaseFeedException($"{build} lists {file.Name} but does not publish it.");

                if (file.Size > MAX_FILE_SIZE)
                    throw new ReleaseFeedException($"{build} lists {file.Name} at {file.Size} bytes, more than any build file. Not downloading it.");

                long before = done;
                await downloadAsync(uri, Path.Combine(staging, file.Name), file.Size, read => progress(Math.Min(1, (before + read) / (double)total)), cancellationToken)
                    .ConfigureAwait(false);
                done += file.Size;
            }

            progress(1);
        }

        private async Task downloadAsync(Uri uri, string path, long? expectedSize, Action<long> progress, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = File.Create(path);

                byte[] buffer = new byte[81920];
                long read = 0;
                long limit = expectedSize ?? MAX_FILE_SIZE;

                while (true)
                {
                    int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                    if (count == 0)
                        break;

                    read += count;

                    if (read > limit)
                        throw new ReleaseFeedException($"{Path.GetFileName(path)} is larger than its release says. Not using it.");

                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    progress(read);
                }
            }
            catch (HttpRequestException e)
            {
                throw new ReleaseFeedException($"Could not download {Path.GetFileName(path)} ({e.Message}).", e);
            }
            catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ReleaseFeedException($"Downloading {Path.GetFileName(path)} timed out.", e);
            }
        }

        private IReadOnlyList<GitHubRelease>? cached;
        private DateTime cachedAt;

        /// <summary>
        /// The release list, asked for at most once a minute (GitHub allows 60 unauthenticated requests an hour).
        /// </summary>
        private async Task<IReadOnlyList<GitHubRelease>> getReleasesAsync(CancellationToken cancellationToken)
        {
            if (cached != null && DateTime.UtcNow - cachedAt < TimeSpan.FromMinutes(1))
                return cached;

            cached = await fetchReleasesAsync(cancellationToken).ConfigureAwait(false);
            cachedAt = DateTime.UtcNow;
            return cached;
        }

        private async Task<IReadOnlyList<GitHubRelease>> fetchReleasesAsync(CancellationToken cancellationToken)
        {
            var uri = new Uri(api, $"repos/{repository}/releases?per_page=50");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? new List<GitHubRelease>()).Where(r => !r.Draft).ToList();
            }
            catch (HttpRequestException e)
            {
                throw new ReleaseFeedException($"Could not reach orthomania's releases ({e.Message}).", e);
            }
            catch (JsonException e)
            {
                throw new ReleaseFeedException($"orthomania's release list could not be read ({e.Message}).", e);
            }
            catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ReleaseFeedException("orthomania's releases did not answer in time.", e);
            }
        }

        /// <summary>
        /// A release tagged <c>lazer-&lt;release&gt;-&lt;build&gt;</c> with its files, or null for any other release.
        /// </summary>
        internal static PublishedBuild? asBuild(GitHubRelease release)
        {
            if (!TryParseBuildTag(release.TagName, out var lazer, out string build))
                return null;

            var files = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in release.Assets)
            {
                if (asset.Name.Length > 0 && asset.BrowserDownloadUrl != null && asset.BrowserDownloadUrl.Scheme == Uri.UriSchemeHttps)
                    files[asset.Name] = asset.BrowserDownloadUrl;
            }

            if (!files.ContainsKey(ReleaseManifest.FILE_NAME))
                return null;

            return new PublishedBuild(lazer, build, release.TagName, files);
        }

        public static string BuildTag(LazerVersion lazer, string build) => $"{BUILD_TAG_PREFIX}{lazer.Number}-{build}";

        public static bool TryParseBuildTag(string tag, out LazerVersion lazer, out string build)
        {
            lazer = null!;
            build = string.Empty;

            if (!tag.StartsWith(BUILD_TAG_PREFIX, StringComparison.Ordinal))
                return false;

            string rest = tag[BUILD_TAG_PREFIX.Length..];
            int dash = rest.IndexOf('-');

            if (dash <= 0 || dash == rest.Length - 1)
                return false;

            if (!LazerVersion.TryParse(rest[..dash], out lazer) || lazer.Suffix.Length > 0)
                return false;

            build = rest[(dash + 1)..];
            return ReleaseManifest.IsSafeBuildName(build);
        }

        internal sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("draft")]
            public bool Draft { get; set; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [JsonPropertyName("html_url")]
            public Uri? HtmlUrl { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubAsset> Assets { get; set; } = new List<GitHubAsset>();
        }

        internal sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public Uri? BrowserDownloadUrl { get; set; }

            [JsonPropertyName("size")]
            public long Size { get; set; }
        }
    }
}
