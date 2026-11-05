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
        private string data = null!;

        [SetUp]
        public void SetUp()
        {
            temp = new TempFolder();
            install = Fakes.Lazer(temp.Combine("osulazer"));
            builds = new BuildStore(temp.Combine("orthomania", "builds"));
            folders = new VersionFolders(temp.Combine("orthomania", "lazer"));
            data = temp.Combine("ortho-data");
        }

        [TearDown]
        public void TearDown() => temp.Dispose();

        private RulesetBuild addBuild(string lazer = "2026.1026.0", string build = "0.1.0", Dictionary<string, string>? files = null)
            => builds.Add(Fakes.Build(temp.Combine("staging-" + build + "-" + lazer), lazer, build, files));

        private static string read(string path) => File.ReadAllText(path);

        [Test]
        public void TestCreatesAFolderOfLinksAndOrthomania()
        {
            var folder = folders.Create(install, addBuild(), data);

            Assert.That(folder.Directory, Is.EqualTo(Path.Combine(folders.Root, "2026.1026.0")));
            Assert.That(folder.Lazer, Is.EqualTo(LazerVersion.Parse("2026.1026.0")));
            Assert.That(folder.Record.Linked, Is.True);

            foreach (string file in Directory.GetFiles(install.Current, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(install.Current, file);
                Assert.That(read(Path.Combine(folder.Directory, relative)), Is.EqualTo(read(file)), relative);
            }

            Assert.That(read(Path.Combine(folder.Directory, VersionFolders.FRAMEWORK_INI)), Is.Empty);
            Assert.That(read(Path.Combine(folder.Directory, VersionFolders.STORAGE_INI)), Is.EqualTo($"FullPath = {data}\r\n"));
            Assert.That(read(Path.Combine(folder.Directory, "osu.Game.Rulesets.ManiaOrtho.dll")), Is.EqualTo("ruleset 0.1.0 for 2026.1026.0"));
            Assert.That(read(Path.Combine(folder.Directory, "0Harmony.dll")), Is.EqualTo("harmony 2.4.3"));

            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);
            Assert.That(folders.Get(LazerVersion.Parse("2026.1026.0")), Is.Not.Null);
            Assert.That(Directory.GetDirectories(folders.Root), Has.Length.EqualTo(1), "no leftovers");
        }

        [Test]
        public void TestLazerFilesAreLinksNotCopies()
        {
            var folder = folders.Create(install, addBuild(), data);

            // Only a test ever writes into lazer's files: a link sees the change, a copy would not.
            File.AppendAllText(Path.Combine(install.Current, "osu.Framework.dll"), " changed");

            Assert.That(read(Path.Combine(folder.Directory, "osu.Framework.dll")), Does.EndWith(" changed"));
        }

        [Test]
        public void TestLeavesLazerInstallAsItWas()
        {
            var before = snapshot(install.Root);

            var folder = folders.Create(install, addBuild(), data);
            folders.Refresh(folder, addBuild(build: "0.1.1"), data);
            folders.Prune(new List<VersionFolder>());

            Assert.That(snapshot(install.Root), Is.EqualTo(before));
        }

        [Test]
        public void TestNeverWritesThroughALinkToLazer()
        {
            // Were lazer ever to ship files of these names, orthomania's must not change lazer's copies.
            File.WriteAllText(Path.Combine(install.Current, VersionFolders.STORAGE_INI), "lazer's own");
            File.WriteAllText(Path.Combine(install.Current, VersionFolders.FRAMEWORK_INI), "lazer's own");

            var folder = folders.Create(install, addBuild(), data);

            Assert.That(read(Path.Combine(install.Current, VersionFolders.STORAGE_INI)), Is.EqualTo("lazer's own"));
            Assert.That(read(Path.Combine(install.Current, VersionFolders.FRAMEWORK_INI)), Is.EqualTo("lazer's own"));
            Assert.That(read(Path.Combine(folder.Directory, VersionFolders.STORAGE_INI)), Does.StartWith("FullPath = "));
            Assert.That(folder.Record.LazerFiles.Select(f => f.Path), Has.None.EqualTo(VersionFolders.STORAGE_INI));
            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);

            folders.Refresh(folder, addBuild(build: "0.1.1"), temp.Combine("other-data"));

            Assert.That(read(Path.Combine(install.Current, VersionFolders.STORAGE_INI)), Is.EqualTo("lazer's own"));
        }

        [Test]
        public void TestABuildCannotReplaceLazerFiles()
        {
            var build = addBuild(files: new Dictionary<string, string>
            {
                ["osu.Game.Rulesets.ManiaOrtho.dll"] = "ruleset",
                ["osu.Framework.dll"] = "not lazer's",
            });

            Assert.That(() => folders.Create(install, build, data), Throws.TypeOf<InvalidDataException>());

            Assert.That(read(Path.Combine(install.Current, "osu.Framework.dll")), Is.EqualTo("framework 2026.1026.0"));
            Assert.That(Directory.GetDirectories(folders.Root), Is.Empty, "the unfinished folder is gone");
        }

        [Test]
        public void TestRefusesABuildForAnotherRelease()
        {
            Assert.That(() => folders.Create(install, addBuild(lazer: "2026.905.0"), data), Throws.InvalidOperationException);
            Assert.That(Directory.Exists(folders.Root) ? Directory.GetDirectories(folders.Root) : new string[0], Is.Empty);
        }

        [Test]
        public void TestFolderOutlivesALazerUpdate()
        {
            var folder = folders.Create(install, addBuild(), data);

            Fakes.UpdateLazer(install, "2026.1105.0");

            Assert.That(install.ReadRelease().Version, Is.EqualTo(LazerVersion.Parse("2026.1105.0")));
            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);
            Assert.That(read(folder.GameExecutable), Is.EqualTo("game 2026.1026.0"), "the old release is still whole, to fall back on");
        }

        [Test]
        public void TestVerifyNoticesDamage()
        {
            var folder = folders.Create(install, addBuild(), data);

            File.Delete(Path.Combine(folder.Directory, "runtimes", "win-x64", "native", "SDL3.dll"));
            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.False);
            Assert.That(problem, Does.Contain("SDL3.dll"));

            folder = folders.Create(install, addBuild(), data);
            File.WriteAllText(Path.Combine(folder.Directory, "0Harmony.dll"), "harmony 2.4.4");
            Assert.That(VersionFolders.Verify(folder, out problem), Is.False);
            Assert.That(problem, Does.Contain("0Harmony.dll"));
        }

        [Test]
        public void TestVerifyNoticesLazerFilesChangingRelease()
        {
            var folder = folders.Create(install, addBuild(), data);

            // An updater writing a new release into the old files (which lazer's does not) would change the linked files too.
            // File.WriteAllText writes into the existing file.
            Fakes.WriteVersionManifest(install.Current, "2026.1105.0");

            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.False);
            Assert.That(problem, Does.Contain("no longer release 2026.1026.0"));
        }

        [Test]
        public void TestIgnoresFilesLazerAddsWhileRunning()
        {
            var folder = folders.Create(install, addBuild(), data);

            Directory.CreateDirectory(Path.Combine(folder.Directory, "cache", "shaders"));
            File.WriteAllText(Path.Combine(folder.Directory, "cache", "shaders", "a.bin"), "compiled");

            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);
        }

        [Test]
        public void TestRefreshReplacesOnlyOrthomaniaFiles()
        {
            var folder = folders.Create(install, addBuild(), data);
            string other = temp.Combine("other-data");

            folder = folders.Refresh(folder, addBuild(build: "0.1.1", files: new Dictionary<string, string>
            {
                ["osu.Game.Rulesets.ManiaOrtho.dll"] = "ruleset 0.1.1",
            }), other);

            Assert.That(folder.Record.Build, Is.EqualTo("0.1.1"));
            Assert.That(read(Path.Combine(folder.Directory, "osu.Game.Rulesets.ManiaOrtho.dll")), Is.EqualTo("ruleset 0.1.1"));
            Assert.That(File.Exists(Path.Combine(folder.Directory, "0Harmony.dll")), Is.False, "the new build no longer ships it");
            Assert.That(read(Path.Combine(folder.Directory, VersionFolders.STORAGE_INI)), Is.EqualTo($"FullPath = {other}\r\n"));
            Assert.That(read(folder.GameExecutable), Is.EqualTo("game 2026.1026.0"));
            Assert.That(VersionFolders.Verify(folder, out string? problem), Is.True, problem);
        }

        [Test]
        public void TestCreateAgainReplacesTheFolder()
        {
            var first = folders.Create(install, addBuild(), data);
            File.Delete(Path.Combine(first.Directory, "osu.Framework.dll"));

            var again = folders.Create(install, addBuild(build: "0.1.1"), data);

            Assert.That(again.Record.Build, Is.EqualTo("0.1.1"));
            Assert.That(VersionFolders.Verify(again, out string? problem), Is.True, problem);
            Assert.That(Directory.GetDirectories(folders.Root), Has.Length.EqualTo(1));
        }

        [Test]
        public void TestPruneKeepsOnlyWhatItIsTold()
        {
            var old = folders.Create(install, addBuild(), data);

            Fakes.UpdateLazer(install, "2026.1105.0");
            var current = folders.Create(install, addBuild(lazer: "2026.1105.0"), data);

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
