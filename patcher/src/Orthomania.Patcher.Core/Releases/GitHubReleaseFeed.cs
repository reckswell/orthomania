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
    /// patcher-0.1.0             the patcher itself: orthomania.exe
    /// </code>
    /// The newest releases are listed in one request; a release older than that list (a build for an osu! release that has not changed
    /// for a while, or a patcher among many builds) is found by its tag. Drafts are skipped. Nothing downloaded here is trusted until <see cref="BuildStore.Add"/> has checked it against its manifest,
    /// and the manifest must name the release and build its tag does.
    /// </summary>
    public sealed class GitHubReleaseFeed : IReleaseFeed
    {
        public const string BUILD_TAG_PREFIX = "lazer-";
        public const string PATCHER_TAG_PREFIX = "patcher-";

        /// <summary>
        /// The file a patcher release carries, which a released patcher updates itself to.
        /// </summary>
        public const string PATCHER_ASSET = "orthomania.exe";

        /// <summary>
        /// Releases listed at once, newest first.
        /// </summary>
        public const int LISTED = 100;

        /// <summary>
        /// Tags tried, newest first, when a release is older than the list: a draft (a build that failed) has a tag but no release.
        /// </summary>
        private const int tags_tried = 3;

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

            var listed = releases.Select(asBuild)
                                 .OfType<PublishedBuild>()
                                 .Where(b => b.Lazer == lazer)
                                 .OrderByDescending(b => b.Build, Comparer<string>.Create(RulesetBuild.CompareBuilds))
                                 .FirstOrDefault();

            if (listed != null)
                return listed;

            return await findByTagAsync($"{BUILD_TAG_PREFIX}{lazer.Number}-",
                tag => TryParseBuildTag(tag, out var l, out string build) && l == lazer ? build : null,
                RulesetBuild.CompareBuilds,
                release => asBuild(release) is { } b && b.Lazer == lazer ? b : null,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<PublishedPatcher?> FindPatcherAsync(CancellationToken cancellationToken)
        {
            var releases = await getReleasesAsync(cancellationToken).ConfigureAwait(false);

            var listed = releases.Select(asPatcher)
                                 .OfType<PublishedPatcher>()
                                 .OrderByDescending(p => p.Version)
                                 .FirstOrDefault();

            if (listed != null)
                return listed;

            return await findByTagAsync(PATCHER_TAG_PREFIX,
                tag => tryParsePatcherTag(tag, out var version) ? version.ToString() : null,
                (a, b) => Version.Parse(a).CompareTo(Version.Parse(b)),
                asPatcher,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task DownloadPatcherAsync(PatcherDownload download, string path, Action<double> progress, CancellationToken cancellationToken)
        {
            if (download.Size <= 0 || download.Size > PatcherExecutable.MAX_SIZE)
                throw new ReleaseFeedException($"The patcher is published at {download.Size} bytes, which no orthomania.exe is. Not downloading it.");

            if (download.Uri.Scheme != Uri.UriSchemeHttps)
                throw new ReleaseFeedException("The patcher's download is not over https. Not downloading it.");

            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            await downloadAsync(download.Uri, path, download.Size, read => progress(Math.Min(1, read / (double)download.Size)), cancellationToken).ConfigureAwait(false);
            progress(1);
        }

        /// <summary>
        /// A release with a tag starting <paramref name="prefix"/> that <paramref name="parse"/> accepts, trying the newest tags first by
        /// what <paramref name="key"/> reads from each. For releases older than the list holds.
        /// </summary>
        private async Task<T?> findByTagAsync<T>(string prefix, Func<string, string?> key, Comparison<string> compareKeys, Func<GitHubRelease, T?> parse,
                                                 CancellationToken cancellationToken)
            where T : class
        {
            var tags = (await getTagsAsync(prefix, cancellationToken).ConfigureAwait(false))
                       .Select(tag => (Tag: tag, Key: key(tag)))
                       .Where(t => t.Key != null)
                       .OrderByDescending(t => t.Key!, Comparer<string>.Create(compareKeys))
                       .Take(tags_tried)
                       .ToList();

            foreach (var (tag, _) in tags)
            {
                var release = await getReleaseAsync(tag, cancellationToken).ConfigureAwait(false);

                if (release is { Draft: false } && parse(release) is T found)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// A release tagged <c>patcher-&lt;version&gt;</c>, with its <see cref="PATCHER_ASSET"/> when GitHub gives that file's hash.
        /// </summary>
        internal static PublishedPatcher? asPatcher(GitHubRelease release)
        {
            if (release.Prerelease || release.HtmlUrl == null || !tryParsePatcherTag(release.TagName, out var version))
                return null;

            var asset = release.Assets.FirstOrDefault(a => a.Name.Equals(PATCHER_ASSET, StringComparison.OrdinalIgnoreCase));
            PatcherDownload? download = null;

            if (asset?.BrowserDownloadUrl is { } uri && uri.Scheme == Uri.UriSchemeHttps && asset.Size > 0
                && asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                string sha256 = digest["sha256:".Length..].ToLowerInvariant();

                if (sha256.Length == 64 && sha256.All(Uri.IsHexDigit))
                    download = new PatcherDownload(uri, asset.Size, sha256);
            }

            return new PublishedPatcher(version, release.TagName, release.HtmlUrl) { Download = download };
        }

        private static bool tryParsePatcherTag(string tag, out Version version)
        {
            version = null!;
            return tag.StartsWith(PATCHER_TAG_PREFIX, StringComparison.Ordinal) && Version.TryParse(tag[PATCHER_TAG_PREFIX.Length..], out version!);
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
            var releases = await getJsonAsync<List<GitHubRelease>>($"repos/{repository}/releases?per_page={LISTED}", cancellationToken).ConfigureAwait(false);
            return (releases ?? new List<GitHubRelease>()).Where(r => !r.Draft).ToList();
        }

        private readonly Dictionary<string, (DateTime At, IReadOnlyList<string> Tags)> cachedTags = new Dictionary<string, (DateTime, IReadOnlyList<string>)>();

        /// <summary>
        /// Every tag starting with <paramref name="prefix"/>, asked for at most once a minute.
        /// </summary>
        private async Task<IReadOnlyList<string>> getTagsAsync(string prefix, CancellationToken cancellationToken)
        {
            if (cachedTags.TryGetValue(prefix, out var cachedPrefix) && DateTime.UtcNow - cachedPrefix.At < TimeSpan.FromMinutes(1))
                return cachedPrefix.Tags;

            var refs = await getJsonAsync<List<GitHubRef>>($"repos/{repository}/git/matching-refs/tags/{prefix}", cancellationToken, notFoundIsEmpty: true)
                .ConfigureAwait(false);

            const string tags_ref = "refs/tags/";
            var tags = (refs ?? new List<GitHubRef>()).Select(r => r.Ref)
                                                      .Where(r => r.StartsWith(tags_ref + prefix, StringComparison.Ordinal))
                                                      .Select(r => r[tags_ref.Length..])
                                                      .ToList();

            cachedTags[prefix] = (DateTime.UtcNow, tags);
            return tags;
        }

        /// <summary>
        /// The release for <paramref name="tag"/>, or null when it has none (or only a draft, which GitHub does not show).
        /// </summary>
        private Task<GitHubRelease?> getReleaseAsync(string tag, CancellationToken cancellationToken)
            => getJsonAsync<GitHubRelease>($"repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}", cancellationToken, notFoundIsEmpty: true);

        private async Task<T?> getJsonAsync<T>(string path, CancellationToken cancellationToken, bool notFoundIsEmpty = false)
            where T : class
        {
            var uri = new Uri(api, path);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (notFoundIsEmpty && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<T>(json);
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

            /// <summary>
            /// <c>sha256:&lt;hex&gt;</c>, which GitHub works out from the file uploaded.
            /// </summary>
            [JsonPropertyName("digest")]
            public string? Digest { get; set; }
        }

        internal sealed class GitHubRef
        {
            [JsonPropertyName("ref")]
            public string Ref { get; set; } = string.Empty;
        }
    }
}
