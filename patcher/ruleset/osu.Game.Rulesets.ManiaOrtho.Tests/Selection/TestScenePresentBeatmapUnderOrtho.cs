// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Screens.Select;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Selection
{
    /// <summary>
    /// Covers the hook on <c>OsuGame.PresentBeatmap</c> (<c>Hooks/LazerHooks.cs</c>, once a patch to lazer in the osu fork): presenting an osu!mania
    /// beatmap while maniaortho is selected keeps maniaortho, and presenting anything else switches as upstream does.
    /// </summary>
    public partial class TestScenePresentBeatmapUnderOrtho : OsuGameTestScene
    {
        private const string ortho = "maniaortho";

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("disallow converts", () => Game.LocalConfig.SetValue(OsuSetting.ShowConvertedBeatmaps, false));
            AddStep("select maniaortho", () => Game.Ruleset.Value = Game.Dependencies.Get<RulesetStore>().GetRuleset(ortho)!);
        }

        [Test]
        public void TestManiaBeatmapKeepsOrtho()
        {
            var mania = import(1, new ManiaRuleset().RulesetInfo, new ManiaRuleset().RulesetInfo);

            present(mania, null, expectedDifficulty: 1, expectedRuleset: ortho);
            present(mania, b => b.OnlineID == onlineID(1, 2), expectedDifficulty: 2, expectedRuleset: ortho);
        }

        [Test]
        public void TestMixedSetPicksTheManiaDifficulty()
        {
            // The osu! difficulty comes first, so a fallback that only matched maniaortho's own ruleset would pick it.
            var mixed = import(2, new OsuRuleset().RulesetInfo, new ManiaRuleset().RulesetInfo);

            present(mixed, null, expectedDifficulty: 2, expectedRuleset: ortho);
        }

        [Test]
        public void TestOsuBeatmapSwitchesAway()
        {
            var osu = import(3, new OsuRuleset().RulesetInfo, new OsuRuleset().RulesetInfo);

            present(osu, null, expectedDifficulty: 1, expectedRuleset: "osu");
        }

        private static int onlineID(int set, int difficulty) => set * 1024 + difficulty;

        private Func<BeatmapSetInfo> import(int set, RulesetInfo first, RulesetInfo second)
        {
            BeatmapSetInfo? imported = null;

            AddStep($"import set {set}", () =>
            {
                var metadata = new BeatmapMetadata
                {
                    Artist = "SomeArtist",
                    Author = { Username = "SomeAuthor" },
                    Title = $"import {set}"
                };

                imported = Game.BeatmapManager.Import(new BeatmapSetInfo
                {
                    Hash = Guid.NewGuid().ToString(),
                    OnlineID = set * 1024,
                    Beatmaps =
                    {
                        new BeatmapInfo
                        {
                            OnlineID = onlineID(set, 1),
                            Metadata = metadata,
                            Difficulty = new BeatmapDifficulty(),
                            Ruleset = first,
                        },
                        new BeatmapInfo
                        {
                            OnlineID = onlineID(set, 2),
                            Metadata = metadata,
                            Difficulty = new BeatmapDifficulty(),
                            Ruleset = second,
                        },
                    }
                })?.Value;
            });

            AddAssert($"import {set} succeeded", () => imported != null);

            return () => imported!;
        }

        private void present(Func<BeatmapSetInfo> getSet, Predicate<BeatmapInfo>? difficulty, int expectedDifficulty, string expectedRuleset)
        {
            AddStep("present beatmap", () => Game.PresentBeatmap(getSet(), difficulty));

            AddUntilStep("wait for song select", () => Game.ScreenStack.CurrentScreen is SoloSongSelect songSelect && songSelect.CarouselItemsPresented);
            AddUntilStep("expected difficulty selected", () => Game.Beatmap.Value.BeatmapInfo.OnlineID,
                () => Is.EqualTo(onlineID(getSet().OnlineID / 1024, expectedDifficulty)));
            AddAssert($"ruleset is {expectedRuleset}", () => Game.Ruleset.Value.ShortName, () => Is.EqualTo(expectedRuleset));
            AddAssert("set has both difficulties", () => getSet().Beatmaps.Count, () => Is.EqualTo(2));
        }
    }
}
