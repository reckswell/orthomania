using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Orthomania.Patcher.Flow;
using Orthomania.Patcher.Folders;
using Orthomania.Patcher.Launch;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Releases;
using Orthomania.Patcher.Settings;

namespace Orthomania.Patcher.Tests.Flow
{
    /// <summary>
    /// Whole launches of the patcher, with lazer's install, its update feed, orthomania's releases and the game played by the test.
    /// </summary>
    [TestFixture]
    public class PatcherFlowTest
    {
        private TempFolder temp = null!;
        private LazerInstall install = null!;
        private PatcherLayout layout = null!;
        private FakeLazerUpdater updater = null!;
        private FakeFeed feed = null!;
        private FakeRunner runner = null!;
        private FakeDevServer devServer = null!;
        private List<string> events = null!;
        private PatcherLog log = null!;
        private string dataFolder = null!;

        private static readonly LazerVersion october = LazerVersion.Parse("2026.1026.0");
        private static readonly LazerVersion november = LazerVersion.Parse("2026.1105.0");

        [SetUp]
        public void SetUp()
        {
            temp = new TempFolder();
            install = Fakes.Lazer(temp.Combine("osulazer"), october.Number);
            layout = new PatcherLayout(temp.Combine("orthomania"));
            dataFolder = temp.Combine("ortho-data");

            writeSettings(new PatcherSettings
            {
                LazerFolder = install.Root,
                DataFolder = dataFolder,
            });

            updater = new FakeLazerUpdater();
            feed = new FakeFeed(temp.Combine("published"));
            events = new List<string>();
            runner = new FakeRunner { OnRun = _ => events.Add("game") };
            devServer = new FakeDevServer(events);
            log = new PatcherLog(temp.Combine("logs"));
        }

        [TearDown]
        public void TearDown() => temp.Dispose();

        private void writeSettings(PatcherSettings settings) => JsonFile.Write(layout.SettingsFile, settings);

        private PatcherFlow createFlow() => new PatcherFlow(layout, updater, feed, runner, log, new Version(0, 1, 0), root => LazerInstall.Find(root),
            new[] { temp.Combine("appdata", "osu") }, devServer);

        private async Task<PatcherFlow> checkAsync()
        {
            var flow = createFlow();
            await flow.CheckAsync(CancellationToken.None);
            return flow;
        }

