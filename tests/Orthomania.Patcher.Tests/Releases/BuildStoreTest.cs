using System.IO;
using NUnit.Framework;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher.Tests.Releases
{
    [TestFixture]
    public class BuildStoreTest
    {
        private static readonly LazerVersion lazer = LazerVersion.Parse("2026.1026.0");

        [Test]
        public void TestAddsAndFindsTheNewestBuild()
        {
            using var temp = new TempFolder();
            var store = new BuildStore(temp.Combine("builds"));

            store.Add(Fakes.Build(temp.Combine("a"), build: "0.1.9"));
            store.Add(Fakes.Build(temp.Combine("b"), build: "0.1.10"));
            store.Add(Fakes.Build(temp.Combine("c"), lazer: "2026.905.0", build: "0.2.0"));

            Assert.That(store.FindNewest(lazer)!.Build, Is.EqualTo("0.1.10"), "0.1.10 is after 0.1.9, and 0.2.0 is for another release");
            Assert.That(store.FindNewest(LazerVersion.Parse("2026.1100.0")), Is.Null);
            Assert.That(Directory.Exists(temp.Combine("a")), Is.False, "moved in");
        }

        [Test]
        public void TestRefusesADamagedBuild()
        {
            using var temp = new TempFolder();
            var store = new BuildStore(temp.Combine("builds"));
            string staging = Fakes.Build(temp.Combine("a"));

            File.AppendAllText(Path.Combine(staging, "0Harmony.dll"), "!");

            Assert.That(() => store.Add(staging), Throws.TypeOf<InvalidDataException>());
            Assert.That(store.FindNewest(lazer), Is.Null);
        }

        [Test]
        public void TestSkipsABuildDamagedLater()
        {
            using var temp = new TempFolder();
            var store = new BuildStore(temp.Combine("builds"));

            store.Add(Fakes.Build(temp.Combine("a"), build: "0.1.0"));
            var newer = store.Add(Fakes.Build(temp.Combine("b"), build: "0.1.1"));

            File.Delete(Path.Combine(newer.Directory, "osu.Game.Rulesets.ManiaOrtho.dll"));

            Assert.That(store.FindNewest(lazer)!.Build, Is.EqualTo("0.1.0"));
        }

        [Test]
        public void TestIgnoresABuildFiledUnderAnotherRelease()
        {
            using var temp = new TempFolder();
            var store = new BuildStore(temp.Combine("builds"));

            // A build for 2026.905.0 placed (by hand) where 2026.1026.0's go.
            Fakes.Build(store.FolderFor(lazer, "0.9.0"), lazer: "2026.905.0", build: "0.9.0");

            Assert.That(store.FindNewest(lazer), Is.Null);
        }

        [Test]
        public void TestPrunes()
        {
            using var temp = new TempFolder();
            var store = new BuildStore(temp.Combine("builds"));

            var keep = store.Add(Fakes.Build(temp.Combine("a"), build: "0.1.1"));
            store.Add(Fakes.Build(temp.Combine("b"), build: "0.1.0"));
            store.Add(Fakes.Build(temp.Combine("c"), lazer: "2026.905.0"));

            store.Prune(new[] { keep });

            Assert.That(Directory.GetDirectories(store.Root), Has.Length.EqualTo(1));
            Assert.That(store.FindNewest(lazer)!.Build, Is.EqualTo("0.1.1"));
        }
    }
}
