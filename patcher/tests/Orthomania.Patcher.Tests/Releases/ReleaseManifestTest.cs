using System.IO;
using NUnit.Framework;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher.Tests.Releases
{
    [TestFixture]
    public class ReleaseManifestTest
    {
        private const string sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static string manifest(string files, string lazer = "2026.1026.0", int format = 1)
            => $"{{\"format\":{format},\"lazer\":\"{lazer}\",\"build\":\"0.1.0\",\"files\":[{files}]}}";

        private static string file(string name, string hash = sha, long size = 10) => $"{{\"name\":\"{name}\",\"sha256\":\"{hash}\",\"size\":{size}}}";

        [Test]
        public void TestReadsAManifest()
        {
            var read = ReleaseManifest.Parse(manifest(file("osu.Game.Rulesets.ManiaOrtho.dll") + "," + file("0Harmony.dll")));

            Assert.That(read.LazerVersion.Number, Is.EqualTo("2026.1026.0"));
            Assert.That(read.Build, Is.EqualTo("0.1.0"));
            Assert.That(read.Files, Has.Count.EqualTo(2));
        }

        [Test]
        public void TestRoundTrips()
        {
            var original = ReleaseManifest.Parse(manifest(file("osu.Game.Rulesets.ManiaOrtho.dll")));
            var again = ReleaseManifest.Parse(original.Serialise());

            Assert.That(again.Lazer, Is.EqualTo(original.Lazer));
            Assert.That(again.Files[0].Sha256, Is.EqualTo(sha));
        }

        [TestCase("..\\\\osu!.exe.dll")]
        [TestCase("../osu.Game.Rulesets.X.dll")]
        [TestCase("sub/osu.Game.Rulesets.X.dll")]
        [TestCase("C:osu.Game.Rulesets.X.dll")]
        [TestCase("osu.Game.Rulesets.X.exe")]
        [TestCase("storage.ini")]
        public void TestRefusesUnsafeFiles(string name)
        {
            Assert.That(() => ReleaseManifest.Parse(manifest(file("osu.Game.Rulesets.ManiaOrtho.dll") + "," + file(name))), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void TestRefusesBadManifests()
        {
            Assert.That(() => ReleaseManifest.Parse(manifest(file("osu.Game.Rulesets.ManiaOrtho.dll"), format: 2)), Throws.TypeOf<InvalidDataException>(), "unknown format");
            Assert.That(() => ReleaseManifest.Parse(manifest(file("osu.Game.Rulesets.ManiaOrtho.dll"), lazer: "latest")), Throws.TypeOf<InvalidDataException>(), "no release");
            Assert.That(() => ReleaseManifest.Parse(manifest("")), Throws.TypeOf<InvalidDataException>(), "no files");
            Assert.That(() => ReleaseManifest.Parse(manifest(file("0Harmony.dll"))), Throws.TypeOf<InvalidDataException>(), "no ruleset");
            Assert.That(() => ReleaseManifest.Parse(manifest(file("osu.Game.Rulesets.ManiaOrtho.dll") + "," + file("OSU.GAME.RULESETS.MANIAORTHO.DLL"))),
                Throws.TypeOf<InvalidDataException>(), "listed twice");
            Assert.That(() => ReleaseManifest.Parse(manifest(file("osu.Game.Rulesets.ManiaOrtho.dll", hash: "abc"))), Throws.TypeOf<InvalidDataException>(), "short hash");
            Assert.That(() => ReleaseManifest.Parse(manifest(file("osu.Game.Rulesets.ManiaOrtho.dll", size: 0))), Throws.TypeOf<InvalidDataException>(), "no size");
        }
    }
}
