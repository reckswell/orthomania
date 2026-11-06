// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Parity
{
    /// <summary>
    /// Fast, non-visual checks that the new ruleset is osu!mania underneath: same playable beatmap, same star rating,
    /// same pp, and never a legacy (submittable) ruleset.
    /// </summary>
    [TestFixture]
    public class RulesetParityTest
    {
        private static readonly object[] sources = PlaytestBeatmaps.DEFAULT_SOURCES.Cast<object>().ToArray();

        private static IEnumerable<Mod[]> modCombinations()
        {
            yield return System.Array.Empty<Mod>();
            yield return new Mod[] { new ManiaModDoubleTime() };
            yield return new Mod[] { new ManiaModHardRock() };
            yield return new Mod[] { new ManiaModEasy(), new ManiaModHalfTime() };
        }

        [TestCaseSource(nameof(sources))]
        public void TestPlayableBeatmapMatchesMania(string source)
        {
            var working = new TestWorkingBeatmap(PlaytestBeatmaps.Load(source));

            var mania = working.GetPlayableBeatmap(new ManiaRuleset().RulesetInfo);
            var ortho = working.GetPlayableBeatmap(new ManiaOrthoRuleset().RulesetInfo);

            Assert.That(describe(ortho), Is.EqualTo(describe(mania)));
        }

        /// <summary>
        /// The calculator decides whether a map is a convert from the ruleset it is given; as a convert it would list the key
        /// mods among its difficulty mod combinations (invariant 5).
        /// </summary>
        [TestCaseSource(nameof(sources))]
        public void TestDifficultyModCombinationsMatchMania(string source)
        {
            var working = new TestWorkingBeatmap(PlaytestBeatmaps.Load(source));

            string[] mania = new ManiaRuleset().CreateDifficultyCalculator(working).CreateDifficultyAdjustmentModCombinations().Select(describeMod).ToArray();
            string[] ortho = new ManiaOrthoRuleset().CreateDifficultyCalculator(working).CreateDifficultyAdjustmentModCombinations().Select(describeMod).ToArray();

            Assert.That(ortho, Is.EqualTo(mania));
            Assert.That(mania.Any(acronyms => acronyms.Contains('K')), Is.False, "a native mania map lists no key mods");

            static string describeMod(Mod mod) => mod is MultiMod multi ? string.Join("+", multi.Mods.Select(describeMod)) : mod.Acronym;
        }

        [TestCaseSource(nameof(sources))]
        public void TestStarRatingMatchesMania(string source)
        {
            var working = new TestWorkingBeatmap(PlaytestBeatmaps.Load(source));

            foreach (var mods in modCombinations())
            {
                double mania = new ManiaRuleset().CreateDifficultyCalculator(working).Calculate(mods).StarRating;
                double ortho = new ManiaOrthoRuleset().CreateDifficultyCalculator(working).Calculate(mods).StarRating;

                Assert.That(ortho, Is.EqualTo(mania), $"mods: {string.Join(",", mods.Select(m => m.Acronym))}");
                Assert.That(mania, Is.GreaterThan(0));
            }
        }

        [TestCaseSource(nameof(sources))]
        public void TestPerformanceMatchesMania(string source)
        {
            var working = new TestWorkingBeatmap(PlaytestBeatmaps.Load(source));
            var attributes = new ManiaRuleset().CreateDifficultyCalculator(working).Calculate();
            int objects = working.GetPlayableBeatmap(new ManiaRuleset().RulesetInfo).HitObjects.Count;

            ScoreInfo score(Ruleset ruleset) => new ScoreInfo(working.BeatmapInfo, ruleset.RulesetInfo)
            {
                Statistics = new Dictionary<HitResult, int>
                {
                    [HitResult.Perfect] = objects - 40,
                    [HitResult.Great] = 20,
                    [HitResult.Good] = 10,
                    [HitResult.Ok] = 5,
                    [HitResult.Meh] = 3,
                    [HitResult.Miss] = 2,
                },
                Mods = System.Array.Empty<Mod>(),
            };

            double mania = new ManiaRuleset().CreatePerformanceCalculator()!.Calculate(score(new ManiaRuleset()), attributes).Total;
            double ortho = new ManiaOrthoRuleset().CreatePerformanceCalculator()!.Calculate(score(new ManiaOrthoRuleset()), attributes).Total;

            Assert.That(ortho, Is.EqualTo(mania));
            Assert.That(mania, Is.GreaterThan(0));
        }

        [Test]
        public void TestNeverSubmittable()
        {
            var ruleset = new ManiaOrthoRuleset();

            // lazer only creates score submission tokens for legacy rulesets (see SoloPlayer.CreateTokenRequest).
            Assert.That(ruleset, Is.Not.InstanceOf<ILegacyRuleset>());
            Assert.That(ruleset.RulesetInfo.IsLegacyRuleset(), Is.False);
            Assert.That(ruleset.RulesetInfo.OnlineID, Is.LessThan(0));
        }

        private static List<string> describe(IBeatmap beatmap) => beatmap.HitObjects.Cast<ManiaHitObject>()
                                                                          .Select(h => $"{h.GetType().Name}@{h.StartTime}-{h.GetEndTime()}:c{h.Column}")
                                                                          .Prepend($"columns={((osu.Game.Rulesets.Mania.Beatmaps.ManiaBeatmap)beatmap).TotalColumns}")
                                                                          .ToList();
    }
}
