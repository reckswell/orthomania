using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using osu.Game.Online.API;
using OrthoServer.Scoring;

namespace OrthoServer.Tests
{
    [TestFixture]
    public class PlayCalculatorTest
    {
        private static PlayRecord play(string status = "Ranked", bool passed = true, params string[] mods)
        {
            var record = new PlayRecord
            {
                BeatmapId = 0,
                Status = status,
                Passed = passed,
                Statistics = new Dictionary<string, int> { ["Perfect"] = 500, ["Great"] = 30, ["Good"] = 5, ["Miss"] = 2 },
                Accuracy = 0.98,
                MaxCombo = 300,
                TotalScore = 900000,
            };

            foreach (string mod in mods)
                record.Mods.Add(new APIMod { Acronym = mod });

            return record;
        }

        [Test]
        public void TestRankedPassAwardsPp()
        {
            var record = play();
            PlayCalculator.Rate(record, TestPlays.Map());

            Assert.That(record.AwardsPp, Is.True, record.Reason);
            Assert.That(record.Pp, Is.GreaterThan(0));
            Assert.That(record.Stars, Is.GreaterThan(0));
            Assert.That(record.CalculatorVersion, Is.EqualTo(PlayCalculator.CalculatorVersion));
        }

        [TestCase("Approved", true)]
        [TestCase("Loved", false)]
        [TestCase("Qualified", false)]
        [TestCase("Pending", false)]
        [TestCase("Graveyard", false)]
        [TestCase("LocallyModified", false)]
        [TestCase("None", false)]
        [TestCase("", false)]
        public void TestOnlyRankedAndApprovedMapsAwardPp(string status, bool awards)
        {
            var record = play(status);
            PlayCalculator.Rate(record, TestPlays.Map());

            Assert.That(record.AwardsPp, Is.EqualTo(awards), record.Reason);
            // The would-be pp is still worked out, for the player's interest.
            Assert.That(record.Pp, Is.GreaterThan(0));
        }

        [Test]
        public void TestFailAwardsNoPp()
        {
            var record = play(passed: false);
            PlayCalculator.Rate(record, TestPlays.Map());

            Assert.That(record.AwardsPp, Is.False);
            Assert.That(record.Reason, Is.EqualTo("Failed"));
        }

        [TestCase("DT", true)]
        [TestCase("HT", true)]
        [TestCase("HD", true)]
        [TestCase("FI", true)]
        [TestCase("MR", true)]
        [TestCase("NF", true)]
        [TestCase("EZ", true)]
        [TestCase("FL", true)]
        [TestCase("4K", true)]
        [TestCase("HR", false)] // not for osu!mania (osu! wiki, "Upgrading to lazer")
        [TestCase("CL", false)] // Classic: only for scores imported from osu!stable
        [TestCase("RD", false)]
        [TestCase("AT", false)]
        [TestCase("XX", false)] // unknown
        public void TestModsAwardPpAsOnOsu(string acronym, bool awards)
        {
            var record = play("Ranked", true, acronym);
            PlayCalculator.Rate(record, TestPlays.Map());

            Assert.That(record.AwardsPp, Is.EqualTo(awards), record.Reason);
        }

        [Test]
        public void TestModSettingsOtherThanDefaultAwardNoPp()
        {
            var record = play();
            record.Mods.Add(new APIMod { Acronym = "DT", Settings = new Dictionary<string, object> { ["speed_change"] = 1.3 } });
            PlayCalculator.Rate(record, TestPlays.Map());

            Assert.That(record.AwardsPp, Is.False);
            Assert.That(record.Reason, Does.Contain("DT"));
        }

        [Test]
        public void TestDoubleTimeRaisesStars()
        {
            var normal = play();
            var doubleTime = play("Ranked", true, "DT");

            PlayCalculator.Rate(normal, TestPlays.Map());
            PlayCalculator.Rate(doubleTime, TestPlays.Map());

            Assert.That(doubleTime.Stars, Is.GreaterThan(normal.Stars!.Value));
        }

        [Test]
        public void TestUnknownJudgementIsRejected()
        {
            var record = play();
            record.Statistics["Wonderful"] = 3;

            Assert.Throws<InvalidDataException>(() => PlayCalculator.Rate(record, TestPlays.Map()));
        }
    }
}
