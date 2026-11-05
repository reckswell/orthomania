using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orthomania.Patcher.Folders;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher.Tests.Folders
{
    [TestFixture]
    public class VersionFoldersTest
    {
        private TempFolder temp = null!;
        private LazerInstall install = null!;
        private BuildStore builds = null!;
        private VersionFolders folders = null!;
        private GameSettings game = null!;

        [SetUp]
        public void SetUp()
        {
            temp = new TempFolder();
            install = Fakes.Lazer(temp.Combine("osulazer"));
            builds = new BuildStore(temp.Combine("orthomania", "builds"));
            folders = new VersionFolders(temp.Combine("orthomania", "lazer"));
            game = new GameSettings(temp.Combine("ortho-data"), osuSignIn: false);
        }

        [TearDown]
        public void TearDown() => temp.Dispose();

        private RulesetBuild addBuild(string lazer = "2026.1026.0", string build = "0.1.0", Dictionary<string, string>? files = null)
            => builds.Add(Fakes.Build(temp.Combine("staging-" + build + "-" + lazer), lazer, build, files));

        private static string read(string path) => File.ReadAllText(path);

        [Test]
        public void TestCreatesAFolderOfLinksAndOrthomania()
        {
            var folder = folders.Create(install, addBuild(), game);

            Assert.That(folder.Directory, Is.EqualTo(Path.Combine(folders.Root, "2026.1026.0")));
            Assert.That(folder.Lazer, Is.EqualTo(LazerVersion.Parse("2026.1026.0")));
            Assert.That(folder.Record.Linked, Is.True);

            foreach (string file in Directory.GetFiles(install.Current, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(install.Current, file);
                Assert.That(read(Path.Combine(folder.Directory, relative)), Is.EqualTo(read(file)), relative);
            }

            Assert.That(File.Exists(Path.Combine(folder.Directory, VersionFolders.FRAMEWORK_INI)), Is.False, "osu! keeps its data where plain osu! does");
            Assert.That(File.Exists(Path.Combine(folder.Directory, VersionFolders.STORAGE_INI)), Is.False, "osu! keeps its data where plain osu! does");

            var settings = GameSettings.Parse(read(Path.Combine(folder.Directory, GameSettings.FILE_NAME)))!;
            Assert.That(settings.Format, Is.EqualTo(GameSettings.CURRENT_FORMAT));
            Assert.That(settings.DataFolder, Is.EqualTo(temp.Combine("ortho-data")));
            Assert.That(settings.OsuSignIn, Is.False);
            Assert.That(folder.Record.OsuSignIn, Is.False);

            Assert.That(read(Path.Combine(folder.Directory, "osu.Game.Rulesets.ManiaOrtho.dll")), Is.EqualTo("ruleset 0.1.0 for 2026.1026.0"));
            Assert.That(read(Path.Combine(folder.Directory, "0Harmony.dll")), Is.EqualTo("harmony 2.4.3"));

            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);
            Assert.That(folders.Get(LazerVersion.Parse("2026.1026.0")), Is.Not.Null);
            Assert.That(Directory.GetDirectories(folders.Root), Has.Length.EqualTo(1), "no leftovers");
        }

        [Test]
        public void TestLazerFilesAreLinksNotCopies()
        {
            var folder = folders.Create(install, addBuild(), game);

            // Only a test ever writes into lazer's files: a link sees the change, a copy would not.
            File.AppendAllText(Path.Combine(install.Current, "osu.Framework.dll"), " changed");

            Assert.That(read(Path.Combine(folder.Directory, "osu.Framework.dll")), Does.EndWith(" changed"));
        }

        [Test]
        public void TestLeavesLazerInstallAsItWas()
        {
            var before = snapshot(install.Root);

            var folder = folders.Create(install, addBuild(), game);
            folders.Refresh(folder, addBuild(build: "0.1.1"), game);
            folders.Prune(new List<VersionFolder>());

            Assert.That(snapshot(install.Root), Is.EqualTo(before));
        }

        [Test]
        public void TestNeverWritesThroughALinkToLazer()
        {
            // Were lazer ever to ship a file of this name, orthomania's must not change lazer's copy.
            File.WriteAllText(Path.Combine(install.Current, GameSettings.FILE_NAME), "lazer's own");

            var folder = folders.Create(install, addBuild(), game);

            Assert.That(read(Path.Combine(install.Current, GameSettings.FILE_NAME)), Is.EqualTo("lazer's own"));
            Assert.That(GameSettings.Parse(read(Path.Combine(folder.Directory, GameSettings.FILE_NAME)))!.DataFolder, Is.EqualTo(game.DataFolder));
            Assert.That(folder.Record.LazerFiles.Select(f => f.Path), Has.None.EqualTo(GameSettings.FILE_NAME));
            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);

            folders.Refresh(folder, addBuild(build: "0.1.1"), new GameSettings(temp.Combine("other-data"), osuSignIn: true));

            Assert.That(read(Path.Combine(install.Current, GameSettings.FILE_NAME)), Is.EqualTo("lazer's own"));
        }

        [TestCase(VersionFolders.FRAMEWORK_INI)]
        [TestCase(VersionFolders.STORAGE_INI)]
        public void TestVerifyRefusesAFolderThatWouldKeepItsOwnOsuData(string name)
        {
            var folder = folders.Create(install, addBuild(), game);

            File.WriteAllText(Path.Combine(folder.Directory, name), "FullPath = C:\\elsewhere\r\n");

            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.False);
            Assert.That(problem, Does.Contain(name));
        }

        [Test]
        public void TestAnEarlierPatchersFolderIsNotOpened()
        {
            var folder = folders.Create(install, addBuild(), game);
            string marker = Path.Combine(folder.Directory, VersionFolder.MARKER);

            File.WriteAllText(marker, read(marker).Replace($"\"format\": {FolderRecord.CURRENT_FORMAT}", "\"format\": 1"));

            Assert.That(folders.Get(folder.Lazer), Is.Null, "built again with this patcher's files");
        }

        [Test]
        public void TestABuildCannotReplaceLazerFiles()
        {
            var build = addBuild(files: new Dictionary<string, string>
            {
                ["osu.Game.Rulesets.ManiaOrtho.dll"] = "ruleset",
                ["osu.Framework.dll"] = "not lazer's",
            });

            Assert.That(() => folders.Create(install, build, game), Throws.TypeOf<InvalidDataException>());

            Assert.That(read(Path.Combine(install.Current, "osu.Framework.dll")), Is.EqualTo("framework 2026.1026.0"));
            Assert.That(Directory.GetDirectories(folders.Root), Is.Empty, "the unfinished folder is gone");
        }

        [Test]
        public void TestRefusesABuildForAnotherRelease()
        {
            Assert.That(() => folders.Create(install, addBuild(lazer: "2026.905.0"), game), Throws.InvalidOperationException);
            Assert.That(Directory.Exists(folders.Root) ? Directory.GetDirectories(folders.Root) : new string[0], Is.Empty);
        }

        [Test]
        public void TestFolderOutlivesALazerUpdate()
        {
            var folder = folders.Create(install, addBuild(), game);

            Fakes.UpdateLazer(install, "2026.1105.0");

            Assert.That(install.ReadRelease().Version, Is.EqualTo(LazerVersion.Parse("2026.1105.0")));
            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);
            Assert.That(read(folder.GameExecutable), Is.EqualTo("game 2026.1026.0"), "the old release is still whole until it is pruned");
        }

        [Test]
        public void TestVerifyNoticesDamage()
        {
            var folder = folders.Create(install, addBuild(), game);

            File.Delete(Path.Combine(folder.Directory, "runtimes", "win-x64", "native", "SDL3.dll"));
            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.False);
            Assert.That(problem, Does.Contain("SDL3.dll"));

            folder = folders.Create(install, addBuild(), game);
            File.WriteAllText(Path.Combine(folder.Directory, "0Harmony.dll"), "harmony 2.4.4");
            Assert.That(VersionFolders.Verify(folder, out problem), Is.False);
            Assert.That(problem, Does.Contain("0Harmony.dll"));
        }

        [Test]
        public void TestVerifyNoticesLazerFilesChangingRelease()
        {
            var folder = folders.Create(install, addBuild(), game);

            // An updater writing a new release into the old files (which lazer's does not) would change the linked files too.
            // File.WriteAllText writes into the existing file.
            Fakes.WriteVersionManifest(install.Current, "2026.1105.0");

            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.False);
            Assert.That(problem, Does.Contain("no longer release 2026.1026.0"));
        }

        [Test]
        public void TestIgnoresFilesLazerAddsWhileRunning()
        {
            var folder = folders.Create(install, addBuild(), game);

            Directory.CreateDirectory(Path.Combine(folder.Directory, "cache", "shaders"));
            File.WriteAllText(Path.Combine(folder.Directory, "cache", "shaders", "a.bin"), "compiled");

            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);
        }

        [Test]
        public void TestRefreshReplacesOnlyOrthomaniaFiles()
        {
            var folder = folders.Create(install, addBuild(), game);
            var other = new GameSettings(temp.Combine("other-data"), osuSignIn: true);

            folder = folders.Refresh(folder, addBuild(build: "0.1.1", files: new Dictionary<string, string>
            {
                ["osu.Game.Rulesets.ManiaOrtho.dll"] = "ruleset 0.1.1",
            }), other);

            Assert.That(folder.Record.Build, Is.EqualTo("0.1.1"));
            Assert.That(read(Path.Combine(folder.Directory, "osu.Game.Rulesets.ManiaOrtho.dll")), Is.EqualTo("ruleset 0.1.1"));
            Assert.That(File.Exists(Path.Combine(folder.Directory, "0Harmony.dll")), Is.False, "the new build no longer ships it");
            var settings = GameSettings.Parse(read(Path.Combine(folder.Directory, GameSettings.FILE_NAME)))!;
            Assert.That(settings.DataFolder, Is.EqualTo(temp.Combine("other-data")));
            Assert.That(settings.OsuSignIn, Is.True);
            Assert.That(folder.Record.DataFolder, Is.EqualTo(temp.Combine("other-data")));
            Assert.That(folder.Record.OsuSignIn, Is.True);
            Assert.That(read(folder.GameExecutable), Is.EqualTo("game 2026.1026.0"));
            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);
        }

        [Test]
        public void TestCreateAgainReplacesTheFolder()
        {
            var first = folders.Create(install, addBuild(), game);
            File.Delete(Path.Combine(first.Directory, "osu.Framework.dll"));

            var again = folders.Create(install, addBuild(build: "0.1.1"), game);

            Assert.That(again.Record.Build, Is.EqualTo("0.1.1"));
            Assert.That(VersionFolders.Verify(again, out string? problem), Is.True, problem);
            Assert.That(Directory.GetDirectories(folders.Root), Has.Length.EqualTo(1));
        }

        [Test]
        public void TestPruneKeepsOnlyWhatItIsTold()
        {
            var old = folders.Create(install, addBuild(), game);

            Fakes.UpdateLazer(install, "2026.1105.0");
            var current = folders.Create(install, addBuild(lazer: "2026.1105.0"), game);

            Directory.CreateDirectory(Path.Combine(folders.Root, "2026.1200.0.partial"));

            Assert.That(folders.All().Select(f => f.Record.Lazer), Is.EqualTo(new[] { "2026.1105.0", "2026.1026.0" }));

            folders.Prune(new[] { current });

            Assert.That(folders.All().Select(f => f.Record.Lazer), Is.EqualTo(new[] { "2026.1105.0" }));
            Assert.That(Directory.GetDirectories(folders.Root), Has.Length.EqualTo(1));
            Assert.That(Directory.Exists(old.Directory), Is.False);
            Assert.That(read(install.GameExecutable), Is.EqualTo("game 2026.1105.0 (bigger)"));
        }

        private static Dictionary<string, string> snapshot(string root)
            => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllText);
    }
}
