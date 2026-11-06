// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Configuration
{
    /// <summary>
    /// osu!'s and osu!mania's own settings that act inside gameplay work in maniaortho as they do in osu!mania (Alpha
    /// 2026-11-04; the full list, and how each was checked, is the osu fork's docs/ortho/SETTINGS-PARITY.md). Each case runs once per
    /// ruleset, so a difference shows as one of the pair failing.
    /// </summary>
    public partial class TestSceneSettingsParity : PlayerTestScene
    {
        private Ruleset ruleset = new ManiaOrthoRuleset();

        protected override bool Autoplay => false;

        protected override Ruleset CreatePlayerRuleset() => ruleset;

        protected override IBeatmap CreateBeatmap(RulesetInfo rulesetInfo) => PlaytestBeatmaps.Load("synthetic-4k");

        /// <summary>
        /// "Touch overlay" (osu!mania's setting; maniaortho's settings show it too): a button per column along the bottom of
        /// the screen, and a touch on one presses its column, on the highway lighting its lane.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TestTouchOverlay(bool ortho)
        {
            choose(ortho);
            AddStep("touch overlay on", () => rulesetConfig.SetValue(ManiaRulesetSetting.TouchOverlay, true));
            play(ortho);

            AddAssert("a button per column", () => receptors.Length, () => Is.EqualTo(4));

            AddStep("touch the third", () => InputManager.BeginTouch(new Touch(TouchSource.Touch1, receptors[2].ScreenSpaceDrawQuad.Centre)));
            AddAssert("the third column is pressed", () => keyBindingContainer.PressedActions, () => Is.EquivalentTo(new[] { ManiaAction.Key3 }));

            if (ortho)
                AddUntilStep("its lane lights on the highway", () => highway.Track.BeamAlphas[2], () => Is.GreaterThan(0));

            AddStep("lift", () => InputManager.EndTouch(new Touch(TouchSource.Touch1, receptors[2].ScreenSpaceDrawQuad.Centre)));
            AddAssert("released", () => keyBindingContainer.PressedActions, () => Is.Empty);

            AddStep("touch overlay off", () => rulesetConfig.SetValue(ManiaRulesetSetting.TouchOverlay, false));
            AddUntilStep("gone", () => receptors, () => Is.Empty);
        }

        /// <summary>
        /// The key overlay (osu!'s "Always show key overlay") counts the same keys, one per column in order, and a key press
        /// the same way.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TestKeyOverlayCounts(bool ortho)
        {
            choose(ortho);
            AddStep("key overlay on, osu!mania's 4K on D F J K", () =>
            {
                LocalConfig.SetValue(OsuSetting.KeyOverlay, true);
                Realm.Write(r =>
                {
                    r.RemoveRange(r.All<RealmKeyBinding>().Where(b => b.RulesetName == ManiaRuleset.SHORT_NAME && b.Variant == 4));

                    foreach (var (action, key) in new[] { (ManiaAction.Key1, InputKey.D), (ManiaAction.Key2, InputKey.F), (ManiaAction.Key3, InputKey.J), (ManiaAction.Key4, InputKey.K) })
                        r.Add(new RealmKeyBinding(action, new KeyCombination(key), ManiaRuleset.SHORT_NAME, 4));
                });
            });
            play(ortho);

            AddAssert("a counter per column, in order", () => Player.HUDOverlay.InputCountController.Triggers.Select(t => t.Name),
                () => Is.EqualTo(new[] { "B1", "B2", "B3", "B4" }));

            AddStep("press K twice", () =>
            {
                InputManager.Key(Key.K);
                InputManager.Key(Key.K);
            });
            AddUntilStep("the fourth counts two, the rest none", () => Player.HUDOverlay.InputCountController.Triggers.Select(t => t.ActivationCount.Value),
                () => Is.EqualTo(new[] { 0, 0, 0, 2 }));
        }

        private void choose(bool ortho) => AddStep(ortho ? "maniaortho" : "osu!mania", () => ruleset = ortho ? new ManiaOrthoRuleset() : new ManiaRuleset());

        private void play(bool ortho)
        {
            AddStep("load the player", () => LoadPlayer());
            AddUntilStep("player running", () => Player.IsLoaded && Player.Alpha == 1 && Player.GameplayClockContainer.IsRunning
                                                  && (!ortho || Player.DrawableRuleset is DrawableManiaOrthoRuleset { Highway: not null }));
        }

        private ManiaRulesetConfigManager rulesetConfig => (ManiaRulesetConfigManager)RulesetConfigs.GetConfigFor(ruleset).AsNonNull();

        private ManiaTouchInputArea.ColumnInputReceptor[] receptors => Player.DrawableRuleset.ChildrenOfType<ManiaTouchInputArea.ColumnInputReceptor>().ToArray();

        private KeyBindingContainer<ManiaAction> keyBindingContainer => Player.DrawableRuleset.ChildrenOfType<ManiaInputManager>().First().KeyBindingContainer;

        private HighwayPlayfieldAdjustmentContainer highway => ((DrawableManiaOrthoRuleset)Player.DrawableRuleset).Highway.AsNonNull();
    }
}
