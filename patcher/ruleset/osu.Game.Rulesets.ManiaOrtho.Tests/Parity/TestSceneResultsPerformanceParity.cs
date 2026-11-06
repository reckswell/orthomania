// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Parity
{
    /// <summary>
    /// pp on the results screen. <see cref="PerformanceStatistic"/> rates a score through the game's difficulty cache with the
    /// score's ruleset, so under maniaortho the cache converts and rates the imported beatmap with this ruleset. The number
    /// must be osu!mania's for the same score (invariant 1), with mods that change the rate, the difficulty or the pp
    /// multiplier, and with Flashlight, which this ruleset replaces.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneResultsPerformanceParity : OsuTestScene
    {
        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        private BeatmapInfo beatmap = null!;

        private ScoreInfo maniaScore = null!;
        private ScoreInfo orthoScore = null!;

        private PerformanceStatistic maniaStatistic = null!;
        private PerformanceStatistic orthoStatistic = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("import the 4K test beatmap", () =>
            {
                var imported = beatmaps.Import(new ImportTask(PlaytestBeatmaps.CreateReal4KArchive(), "maniaortho real 4k.osz")).GetResultSafely().AsNonNull();
                beatmap = imported.PerformRead(set => set.Beatmaps.Single().Detach());
            });
        }

        [TestCase("")]
        [TestCase("DT")]
        [TestCase("HT")]
        [TestCase("HR")]
        [TestCase("NF")]
        [TestCase("FL")]
        public void TestResultsPerformanceMatchesMania(string mod)
        {
            string[] mods = mod.Length > 0 ? new[] { mod } : Array.Empty<string>();
            int expected = 0;

            AddStep("rate the score with mania's calculators", () => expected = expectedPerformance(mods));

            AddStep("show both scores' pp", () =>
            {
                maniaScore = createScore(new ManiaRuleset(), mods);
                orthoScore = createScore(new ManiaOrthoRuleset(), mods);

                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Children = new Drawable[]
                    {
                        maniaStatistic = new PerformanceStatistic(maniaScore),
                        orthoStatistic = new PerformanceStatistic(orthoScore),
                    }
                };
            });

            AddStep("appear", () =>
            {
                maniaStatistic.Appear();
                orthoStatistic.Appear();
            });

            AddAssert("expected pp is above 0", () => expected, () => Is.GreaterThan(0));
            AddUntilStep("mania rated the score", () => shownPerformance(maniaStatistic), () => Is.EqualTo(expected));
            AddUntilStep("maniaortho rated it the same", () => shownPerformance(orthoStatistic), () => Is.EqualTo(expected));
            AddAssert("both scores have the mods", () => new[] { maniaScore, orthoScore }.Select(s => s.Mods.Select(m => m.Acronym)), () => Has.All.EquivalentTo(mods));
            AddAssert("the scores are each ruleset's", () => new[] { maniaScore.Ruleset.ShortName, orthoScore.Ruleset.ShortName },
                () => Is.EqualTo(new[] { "mania", ManiaOrthoRuleset.SHORT_NAME }));
        }

        private static int shownPerformance(PerformanceStatistic statistic) => statistic.ChildrenOfType<RollingCounter<int>>().Single().Current.Value;

        /// <summary>
        /// osu!mania's pp for the score, from its own calculators on the decoded file.
        /// </summary>
        private int expectedPerformance(string[] mods)
        {
            var mania = new ManiaRuleset();
            var score = createScore(mania, mods);
            var attributes = mania.CreateDifficultyCalculator(new TestWorkingBeatmap(PlaytestBeatmaps.Load(PlaytestBeatmaps.REAL_4K))).Calculate(score.Mods);

            return (int)Math.Round(mania.CreatePerformanceCalculator()!.Calculate(score, attributes).Total, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// A score with a mix of judgements, as a 98% play would have.
        /// </summary>
        private ScoreInfo createScore(Ruleset ruleset, string[] mods)
        {
            int objects = beatmap.TotalObjectCount;

            return new ScoreInfo(beatmap, ruleset.RulesetInfo)
            {
                Statistics = new Dictionary<HitResult, int>
                {
                    [HitResult.Perfect] = objects - 80,
                    [HitResult.Great] = 40,
                    [HitResult.Good] = 20,
                    [HitResult.Ok] = 10,
                    [HitResult.Meh] = 5,
                    [HitResult.Miss] = 5,
                },
                MaxCombo = objects / 2,
                Accuracy = 0.98,
                Rank = ScoreRank.A,
                Mods = mods.Select(m => ruleset.CreateModFromAcronym(m).AsNonNull()).ToArray(),
            };
        }
    }
}