        [Test]
        public async Task TestNotInstalled()
        {
            Directory.Delete(install.Root, recursive: true);

            var flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.NotInstalled));
            Assert.That(flow.Status.Detail, Is.EqualTo("osu!(lazer) is not installed."));
            Assert.That(flow.Target, Is.Null);
        }

        [Test]
        public async Task TestFirstLaunch()
        {
            feed.Publish(october, "0.1.0");

            var flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready), flow.Status.Detail);
            Assert.That(flow.Status.Detail, Is.EqualTo("osu! 2026.1026.0 with orthomania 0.1.0"));
            Assert.That(flow.Target!.Directory, Is.EqualTo(Path.Combine(layout.LazerFolders, "2026.1026.0")));
            Assert.That(File.ReadAllText(Path.Combine(flow.Target.Directory, "osu.Game.Rulesets.ManiaOrtho.dll")), Is.EqualTo("ruleset 0.1.0 for 2026.1026.0"));

            int exitCode = await flow.LaunchAsync(CancellationToken.None);

            Assert.That(exitCode, Is.Zero);
            Assert.That(runner.Ran.Single().Directory, Is.EqualTo(flow.Target.Directory));
            Assert.That(flow.State.LastWorking, Is.EqualTo(new PairRecord("2026.1026.0", "0.1.0")));
            Assert.That(Directory.GetFileSystemEntries(dataFolder), Is.EqualTo(new[] { Path.Combine(dataFolder, Orthomania.Patcher.Data.DataFolder.NOTE) }),
                "orthomania's data folder starts with only the note");

            Assert.That(File.Exists(Path.Combine(flow.Target.Directory, VersionFolders.FRAMEWORK_INI)), Is.False, "osu! keeps its data where plain osu! does");
            var game = GameSettings.Parse(File.ReadAllText(Path.Combine(flow.Target.Directory, GameSettings.FILE_NAME)))!;
            Assert.That(game.DataFolder, Is.EqualTo(dataFolder));
            Assert.That(game.OsuSignIn, Is.False, "signed out of osu! unless turned on");

            var saved = JsonFile.ReadOrDefault<PatcherState>(layout.StateFile, out _);
            Assert.That(saved.LastWorking, Is.EqualTo(new PairRecord("2026.1026.0", "0.1.0")));
        }

        [Test]
        public async Task TestLeavesLazerInstallAlone()
        {
            feed.Publish(october, "0.1.0");
            var before = snapshot(install.Root);

            var flow = await checkAsync();
            await flow.LaunchAsync(CancellationToken.None);
            await flow.RepairAsync(CancellationToken.None);
            flow.RemoveOrthomania();

            Assert.That(snapshot(install.Root), Is.EqualTo(before));
            Assert.That(Directory.Exists(temp.Combine("appdata")), Is.False, "nothing made where osu! keeps its data");
        }

        [Test]
        public async Task TestNeverOpensOsuData()
        {
            string osu = temp.Combine("appdata", "osu");
            Directory.CreateDirectory(osu);
            File.WriteAllText(Path.Combine(osu, "game.ini"), "Token = secret-osu-token\r\n");
            File.WriteAllText(Path.Combine(osu, "client.realm"), "scores");
            File.WriteAllText(Path.Combine(osu, "storage.ini"), $"FullPath = {temp.Combine("moved")}\r\n");

            feed.Publish(october, "0.1.0");
            var before = snapshot(osu);

            // Held shut: any read of osu!'s files by the patcher would throw.
            var held = Directory.GetFiles(osu).Select(f => File.Open(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None)).ToList();

            try
            {
                var flow = await checkAsync();
                Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready), flow.Status.Detail);

                await flow.LaunchAsync(CancellationToken.None);
                await flow.RepairAsync(CancellationToken.None);
            }
            finally
            {
                held.ForEach(h => h.Dispose());
            }

            Assert.That(snapshot(osu), Is.EqualTo(before));
            Assert.That(Directory.Exists(temp.Combine("moved")), Is.False);

            foreach (string written in Directory.GetFiles(layout.Root, "*", SearchOption.AllDirectories).Concat(Directory.GetFiles(dataFolder)))
                Assert.That(File.ReadAllText(written), Does.Not.Contain("secret-osu-token"), written);
        }

        [Test]
        public async Task TestOsuSignInIsOnlyAllowedWhenTurnedOn()
        {
            feed.Publish(october, "0.1.0");

            var flow = await checkAsync();
            string file = Path.Combine(flow.Target!.Directory, GameSettings.FILE_NAME);
            Assert.That(GameSettings.Parse(File.ReadAllText(file))!.OsuSignIn, Is.False);

            writeSettings(new PatcherSettings { LazerFolder = install.Root, DataFolder = dataFolder, OsuSignIn = true });
            flow = await checkAsync();

            Assert.That(flow.Target!.Record.OsuSignIn, Is.True);
            Assert.That(GameSettings.Parse(File.ReadAllText(file))!.OsuSignIn, Is.True);
            Assert.That(VersionFolders.Verify(flow.Target, out string? problem), Is.True, problem);

            writeSettings(new PatcherSettings { LazerFolder = install.Root, DataFolder = dataFolder });
            flow = await checkAsync();

            Assert.That(GameSettings.Parse(File.ReadAllText(file))!.OsuSignIn, Is.False);
        }

        [Test]
        public async Task TestUpdatesLazerThenGetsTheBuildForIt()
        {
            feed.Publish(october, "0.1.0");
            feed.Publish(november, "0.2.0");
            updater.Available = november;

            var stages = new List<PatcherStage>();
            var flow = createFlow();
            flow.StatusChanged += s => stages.Add(s.Stage);

            await flow.CheckAsync(CancellationToken.None);

            Assert.That(install.ReadRelease().Version, Is.EqualTo(november));
            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready));
            Assert.That(flow.Target!.Lazer, Is.EqualTo(november));
            Assert.That(flow.Target.Record.Build, Is.EqualTo("0.2.0"));
            Assert.That(stages.Distinct(), Is.EqualTo(new[]
            {
                PatcherStage.Starting, PatcherStage.UpdatingLazer, PatcherStage.GettingOrthomania, PatcherStage.Preparing, PatcherStage.Ready,
            }));
        }

        [Test]
        public async Task TestWaitsForTheBuildForTheNewLazer()
        {
            feed.Publish(october, "0.1.0");

            var flow = await checkAsync();
            await flow.LaunchAsync(CancellationToken.None);

            updater.Available = november;
            flow = await checkAsync();

            Assert.That(install.ReadRelease().Version, Is.EqualTo(november), "plain lazer is updated anyway");
            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Problem), "october may not open osu!'s data after november has");
            Assert.That(flow.Status.Detail, Is.EqualTo("orthomania for osu! 2026.1105.0 is not out yet. Plain osu! works as usual; try again later."));
            Assert.That(flow.Target, Is.Null);

            // The build for november comes out.
            feed.Publish(november, "0.2.0");
            flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready));
            Assert.That(flow.Target!.Lazer, Is.EqualTo(november));
        }

        [Test]
        public async Task TestOfflineUsesTheBuildItHas()
        {
            feed.Publish(october, "0.1.0");
            await checkAsync();

            feed.Offline = true;
            var flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready));
            Assert.That(flow.Notices.Single(), Does.Contain("Using 0.1.0"));
        }

        [Test]
        public async Task TestOfflineWithNoBuildIsAProblem()
        {
            feed.Offline = true;

            var flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Problem));
            Assert.That(flow.Status.Detail, Does.Contain("not out yet"));
        }

        [Test]
        public async Task TestFailedLazerUpdateCarriesOn()
        {
            feed.Publish(october, "0.1.0");
            updater.Available = november;
            updater.FailApply = true;

            var flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready));
            Assert.That(flow.Target!.Lazer, Is.EqualTo(october));
            Assert.That(flow.Notices.Single(), Does.Contain("could not be updated to 2026.1105.0"));
        }

        [Test]
        public async Task TestLazerUpdatesCanBeTurnedOff()
        {
            feed.Publish(october, "0.1.0");
            updater.Available = november;

            writeSettings(new PatcherSettings { LazerFolder = install.Root, DataFolder = dataFolder, UpdateLazer = false });

            var flow = await checkAsync();

            Assert.That(install.ReadRelease().Version, Is.EqualTo(october));
            Assert.That(updater.Checks, Is.Zero);
            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready));
        }

        [Test]
        public async Task TestWaitsForRunningOsuToClose()
        {
            feed.Publish(october, "0.1.0");
            runner.RunningChecks = 2;

            var stages = new List<PatcherStage>();
            var flow = createFlow();
            flow.StatusChanged += s => stages.Add(s.Stage);

            await flow.CheckAsync(CancellationToken.None);

            Assert.That(stages, Does.Contain(PatcherStage.WaitingForGame));
            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready));
        }

        [Test]
        public async Task TestRefusesToShareDataWithPlainLazer()
        {
            writeSettings(new PatcherSettings { LazerFolder = install.Root, DataFolder = temp.Combine("appdata", "osu") });
            feed.Publish(october, "0.1.0");

            var flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Problem));
            Assert.That(flow.Status.Detail, Does.Contain("is osu!'s own data"));
            Assert.That(Directory.Exists(layout.LazerFolders), Is.False, "nothing made");
        }

        [Test]
        public async Task TestRefusesADataFolderHoldingACopyOfOsuData()
        {
            // An earlier orthomania kept a whole copy of osu!'s data in its folder.
            feed.Publish(october, "0.1.0");
            Directory.CreateDirectory(dataFolder);
            File.WriteAllText(Path.Combine(dataFolder, "client_52.realm"), "the debug build's");

            var flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Problem));
            Assert.That(flow.Status.Detail, Does.Contain("holds osu!'s own data (client_52.realm)"));
            Assert.That(Directory.Exists(layout.LazerFolders), Is.False, "nothing made");
            Assert.That(File.ReadAllText(Path.Combine(dataFolder, "client_52.realm")), Is.EqualTo("the debug build's"), "left as it was");
        }

        [Test]
        public async Task TestNewerBuildForTheSameLazerRefreshesTheFolder()
        {
            feed.Publish(october, "0.1.0");
            var first = (await checkAsync()).Target!;

            feed.Publish(october, "0.1.1");
            var flow = await checkAsync();

            Assert.That(flow.Target!.Directory, Is.EqualTo(first.Directory));
            Assert.That(flow.Target.Record.Build, Is.EqualTo("0.1.1"));
            Assert.That(File.ReadAllText(Path.Combine(flow.Target.Directory, "osu.Game.Rulesets.ManiaOrtho.dll")), Is.EqualTo("ruleset 0.1.1 for 2026.1026.0"));
            Assert.That(VersionFolders.Verify(flow.Target, out string? problem), Is.True, problem);
        }

        [Test]
        public async Task TestDamagedFolderIsRebuilt()
        {
            feed.Publish(october, "0.1.0");
            var first = (await checkAsync()).Target!;

            File.Delete(Path.Combine(first.Directory, "osu.Framework.dll"));
            var flow = await checkAsync();

            Assert.That(flow.Status.Stage, Is.EqualTo(PatcherStage.Ready));
            Assert.That(VersionFolders.Verify(flow.Target!, out string? problem), Is.True, problem);
        }

        [Test]
        public async Task TestAQuickCrashIsNotAWorkingPair()
        {
            feed.Publish(october, "0.1.0");
            runner.ExitCode = 1;

            var flow = await checkAsync();
            await flow.LaunchAsync(CancellationToken.None);

            Assert.That(flow.State.LastStarted, Is.EqualTo(new PairRecord("2026.1026.0", "0.1.0")));
            Assert.That(flow.State.LastWorking, Is.Null);
        }

        [Test]
        public async Task TestKeepsOnlyTheInstalledRelease()
        {
            var releases = new[] { "2026.905.0", "2026.1026.0", "2026.1105.0", "2026.1200.0" }.Select(LazerVersion.Parse).ToArray();

            Directory.Delete(install.Root, recursive: true);
            install = Fakes.Lazer(install.Root, releases[0].Number);

            foreach (var release in releases)
                feed.Publish(release, "0.1.0");

            foreach (var release in releases)
            {
                updater.Available = release;
                var flow = await checkAsync();
                await flow.LaunchAsync(CancellationToken.None);
            }

            await checkAsync();

            Assert.That(new VersionFolders(layout.LazerFolders).All().Select(f => f.Record.Lazer), Is.EqualTo(new[] { "2026.1200.0" }));
            Assert.That(Directory.GetDirectories(layout.LazerFolders), Has.Length.EqualTo(1));
            Assert.That(Directory.GetDirectories(layout.Builds).Select(Path.GetFileName), Is.EquivalentTo(new[] { "2026.1200.0" }));
        }

        [Test]
        public async Task TestMentionsANewerPatcher()
        {
            feed.Publish(october, "0.1.0");
            feed.Patcher = new PublishedPatcher(new Version(0, 2, 0), "patcher-0.2.0", new Uri("https://github.com/Reckswell/orthomania/releases/tag/patcher-0.2.0"));

            var flow = await checkAsync();

            Assert.That(flow.NewerPatcher!.Version, Is.EqualTo(new Version(0, 2, 0)));
            Assert.That(flow.Notices.Single(), Does.Contain("0.2.0"));
        }

        [Test]
        public async Task TestRemoveKeepsTheData()
        {
            feed.Publish(october, "0.1.0");
            var flow = await checkAsync();
            await flow.LaunchAsync(CancellationToken.None);
            Directory.CreateDirectory(Path.Combine(dataFolder, "maniaortho"));
            File.WriteAllText(Path.Combine(dataFolder, "maniaortho", "plays.csv"), "plays");

            flow.RemoveOrthomania();

            Assert.That(Directory.Exists(layout.LazerFolders), Is.False);
            Assert.That(Directory.Exists(layout.Builds), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(dataFolder, "maniaortho", "plays.csv")), Is.EqualTo("plays"));
            Assert.That(File.Exists(install.GameExecutable), Is.True);
        }

        [Test]
        public async Task TestDevServerIsOffUnlessTurnedOn()
        {
            feed.Publish(october, "0.1.0");

            var flow = await checkAsync();
            await flow.LaunchAsync(CancellationToken.None);

            Assert.That(flow.DevServerEnabled, Is.False);
            Assert.That(events, Is.EqualTo(new[] { "game" }));
        }

        [Test]
        public async Task TestDevServerRunsAroundTheGame()
        {
            feed.Publish(october, "0.1.0");

            var flow = await checkAsync();
            flow.SetDevServerEnabled(true);
            int exitCode = await flow.LaunchAsync(CancellationToken.None);

            Assert.That(exitCode, Is.Zero);
            Assert.That(events, Is.EqualTo(new[] { "server started", "game", "server stopped" }));
            Assert.That(flow.Notices, Is.Empty);

            var saved = JsonFile.ReadOrDefault<PatcherSettings>(layout.SettingsFile, out _);
            Assert.That(saved.DevServer!.Enabled, Is.True);
            Assert.That(saved.LazerFolder, Is.EqualTo(install.Root));

            flow = await checkAsync();
            Assert.That(flow.DevServerEnabled, Is.True);
            flow.SetDevServerEnabled(false);
            await flow.LaunchAsync(CancellationToken.None);

            Assert.That(events.Skip(3), Is.EqualTo(new[] { "game" }));
        }

        [Test]
        public async Task TestDevServerAlreadyRunningIsLeftRunning()
        {
            feed.Publish(october, "0.1.0");
            devServer.AlreadyRunning = true;

            var flow = await checkAsync();
            flow.SetDevServerEnabled(true);
            await flow.LaunchAsync(CancellationToken.None);

            Assert.That(events, Is.EqualTo(new[] { "server found", "game" }));
        }

        [Test]
        public async Task TestGameStartsWhenTheServerDoesNot()
        {
            feed.Publish(october, "0.1.0");
            devServer.Fail = true;

            var flow = await checkAsync();
            flow.SetDevServerEnabled(true);
            int exitCode = await flow.LaunchAsync(CancellationToken.None);

            Assert.That(exitCode, Is.Zero);
            Assert.That(events, Is.EqualTo(new[] { "game" }));
            Assert.That(flow.Notices.Single(), Does.StartWith("The score server did not start"));
            Assert.That(flow.State.LastWorking, Is.EqualTo(new PairRecord("2026.1026.0", "0.1.0")));
        }

        private static Dictionary<string, string> snapshot(string root)
            => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllText);

        private sealed class FakeLazerUpdater : ILazerUpdater
        {
            public LazerVersion? Available;
            public bool FailApply;
            public int Checks;

            public Task<LazerUpdate?> CheckAsync(LazerInstall install, CancellationToken cancellationToken)
            {
                Checks++;
                var installed = install.ReadRelease().Version;
                return Task.FromResult(Available != null && Available > installed ? new LazerUpdate(install, installed, Available) : null);
            }

            public Task DownloadAsync(LazerUpdate update, Action<double> progress, CancellationToken cancellationToken)
            {
                progress(0.5);
                progress(1);
                return Task.CompletedTask;
            }

            public Task ApplyAsync(LazerUpdate update, CancellationToken cancellationToken)
            {
                if (FailApply)
                    throw new LazerUpdateException("Update.exe failed");

                Fakes.UpdateLazer(update.Install, update.To.Number);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeFeed : IReleaseFeed
        {
            private readonly string root;
            private readonly Dictionary<LazerVersion, string> published = new Dictionary<LazerVersion, string>();

            public bool Offline;
            public PublishedPatcher? Patcher;

            public FakeFeed(string root)
            {
                this.root = root;
            }

            public void Publish(LazerVersion lazer, string build)
            {
                string folder = Path.Combine(root, lazer.Number, build);
                Fakes.Build(folder, lazer.Number, build);
                published[lazer] = build;
            }

            public void Withdraw(LazerVersion lazer) => published.Remove(lazer);

            public Task<PublishedBuild?> FindBuildAsync(LazerVersion lazer, CancellationToken cancellationToken)
            {
                if (Offline)
                    throw new ReleaseFeedException("offline");

                if (!published.TryGetValue(lazer, out string? build))
                    return Task.FromResult<PublishedBuild?>(null);

                string folder = Path.Combine(root, lazer.Number, build);
                var files = Directory.GetFiles(folder).ToDictionary(f => Path.GetFileName(f), f => new Uri(f));

                return Task.FromResult<PublishedBuild?>(new PublishedBuild(lazer, build, GitHubReleaseFeed.BuildTag(lazer, build), files));
            }

            public Task DownloadAsync(PublishedBuild build, string staging, Action<double> progress, CancellationToken cancellationToken)
            {
                Directory.CreateDirectory(staging);

                foreach (var (name, uri) in build.Files)
                    File.Copy(uri.LocalPath, Path.Combine(staging, name));

                progress(1);
                return Task.CompletedTask;
            }

            public Task<PublishedPatcher?> FindPatcherAsync(CancellationToken cancellationToken)
                => Offline ? throw new ReleaseFeedException("offline") : Task.FromResult(Patcher);
        }

        private sealed class FakeDevServer : IDevServerRunner
        {
            private readonly List<string> events;

            public bool Fail;
            public bool AlreadyRunning;

            public FakeDevServer(List<string> events)
            {
                this.events = events;
            }

            public Task<IStartedServer?> EnsureRunningAsync(DevServerSettings settings, CancellationToken cancellationToken)
            {
                if (Fail)
                    throw new DevServerException("it stopped before it answered (exit code 3).");

                if (AlreadyRunning)
                {
                    events.Add("server found");
                    return Task.FromResult<IStartedServer?>(null);
                }

                events.Add("server started");
                return Task.FromResult<IStartedServer?>(new Started(events));
            }

            private sealed class Started : IStartedServer
            {
                private readonly List<string> events;

                public Started(List<string> events)
                {
                    this.events = events;
                }

                public int ProcessId => 1;

                public ValueTask DisposeAsync()
                {
                    events.Add("server stopped");
                    return ValueTask.CompletedTask;
                }
            }
        }

        private sealed class FakeRunner : IGameRunner
        {
            public readonly List<VersionFolder> Ran = new List<VersionFolder>();
            public int ExitCode;
            public int RunningChecks;
            public Action<VersionFolder>? OnRun;

            public bool IsLazerRunning()
            {
                if (RunningChecks <= 0)
                    return false;

                RunningChecks--;
                return true;
            }

            public Task<int> RunAsync(VersionFolder folder, Action started, CancellationToken cancellationToken)
            {
                Ran.Add(folder);
                started();
                OnRun?.Invoke(folder);
                return Task.FromResult(ExitCode);
            }
        }
    }
}
