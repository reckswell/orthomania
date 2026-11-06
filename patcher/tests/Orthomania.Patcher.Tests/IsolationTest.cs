using System.IO;
using NUnit.Framework;

namespace Orthomania.Patcher.Tests
{
    [TestFixture]
    public class IsolationTest
    {
        [Test]
        public void TestUsualPlacesAreApart()
        {
            using var temp = new TempFolder();
            var install = Fakes.Lazer(temp.Combine("osulazer"));
            var layout = new PatcherLayout(temp.Combine("orthomania"));

            Assert.That(Isolation.Check(layout, install, temp.Combine("ortho-data"), new[] { temp.Combine("osu") }), Is.Empty);
        }

        [Test]
        public void TestRefusesSharedPlaces()
        {
            using var temp = new TempFolder();
            var install = Fakes.Lazer(temp.Combine("osulazer"));
            var osu = new[] { temp.Combine("osu") };

            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("osulazer", "orthomania")), install, temp.Combine("ortho-data"), osu), Is.Not.Empty,
                "orthomania inside lazer's install");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Combine("osulazer", "data"), osu), Is.Not.Empty,
                "data inside lazer's install");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Combine("osu"), osu), Is.Not.Empty,
                "osu!'s data");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Path, osu), Is.Not.Empty,
                "a folder holding osu!'s data");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Combine("orthomania", "lazer", "data"), osu), Is.Not.Empty,
                "data inside the version folders");
        }

        [Test]
        public void TestRefusesADataFolderWithOsuDataInIt()
        {
            using var temp = new TempFolder();
            var install = Fakes.Lazer(temp.Combine("osulazer"));
            var layout = new PatcherLayout(temp.Combine("orthomania"));
            string moved = temp.Combine("lazer-moved");

            Directory.CreateDirectory(moved);
            File.WriteAllText(Path.Combine(moved, "client.realm"), "osu!'s data, moved by osu!");

            Assert.That(Isolation.Check(layout, install, moved, new[] { temp.Combine("osu") }), Has.One.Contains("holds osu!'s own data (client.realm)"));
        }
    }
}
