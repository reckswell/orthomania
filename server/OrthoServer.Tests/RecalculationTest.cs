using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using OrthoServer.Accounts;
using OrthoServer.Live;
using OrthoServer.Scoring;
using OrthoServer.Storage;

namespace OrthoServer.Tests
{
    [TestFixture]
    public class RecalculationTest
    {
        private string dataPath = null!;

        [SetUp]
        public void SetUp() => dataPath = Path.Combine(Path.GetTempPath(), "ortho-server-test-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataPath, true);
        }

        [Test]
        public void TestPlaysFromAnotherCalculatorVersionAreRatedAgain()
        {
            var service = new PlayService(new PlayStore(dataPath), new AccountStore(dataPath));
            byte[] map = TestPlays.Map();

            var result = service.Submit(TestPlays.Upload(map), map, null);

            // As if the play had been rated by an older calculator.
            var stored = service.Store.All().Single();
            stored.Pp = 1;
            stored.CalculatorVersion = "2020.101.0";
            service.Store.UpdateRating(stored);

            // A new server (as after an update) rates only what is outdated.
            var restarted = new PlayService(new PlayStore(dataPath), new AccountStore(dataPath));
            Assert.That(restarted.Recalculate(onlyOutdated: true), Is.EqualTo((1, 0)));
            Assert.That(restarted.Recalculate(onlyOutdated: true), Is.EqualTo((0, 0)));

            var again = restarted.Store.All().Single();
            Assert.That(again.CalculatorVersion, Is.EqualTo(PlayCalculator.CalculatorVersion));
            Assert.That(again.Pp, Is.EqualTo(result.Pp).Within(1e-9));
        }

        [Test]
        public void TestPlayWhoseMapIsMissingIsReported()
        {
            var service = new PlayService(new PlayStore(dataPath), new AccountStore(dataPath));
            byte[] map = TestPlays.Map();
            service.Submit(TestPlays.Upload(map), map, null);

            File.Delete(service.Store.BeatmapPath(PlayCalculator.Md5(map)));

            Assert.That(service.Recalculate(onlyOutdated: false), Is.EqualTo((0, 1)));
        }
    }

    [TestFixture]
    public class LiveCalculatorVersionTest
    {
        // The start of what github.com/ppy/osu-queue-score-statistics.git/info/refs?service=git-upload-pack answers.
        private const string advertisement = "001e# service=git-upload-pack\n0000"
                                              + "0155c0a0c3a0000000000000000000000000000000 HEAD\0multi_ack thin-pack side-band\n"
                                              + "003fc0a0c3a0000000000000000000000000000000 refs/heads/master\n"
                                              + "0041aaaa000000000000000000000000000000000000 refs/tags/2026.923.0\n"
                                              + "0044bbbb000000000000000000000000000000000000 refs/tags/2026.923.0^{}\n"
                                              + "0042cccc000000000000000000000000000000000000 refs/tags/2026.1019.0\n"
                                              + "0041dddd000000000000000000000000000000000000 refs/tags/2026.908.0\n"
                                              + "0045eeee000000000000000000000000000000000000 refs/tags/production-import\n"
                                              + "0000";

        [Test]
        public void TestNewestReleaseByVersionNotByName()
        {
            var tags = LiveCalculatorVersion.ParseTags(advertisement).ToList();

            Assert.That(tags, Is.EquivalentTo(new[] { "2026.923.0", "2026.1019.0", "2026.908.0", "production-import" }));
            // "2026.923.0" sorts after "2026.1019.0" by name.
            Assert.That(LiveCalculatorVersion.LatestReleaseTag(tags), Is.EqualTo("2026.1019.0"));
        }

        [Test]
        public void TestManiaPackageVersionFromProjectFile()
        {
            const string project = """
                                   <ItemGroup>
                                       <PackageReference Include="ppy.osu.Game" Version="2026.730.0" />
                                       <PackageReference Include="ppy.osu.Game.Rulesets.Catch" Version="2026.730.0" />
                                       <PackageReference Include="ppy.osu.Game.Rulesets.Mania" Version="2026.730.0" />
                                   </ItemGroup>
                                   """;

            Assert.That(LiveCalculatorVersion.ParseManiaPackageVersion(project), Is.EqualTo("2026.730.0"));
            Assert.That(LiveCalculatorVersion.ParseManiaPackageVersion("<Project />"), Is.Null);
        }

        [TestCase("2026.730.0", "2026.730.0", false)]
        [TestCase("2026.730.0", "2026.1026.0", true)]
        [TestCase("2026.1026.0", "2026.730.0", true)]
        public void TestDiffers(string current, string live, bool differs)
            => Assert.That(LiveCalculatorVersion.Differs(current, live), Is.EqualTo(differs));

        [Test]
        public void TestServerWasBuiltWithTheVersionInTheFile()
        {
            string file = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "calculator-version.txt");
            Assert.That(PlayCalculator.CalculatorVersion, Is.EqualTo(File.ReadAllText(file).Trim()));
        }
    }
}
