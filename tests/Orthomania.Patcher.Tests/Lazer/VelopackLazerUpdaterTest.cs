using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Orthomania.Patcher.Lazer;
using Velopack.Sources;

namespace Orthomania.Patcher.Tests.Lazer
{
    /// <summary>
    /// Drives Velopack's real update code against a local feed of real packages, with <c>Update.exe</c> played by the test.
    /// </summary>
    [TestFixture]
    public class VelopackLazerUpdaterTest
    {
        private TempFolder temp = null!;
        private LazerInstall install = null!;
        private string feed = null!;
        private readonly List<string> log = new List<string>();
        private readonly List<ProcessStartInfo> updaterRuns = new List<ProcessStartInfo>();

        [SetUp]
        public void SetUp()
        {
            temp = new TempFolder();
            install = Fakes.Lazer(temp.Combine("osulazer"));
            feed = temp.Combine("feed");
            Directory.CreateDirectory(feed);
            log.Clear();
            updaterRuns.Clear();
        }

        [TearDown]
        public void TearDown() => temp.Dispose();

        private VelopackLazerUpdater createUpdater(Func<ProcessStartInfo, Task<int>>? runUpdater = null)
            => new VelopackLazerUpdater(log.Add, _ => new SimpleFileSource(new DirectoryInfo(feed)), runUpdater ?? playUpdateExe);

        /// <summary>
        /// What Update.exe apply does: installs the package's release as the new current folder.
        /// </summary>
        private Task<int> playUpdateExe(ProcessStartInfo info)
        {
            updaterRuns.Add(info);

            var args = info.ArgumentList.ToList();
            string package = args[args.IndexOf("--package") + 1];

            using (var zip = ZipFile.OpenRead(package))
            {
                string nuspec = new StreamReader(zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open()).ReadToEnd();
                string version = nuspec.Split("<version>")[1].Split("</version>")[0];
                Fakes.UpdateLazer(install, version);
            }

            return Task.FromResult(0);
        }

        private void publish(string version, string channel = "win")
        {
            string path = Path.Combine(feed, $"osulazer-{version}-{channel}-full.nupkg");

            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

            using (var writer = new StreamWriter(zip.CreateEntry("osulazer.nuspec").Open()))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                             + "<package xmlns=\"http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd\"><metadata>"
                             + $"<id>osulazer</id><title>osu!</title><authors>ppy</authors><description>osu!</description><version>{version}</version>"
                             + $"<channel>{channel}</channel><mainExe>osu!.exe</mainExe></metadata></package>");
            }

