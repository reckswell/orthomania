using System.Linq;
using NUnit.Framework;
using OrthoServer.Scoring;

namespace OrthoServer.Tests
{
    /// <summary>
    /// Totals worked out by hand from osu!'s rules (UserTotalPerformanceAggregateHelper, CheckIsUserHigh).
    /// </summary>
    [TestFixture]
    public class ProfileCalculatorTest
    {
        // (417 - 1/3) * (1 - 0.995^n), worked out by hand.
        private const double bonus_1 = 2.0833333333;
        private const double bonus_3 = 6.2188020833;

        [Test]
        public void TestWeightingAndBonus()
        {
            var profile = ProfileCalculator.Calculate(new[]
            {
                TestPlays.Rated(1, 101, 100),
                TestPlays.Rated(2, 102, 300),
                TestPlays.Rated(3, 103, 200),
            });

            // 300 + 200 * 0.95 + 100 * 0.95^2 = 580.25
            Assert.That(profile.TotalPp, Is.EqualTo(580.25 + bonus_3).Within(1e-6));
            Assert.That(profile.BonusPp, Is.EqualTo(bonus_3).Within(1e-6));
            Assert.That(profile.CountedScores, Is.EqualTo(3));
            Assert.That(profile.Top.Select(t => t.Play.Id), Is.EqualTo(new long[] { 2, 3, 1 }));
            Assert.That(profile.Top.Select(t => t.Weight), Is.EqualTo(new[] { 1, 0.95, 0.9025 }).Within(1e-12));
            // Every play at 97%: the weighted accuracy is 97%.
            Assert.That(profile.Accuracy, Is.EqualTo(97).Within(1e-9));
        }

        [Test]
        public void TestReplaysOfAMapCountOnceForPpAndAtMostTwiceForTheBonus()
        {
            var profile = ProfileCalculator.Calculate(new[]
            {
                TestPlays.Rated(1, 101, 300, totalScore: 900000),
                TestPlays.Rated(2, 101, 310, totalScore: 800000), // best pp: kept
                TestPlays.Rated(3, 101, 200, totalScore: 700000), // neither best pp nor best score: osu! deletes it
                TestPlays.Rated(4, 101, 320, totalScore: 950000, mods: "HD"), // other mods: kept on their own
            });

            Assert.That(ProfileCalculator.CountedScores(new[]
            {
                TestPlays.Rated(1, 101, 300, totalScore: 900000),
                TestPlays.Rated(2, 101, 310, totalScore: 800000),
                TestPlays.Rated(3, 101, 200, totalScore: 700000),
                TestPlays.Rated(4, 101, 320, totalScore: 950000, mods: "HD"),
            }).Select(p => p.Id), Is.EquivalentTo(new long[] { 1, 2, 4 }));

            // One map: only its best pp counts; three kept scores for the bonus.
            Assert.That(profile.Top, Has.Count.EqualTo(1));
            Assert.That(profile.TotalPp, Is.EqualTo(320 + bonus_3).Within(1e-6));
        }

        [Test]
        public void TestOneScoreThatIsBothBestsCountsOnce()
        {
            var profile = ProfileCalculator.Calculate(new[]
            {
                TestPlays.Rated(1, 101, 300, totalScore: 900000),
                TestPlays.Rated(2, 101, 250, totalScore: 850000),
            });

            Assert.That(profile.CountedScores, Is.EqualTo(1));
            Assert.That(profile.TotalPp, Is.EqualTo(300 + bonus_1).Within(1e-6));
        }

        [Test]
        public void TestPlaysThatAwardNoPpAreLeftOut()
        {
            var profile = ProfileCalculator.Calculate(new[]
            {
                TestPlays.Rated(1, 101, 100),
                TestPlays.Rated(2, 102, 1000, awardsPp: false),
            });

            Assert.That(profile.CountedScores, Is.EqualTo(1));
            Assert.That(profile.TotalPp, Is.EqualTo(100 + bonus_1).Within(1e-6));
        }

        [Test]
        public void TestAtMostAThousandScores()
        {
            var plays = Enumerable.Range(1, 1200).Select(i => TestPlays.Rated(i, 100000 + i, i)).ToList();
            var counted = ProfileCalculator.CountedScores(plays);

            Assert.That(counted, Has.Count.EqualTo(1000));
            Assert.That(counted.Min(p => p.Pp), Is.EqualTo(201));
        }

        [Test]
        public void TestNoPlays()
        {
            var profile = ProfileCalculator.Calculate(System.Array.Empty<PlayRecord>());

            Assert.That(profile.TotalPp, Is.Zero);
            Assert.That(profile.Accuracy, Is.Zero);
            Assert.That(profile.Top, Is.Empty);
        }
    }
}
