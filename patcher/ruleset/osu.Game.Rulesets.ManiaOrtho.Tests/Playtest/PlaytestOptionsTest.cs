// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Playtest
{
    /// <summary>
    /// The harness's scene always adds No Fail (the humanised replay misses a few notes); a second one fails the player's load.
    /// </summary>
    [TestFixture]
    public class PlaytestOptionsTest
    {
        [TestCase(null)]
        [TestCase(4)]
        public void TestNoFailIsLeftToTheScene(int? humanisedSeed)
        {
            var mods = new PlaytestOptions { HumanisedSeed = humanisedSeed, Mods = new[] { "HD", "nf" } }.CreateMods(new ManiaRuleset());

            Assert.That(mods.Select(m => m.Acronym), Is.EquivalentTo(new[] { "HD" }));
        }

        /// <summary>
        /// <c>--game-set</c> sets osu!'s and osu!mania's own settings, whatever their type, from text as osu! reads them.
        /// </summary>
        [Test]
        public void TestGameValues()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestGameValues));
            using var game = new OsuConfigManager(storage);
            var ruleset = new ManiaRulesetConfigManager(null, new ManiaRuleset().RulesetInfo);

            new PlaytestOptions
            {
                GameValues = new[]
                {
                    "KeyOverlay=true", "hudvisibilitymode=Never", "DimLevel=0.25", "Scaling=Gameplay", "TimingBasedNoteColouring=true",
                    "ScrollDirection=Up",
                },
            }.ApplyGameValues(game, ruleset);

            Assert.That(game.Get<bool>(OsuSetting.KeyOverlay), Is.True);
            Assert.That(game.Get<HUDVisibilityMode>(OsuSetting.HUDVisibilityMode), Is.EqualTo(HUDVisibilityMode.Never));
            Assert.That(game.Get<double>(OsuSetting.DimLevel), Is.EqualTo(0.25));
            Assert.That(game.Get<ScalingMode>(OsuSetting.Scaling), Is.EqualTo(ScalingMode.Gameplay));
            Assert.That(ruleset.Get<bool>(ManiaRulesetSetting.TimingBasedNoteColouring), Is.True);
            Assert.That(ruleset.Get<ManiaScrollingDirection>(ManiaRulesetSetting.ScrollDirection), Is.EqualTo(ManiaScrollingDirection.Up));

            PlaytestOptions.ResetRulesetValues(ruleset);
            Assert.That(ruleset.Get<bool>(ManiaRulesetSetting.TimingBasedNoteColouring), Is.False, "reset for the next run");
            Assert.That(ruleset.Get<ManiaScrollingDirection>(ManiaRulesetSetting.ScrollDirection), Is.EqualTo(ManiaScrollingDirection.Down), "reset for the next run");
        }

        /// <summary>
        /// The same list serves both rulesets: maniaortho's camera fixes the scroll direction, so setting it leaves it be.
        /// </summary>
        [Test]
        public void TestGameValuesLeaveFixedSettings()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestGameValuesLeaveFixedSettings));
            using var game = new OsuConfigManager(storage);
            var ruleset = new ManiaOrthoRulesetConfigManager(null, new ManiaOrthoRuleset().RulesetInfo);

            new PlaytestOptions { GameValues = new[] { "ScrollDirection=Up", "TouchOverlay=true" } }.ApplyGameValues(game, ruleset);

            Assert.That(ruleset.Get<ManiaScrollingDirection>(ManiaRulesetSetting.ScrollDirection), Is.EqualTo(ManiaScrollingDirection.Down));
            Assert.That(ruleset.Get<bool>(ManiaRulesetSetting.TouchOverlay), Is.True);

            PlaytestOptions.ResetRulesetValues(ruleset);
            Assert.That(ruleset.Get<bool>(ManiaRulesetSetting.TouchOverlay), Is.False);
        }

        [TestCase("NoSuchSetting=1")]
        [TestCase("KeyOverlay")]
        public void TestUnknownGameValuesFailEarly(string assignment)
        {
            Environment.SetEnvironmentVariable(PlaytestOptions.ENV_GAME_SET, assignment);

            try
            {
                Assert.Throws<ArgumentException>(() => PlaytestOptions.FromEnvironment());
            }
            finally
            {
                Environment.SetEnvironmentVariable(PlaytestOptions.ENV_GAME_SET, null);
            }
        }

        [Test]
        public void TestModsAsGiven()
        {
            var mods = new PlaytestOptions { Mods = new[] { "HD", "FI" } }.CreateMods(new ManiaRuleset());

            Assert.That(mods.Select(m => m.Acronym), Is.EquivalentTo(new[] { "HD", "FI" }));
        }
    }
}
