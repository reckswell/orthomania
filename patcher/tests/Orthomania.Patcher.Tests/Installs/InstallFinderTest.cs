using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Orthomania.Patcher.Installs;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Tests.Installs
{
    [TestFixture]
    public class InstallFinderTest
    {
        private TempFolder temp = null!;
        private List<string> log = null!;

        [SetUp]
        public void SetUp()
        {
            temp = new TempFolder();
            log = new List<string>();
        }

        [TearDown]
        public void TearDown() => temp.Dispose();

        private string stable(params string[] path)
        {
            string folder = temp.Combine(path);
            Directory.CreateDirectory(Path.Combine(folder, "Songs"));
            File.WriteAllText(Path.Combine(folder, LazerInstall.GAME_EXECUTABLE), "stable");
            return folder;
        }

        [Test]
        public void TestIdentifiesLazerFromItsFolderOrItsGame()
        {
            var install = Fakes.Lazer(temp.Combine("osulazer"), "2026.1105.0");

            foreach (string path in new[] { install.Root, install.Current, install.GameExecutable, $"\"{install.GameExecutable}\"", install.Root + Path.DirectorySeparatorChar })
            {
                var found = OsuInstall.Identify(path);

                Assert.That(found, Is.Not.Null, path);
                Assert.That(found!.Kind, Is.EqualTo(OsuKind.Lazer));
                Assert.That(found.Folder, Is.EqualTo(PathUtils.Normalise(install.Root)));
                Assert.That(found.Release, Is.EqualTo("2026.1105.0"));
            }
        }

        [Test]
        public void TestIdentifiesStable()
        {
            string folder = stable("Games", "osu!");

            var found = OsuInstall.Identify(Path.Combine(folder, "osu!.exe"));

            Assert.That(found!.Kind, Is.EqualTo(OsuKind.Stable));
            Assert.That(found.Folder, Is.EqualTo(PathUtils.Normalise(folder)));
            Assert.That(found.ToString(), Does.StartWith("osu!(stable)"));
        }

        [Test]
        public void TestNotOsu()
        {
            Directory.CreateDirectory(temp.Combine("empty"));
            Directory.CreateDirectory(temp.Combine("just-an-exe"));
            File.WriteAllText(temp.Combine("just-an-exe", "osu!.exe"), "not stable: nothing of stable's beside it");

            // A copy of lazer's files outside its installer's layout: neither stable nor an install lazer's updater can update.
            Directory.CreateDirectory(temp.Combine("portable"));
            File.WriteAllText(temp.Combine("portable", "osu!.exe"), "lazer");
            File.WriteAllText(temp.Combine("portable", "osu.Game.dll"), "lazer");
            Directory.CreateDirectory(temp.Combine("portable", "Songs"));

            Assert.That(OsuInstall.Identify(temp.Combine("empty")), Is.Null);
            Assert.That(OsuInstall.Identify(temp.Combine("just-an-exe")), Is.Null);
            Assert.That(OsuInstall.Identify(temp.Combine("portable")), Is.Null);
            Assert.That(OsuInstall.Identify(temp.Combine("nowhere")), Is.Null);
            Assert.That(OsuInstall.Identify(""), Is.Null);
            Assert.That(OsuInstall.Identify(null), Is.Null);
        }

        [TestCase("\"C:\\osu!\\osu!.exe\" \"%1\"", "C:\\osu!\\osu!.exe")]
        [TestCase("C:\\Program Files\\osu!\\osu!.exe,0", "C:\\Program Files\\osu!\\osu!.exe")]
        [TestCase("C:\\Users\\Alpha\\AppData\\Local\\osulazer", "C:\\Users\\Alpha\\AppData\\Local\\osulazer")]
        [TestCase("  \"D:\\games\\osulazer\\current\\osu!.exe\"  ", "D:\\games\\osulazer\\current\\osu!.exe")]
        [TestCase("", null)]
        [TestCase("\"", null)]
        public void TestCommandLinePath(string value, string? expected) => Assert.That(InstallFinder.CommandLinePath(value), Is.EqualTo(expected));

        [Test]
        public void TestFindsKnownPlacesAndSearches()
        {
            var lazer = Fakes.Lazer(temp.Combine("AppData", "osulazer"), "2026.1105.0");
            var olderLazer = Fakes.Lazer(temp.Combine("drive", "Games", "osulazer"), "2026.1026.0");
            string stableFolder = stable("drive", "osu!");
            string deep = stable("drive", "a", "b", "c", "osu!");
            stable("drive", "node_modules", "osu!");

            var places = new FakePlaces
            {
                Known = { $"\"{lazer.GameExecutable}\" \"%1\"", temp.Combine("not-there"), "" },
                Searched = { new SearchRoot(temp.Combine("drive"), 3) },
            };

            var found = new InstallFinder(places, log.Add).Find(CancellationToken.None);

            Assert.That(found.Select(i => (i.Kind, i.Folder)), Is.EqualTo(new[]
            {
                (OsuKind.Lazer, PathUtils.Normalise(lazer.Root)),
                (OsuKind.Lazer, PathUtils.Normalise(olderLazer.Root)),
                (OsuKind.Stable, PathUtils.Normalise(stableFolder)),
            }));

            // Four levels down is past the search, and node_modules is never searched.
            Assert.That(found.Any(i => i.Folder == PathUtils.Normalise(deep)), Is.False);
            Assert.That(log.Single(), Does.Contain("osu!(lazer) 2026.1105.0"));
        }

        [Test]
        public void TestSearchStopsAtItsLimit()
        {
            for (int i = 0; i < 30; i++)
                Directory.CreateDirectory(temp.Combine("drive", $"folder{i:00}"));

            stable("drive", "zz", "osu!");

            var places = new FakePlaces { Searched = { new SearchRoot(temp.Combine("drive"), 3) } };
            var found = new InstallFinder(places, log.Add) { MaxFolders = 10 }.Find(CancellationToken.None);

            Assert.That(found, Is.Empty);
            Assert.That(log.Single(), Does.Contain("stopped at the limit"));
        }

        [Test]
        public void TestSearchCanBeCancelled()
        {
            Directory.CreateDirectory(temp.Combine("drive"));
            var places = new FakePlaces { Searched = { new SearchRoot(temp.Combine("drive"), 3) } };

            Assert.Throws<OperationCanceledException>(() => new InstallFinder(places, log.Add).Find(new CancellationToken(canceled: true)));
        }

        [Test]
        public void TestThisComputerIsSearchedWithoutFailing()
        {
            // Whatever is on the computer running the tests: the search must end, within its limits, without throwing.
            var finder = new InstallFinder(new InstallPlaces(), log.Add) { MaxFolders = 2000, TimeLimit = TimeSpan.FromSeconds(5) };

            Assert.DoesNotThrow(() => finder.Find(CancellationToken.None));
            Assert.That(log.Single(), Does.StartWith("searched "));
        }

        private sealed class FakePlaces : IInstallPlaces
        {
            public readonly List<string> Known = new List<string>();
            public readonly List<SearchRoot> Searched = new List<SearchRoot>();

            IEnumerable<string> IInstallPlaces.Known() => Known;

            IEnumerable<SearchRoot> IInstallPlaces.Searched() => Searched;
        }
    }
}
