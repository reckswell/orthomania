using NUnit.Framework;
using Orthomania.Patcher.Lazer;

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
            var plain = new[] { temp.Combine("osu"), temp.Combine("lazer-moved") };

            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("osulazer", "orthomania")), install, temp.Combine("ortho-data"), plain), Is.Not.Empty,
                "orthomania inside lazer's install");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Combine("osulazer", "data"), plain), Is.Not.Empty,
                "data inside lazer's install");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Combine("osu"), plain), Is.Not.Empty,
                "plain lazer's data");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Combine("lazer-moved"), plain), Is.Not.Empty,
                "plain lazer's moved data");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Path, plain), Is.Not.Empty,
                "a folder holding plain lazer's data");
            Assert.That(Isolation.Check(new PatcherLayout(temp.Combine("orthomania")), install, temp.Combine("orthomania", "lazer", "data"), plain), Is.Not.Empty,
                "data inside the version folders");
        }
    }
}