            using (var writer = new StreamWriter(zip.CreateEntry("lib/app/osu!.exe").Open()))
                writer.Write($"game {version}");
        }

        [Test]
        public async Task TestNoUpdateWhenCurrent()
        {
            publish("2026.1026.0");
            publish("2026.905.0");

            Assert.That(await createUpdater().CheckAsync(install, CancellationToken.None), Is.Null);
        }

        [Test]
        public async Task TestUpdatesWithLazersOwnUpdater()
        {
            publish("2026.1026.0");
            publish("2026.1105.0");
            publish("2026.1200.0", channel: "osx");

            var updater = createUpdater();
            var update = await updater.CheckAsync(install, CancellationToken.None);

            Assert.That(update, Is.Not.Null);
            Assert.That(update!.From.Number, Is.EqualTo("2026.1026.0"));
            Assert.That(update.To.Number, Is.EqualTo("2026.1105.0"), "only the install's own channel");

            var progress = new List<double>();
            await updater.DownloadAsync(update, progress.Add, CancellationToken.None);

            Assert.That(File.Exists(Path.Combine(install.PackagesDir, "osulazer-2026.1105.0-win-full.nupkg")), Is.True, "in lazer's own packages folder");
            Assert.That(progress, Is.Not.Empty);
            Assert.That(progress.Last(), Is.EqualTo(1.0));

            await updater.ApplyAsync(update, CancellationToken.None);

            Assert.That(install.ReadRelease().Version.Number, Is.EqualTo("2026.1105.0"));

            var run = updaterRuns.Single();
            var args = run.ArgumentList.ToList();

            Assert.That(run.FileName, Is.EqualTo(install.UpdateExe));
            Assert.That(args, Does.Contain("apply"));
            Assert.That(args, Does.Contain("--norestart"), "the patcher starts the game itself");
            Assert.That(args, Has.None.EqualTo("--waitPid"));
            Assert.That(args[args.IndexOf("--rootDir") + 1], Is.EqualTo(install.Root));
            Assert.That(args[args.IndexOf("--packageDir") + 1], Is.EqualTo(install.PackagesDir));
            Assert.That(args[args.IndexOf("--package") + 1], Is.EqualTo(Path.Combine(install.PackagesDir, "osulazer-2026.1105.0-win-full.nupkg")));
        }

        [Test]
        public async Task TestNeverDowngrades()
        {
            publish("2026.905.0");

            Assert.That(await createUpdater().CheckAsync(install, CancellationToken.None), Is.Null);
        }

        [Test]
        public async Task TestDamagedDownloadIsRefused()
        {
            publish("2026.1105.0");

            // A feed index whose hash does not match the package.
            string package = Directory.GetFiles(feed, "*.nupkg").Single();
            File.WriteAllText(Path.Combine(feed, "releases.win.json"),
                "{\"Assets\":[{\"PackageId\":\"osulazer\",\"Version\":\"2026.1105.0\",\"Type\":\"Full\",\"FileName\":\"" + Path.GetFileName(package)
                + "\",\"SHA1\":\"0000000000000000000000000000000000000000\",\"SHA256\":\"" + new string('0', 64) + "\",\"Size\":" + new FileInfo(package).Length + "}]}");

            var updater = createUpdater();
            var update = await updater.CheckAsync(install, CancellationToken.None);

            Assert.That(update, Is.Not.Null);
            Assert.That(async () => await updater.DownloadAsync(update!, _ => { }, CancellationToken.None), Throws.TypeOf<LazerUpdateException>());
            Assert.That(Directory.GetFiles(install.PackagesDir, "*.nupkg"), Is.Empty);
        }

        [Test]
        public async Task TestFailedApplyIsReported()
        {
            publish("2026.1105.0");

            var updater = createUpdater(_ => Task.FromResult(1));
            var update = (await updater.CheckAsync(install, CancellationToken.None))!;
            await updater.DownloadAsync(update, _ => { }, CancellationToken.None);

            Assert.That(async () => await updater.ApplyAsync(update, CancellationToken.None), Throws.TypeOf<LazerUpdateException>().With.Message.Contains("exit code 1"));
            Assert.That(install.ReadRelease().Version.Number, Is.EqualTo("2026.1026.0"));
        }

        [Test]
        public async Task TestApplyThatChangesNothingIsReported()
        {
            publish("2026.1105.0");

            var updater = new VelopackLazerUpdater(log.Add, _ => new SimpleFileSource(new DirectoryInfo(feed)), _ => Task.FromResult(0))
            {
                ApplyTimeout = TimeSpan.FromSeconds(1),
            };

            var update = (await updater.CheckAsync(install, CancellationToken.None))!;
            await updater.DownloadAsync(update, _ => { }, CancellationToken.None);

            Assert.That(async () => await updater.ApplyAsync(update, CancellationToken.None), Throws.TypeOf<LazerUpdateException>().With.Message.Contains("not at 2026.1105.0"));
        }

        [Test]
        public void TestUnreachableFeedIsReported()
        {
            var updater = new VelopackLazerUpdater(log.Add, _ => new ThrowingSource());

            Assert.That(async () => await updater.CheckAsync(install, CancellationToken.None), Throws.TypeOf<LazerUpdateException>());
        }

        private sealed class ThrowingSource : IUpdateSource
        {
            public Task<Velopack.VelopackAssetFeed> GetReleaseFeed(Velopack.Logging.IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null,
                                                                   Velopack.VelopackAsset? latestLocalRelease = null)
                => throw new System.Net.Http.HttpRequestException("offline");

            public Task DownloadReleaseEntry(Velopack.Logging.IVelopackLogger logger, Velopack.VelopackAsset releaseEntry, string localFile, Action<int> progress,
                                             CancellationToken cancelToken)
                => throw new System.Net.Http.HttpRequestException("offline");
        }
    }
}
