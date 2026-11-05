using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher.Tests.Releases
{
    [TestFixture]
    public class GitHubReleaseFeedTest
    {
        private static readonly LazerVersion october = LazerVersion.Parse("2026.1026.0");

        /// <summary>
        /// GitHub's API and downloads, from files on disk.
        /// </summary>
        private sealed class FakeGitHub : HttpMessageHandler
        {
            public string ReleasesJson = "[]";
            public readonly Dictionary<string, byte[]> Downloads = new Dictionary<string, byte[]>();
            public int ReleaseListRequests;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string url = request.RequestUri!.ToString();

                if (url.StartsWith("https://api.github.com/repos/Reckswell/orthomania/releases", StringComparison.Ordinal))
                {
                    ReleaseListRequests++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleasesJson, Encoding.UTF8, "application/json") });
                }

                if (Downloads.TryGetValue(url, out byte[]? bytes))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private static string release(string tag, bool draft = false, bool prerelease = false, params string[] assets)
            => $"{{\"tag_name\":\"{tag}\",\"draft\":{draft.ToString().ToLowerInvariant()},\"prerelease\":{prerelease.ToString().ToLowerInvariant()},"
               + $"\"html_url\":\"https://github.com/Reckswell/orthomania/releases/tag/{tag}\",\"assets\":["
               + string.Join(",", assets.Select(a => $"{{\"name\":\"{a}\",\"size\":1,\"browser_download_url\":\"{url(tag, a)}\"}}")) + "]}";

        private static string url(string tag, string asset) => $"https://github.com/Reckswell/orthomania/releases/download/{tag}/{asset}";

        private static (FakeGitHub GitHub, GitHubReleaseFeed Feed) create()
        {
            var github = new FakeGitHub();
            return (github, new GitHubReleaseFeed(new HttpClient(github), "Reckswell/orthomania"));
        }

        /// <summary>
        /// Publishes a build made by <see cref="Fakes.Build"/> as the release <paramref name="tag"/>.
        /// </summary>
        private static void publish(FakeGitHub github, string folder, string tag)
        {
            foreach (string file in Directory.GetFiles(folder))
                github.Downloads[url(tag, Path.GetFileName(file))] = File.ReadAllBytes(file);
        }

        [TestCase("lazer-2026.1026.0-0.1.0", "2026.1026.0", "0.1.0")]
        [TestCase("lazer-2026.1026.0-0.1.0-beta.2", "2026.1026.0", "0.1.0-beta.2")]
        public void TestParsesBuildTags(string tag, string lazer, string build)
        {
            Assert.That(GitHubReleaseFeed.TryParseBuildTag(tag, out var l, out string b), Is.True);
            Assert.That(l.Number, Is.EqualTo(lazer));
            Assert.That(b, Is.EqualTo(build));
            Assert.That(GitHubReleaseFeed.BuildTag(l, b), Is.EqualTo(tag));
        }

        [TestCase("patcher-0.1.0")]
        [TestCase("lazer-2026.1026.0")]
        [TestCase("lazer-2026.1026.0-")]
        [TestCase("lazer-2026.1026-0.1.0")]
        [TestCase("lazer-2026.1026.0-../../evil")]
        [TestCase("v2026.1026.0-0.1.0")]
        public void TestRefusesOtherTags(string tag)
        {
            Assert.That(GitHubReleaseFeed.TryParseBuildTag(tag, out _, out _), Is.False);
        }

        [Test]
        public async Task TestFindsTheNewestBuildForExactlyTheRelease()
        {
            var (github, feed) = create();

            github.ReleasesJson = "[" + string.Join(",",
                release("lazer-2026.1105.0-0.3.0", assets: new[] { "orthomania.json", "osu.Game.Rulesets.ManiaOrtho.dll" }),
                release("lazer-2026.1026.0-0.2.0", draft: true, assets: new[] { "orthomania.json" }),
                release("lazer-2026.1026.0-0.1.10", assets: new[] { "orthomania.json", "osu.Game.Rulesets.ManiaOrtho.dll" }),
                release("lazer-2026.1026.0-0.1.9", assets: new[] { "orthomania.json", "osu.Game.Rulesets.ManiaOrtho.dll" }),
                release("lazer-2026.1026.0-0.1.11", assets: new[] { "osu.Game.Rulesets.ManiaOrtho.dll" }),
                release("patcher-0.2.0", assets: new[] { "orthomania-win-x64.zip" })) + "]";

            var found = await feed.FindBuildAsync(october, CancellationToken.None);

            Assert.That(found, Is.Not.Null);
            Assert.That(found!.Build, Is.EqualTo("0.1.10"), "not the draft, not the one without a manifest, 0.1.10 after 0.1.9");
            Assert.That(found.Files.Keys, Is.EquivalentTo(new[] { "orthomania.json", "osu.Game.Rulesets.ManiaOrtho.dll" }));

            Assert.That(await feed.FindBuildAsync(LazerVersion.Parse("2026.1200.0"), CancellationToken.None), Is.Null);

            var patcher = await feed.FindPatcherAsync(CancellationToken.None);
            Assert.That(patcher!.Version, Is.EqualTo(new Version(0, 2, 0)));

            Assert.That(github.ReleaseListRequests, Is.EqualTo(1), "asked once");
        }

        [Test]
        public async Task TestDownloadsABuildThatTheStoreAccepts()
        {
            using var temp = new TempFolder();
            var (github, feed) = create();
            string tag = "lazer-2026.1026.0-0.1.0";

            publish(github, Fakes.Build(temp.Combine("published")), tag);
            github.ReleasesJson = "[" + release(tag, assets: new[] { "orthomania.json", "osu.Game.Rulesets.ManiaOrtho.dll", "0Harmony.dll" }) + "]";

            var found = (await feed.FindBuildAsync(october, CancellationToken.None))!;
            var progress = new List<double>();

            await feed.DownloadAsync(found, temp.Combine("staging"), progress.Add, CancellationToken.None);

            var added = new BuildStore(temp.Combine("builds")).Add(temp.Combine("staging"));

            Assert.That(added.Build, Is.EqualTo("0.1.0"));
            Assert.That(progress.Last(), Is.EqualTo(1.0));
        }

        [Test]
        public async Task TestRefusesAManifestForAnotherPair()
        {
            using var temp = new TempFolder();
            var (github, feed) = create();
            string tag = "lazer-2026.1026.0-0.1.0";

            // The tag says 2026.1026.0, the manifest 2026.905.0.
            publish(github, Fakes.Build(temp.Combine("published"), lazer: "2026.905.0"), tag);
            github.ReleasesJson = "[" + release(tag, assets: new[] { "orthomania.json", "osu.Game.Rulesets.ManiaOrtho.dll", "0Harmony.dll" }) + "]";

            var found = (await feed.FindBuildAsync(october, CancellationToken.None))!;

            Assert.That(async () => await feed.DownloadAsync(found, temp.Combine("staging"), _ => { }, CancellationToken.None), Throws.TypeOf<ReleaseFeedException>());
        }

        [Test]
        public async Task TestRefusesAFileLargerThanListed()
        {
            using var temp = new TempFolder();
            var (github, feed) = create();
            string tag = "lazer-2026.1026.0-0.1.0";

            publish(github, Fakes.Build(temp.Combine("published")), tag);
            github.Downloads[url(tag, "0Harmony.dll")] = new byte[1024];
            github.ReleasesJson = "[" + release(tag, assets: new[] { "orthomania.json", "osu.Game.Rulesets.ManiaOrtho.dll", "0Harmony.dll" }) + "]";

            var found = (await feed.FindBuildAsync(october, CancellationToken.None))!;

            Assert.That(async () => await feed.DownloadAsync(found, temp.Combine("staging"), _ => { }, CancellationToken.None), Throws.TypeOf<ReleaseFeedException>());
        }

        [Test]
        public async Task TestMissingFileIsReported()
        {
            using var temp = new TempFolder();
            var (github, feed) = create();
            string tag = "lazer-2026.1026.0-0.1.0";

            publish(github, Fakes.Build(temp.Combine("published")), tag);
            github.ReleasesJson = "[" + release(tag, assets: new[] { "orthomania.json", "osu.Game.Rulesets.ManiaOrtho.dll" }) + "]";

            var found = (await feed.FindBuildAsync(october, CancellationToken.None))!;

            Assert.That(async () => await feed.DownloadAsync(found, temp.Combine("staging"), _ => { }, CancellationToken.None),
                Throws.TypeOf<ReleaseFeedException>().With.Message.Contains("0Harmony.dll"));
        }

        [Test]
        public void TestUnreachableIsReported()
        {
            var feed = new GitHubReleaseFeed(new HttpClient(new FakeGitHub()), "Someone/else");

            Assert.That(async () => await feed.FindBuildAsync(october, CancellationToken.None), Throws.TypeOf<ReleaseFeedException>());
        }
    }
}
