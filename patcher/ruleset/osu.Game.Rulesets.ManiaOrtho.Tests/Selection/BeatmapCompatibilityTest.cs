// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Selection
{
    /// <summary>
    /// Covers the core patch that lists osu!mania beatmaps under maniaortho at song select
    /// (<see cref="Hooks.LazerHooks"/> on <see cref="BeatmapInfoExtensions.AllowGameplayWithRuleset"/>),
    /// and checks that upstream rulesets behave exactly as before.
    /// </summary>
    [TestFixture]
    public class BeatmapCompatibilityTest
    {
        private static readonly RulesetInfo ortho = new ManiaOrthoRuleset().RulesetInfo;
        private static readonly RulesetInfo mania = new ManiaRuleset().RulesetInfo;

        // Only the short name and online ID of a beatmap's ruleset are read, so stand-ins are enough for other rulesets.
        private static readonly RulesetInfo osu = new RulesetInfo { ShortName = "osu", OnlineID = 0 };
        private static readonly RulesetInfo taiko = new RulesetInfo { ShortName = "taiko", OnlineID = 1 };
        private static readonly RulesetInfo fruits = new RulesetInfo { ShortName = "fruits", OnlineID = 2 };

        [Test]
        public void TestDeclaresManiaBeatmapsAsItsOwn()
        {
            Assert.That(new ManiaOrthoRuleset().PlaysBeatmapsOfRuleset, Is.EqualTo(mania.ShortName));
            Assert.That(Hooks.LazerHooks.Skipped, Is.Empty);
            Assert.That(Hooks.LazerHooks.Applied, Has.Count.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestManiaBeatmapsPlayUnderOrtho(bool allowConversion)
            => Assert.That(beatmapFor(mania).AllowGameplayWithRuleset(ortho, allowConversion), Is.True);

        [Test]
        public void TestOsuBeatmapsNeedConversionUnderOrtho()
        {
            Assert.That(beatmapFor(osu).AllowGameplayWithRuleset(ortho, allowConversion: false), Is.False);
            Assert.That(beatmapFor(osu).AllowGameplayWithRuleset(ortho, allowConversion: true), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestTaikoAndCatchBeatmapsNeverPlayUnderOrtho(bool allowConversion)
        {
            Assert.That(beatmapFor(taiko).AllowGameplayWithRuleset(ortho, allowConversion), Is.False);
            Assert.That(beatmapFor(fruits).AllowGameplayWithRuleset(ortho, allowConversion), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestUpstreamRulesetsUnchanged(bool allowConversion)
        {
            Assert.That(beatmapFor(mania).AllowGameplayWithRuleset(mania, allowConversion), Is.True);
            Assert.That(beatmapFor(taiko).AllowGameplayWithRuleset(mania, allowConversion), Is.False);
            Assert.That(beatmapFor(osu).AllowGameplayWithRuleset(mania, allowConversion), Is.EqualTo(allowConversion));

            // The reverse direction is not implied: mania does not play "maniaortho" beatmaps.
            Assert.That(beatmapFor(ortho).AllowGameplayWithRuleset(mania, allowConversion), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestSongSelectFilterListsManiaBeatmapsUnderOrtho(bool allowConversion)
        {
            var criteria = new FilterCriteria { Ruleset = ortho, AllowConvertedBeatmaps = allowConversion };

            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmapFor(mania), criteria), Is.True);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmapFor(taiko), criteria), Is.False);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmapFor(osu), criteria), Is.EqualTo(allowConversion));
        }

        private static BeatmapInfo beatmapFor(RulesetInfo ruleset) => new BeatmapInfo
        {
            Ruleset = ruleset,
            Difficulty = new BeatmapDifficulty { CircleSize = 4 },
            Metadata = new BeatmapMetadata { Title = "compatibility test" },
            DifficultyName = "test",
        };
    }
}
