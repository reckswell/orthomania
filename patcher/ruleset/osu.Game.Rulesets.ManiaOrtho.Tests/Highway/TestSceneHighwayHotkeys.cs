// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// Gameplay hotkeys (SPEC 4): Shift+F3 and Shift+F4 move the wall, F3 and F4 still change mania's scroll speed, and every
    /// key still reaches mania's input as it does in mania.
    /// </summary>
    public partial class TestSceneHighwayHotkeys : PlayerTestScene
    {
        protected override bool Autoplay => false;

        protected override Ruleset CreatePlayerRuleset() => new ManiaOrthoRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => PlaytestBeatmaps.Load("synthetic-4k");

        private DrawableManiaOrthoRuleset drawableRuleset => (DrawableManiaOrthoRuleset)Player.DrawableRuleset;

        private HighwayPlayfieldAdjustmentContainer highway => drawableRuleset.Highway.AsNonNull();

        private ManiaOrthoRulesetConfigManager config => (ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(CreatePlayerRuleset()).AsNonNull();

        private float wall => config.Highway.Get<float>(HighwaySetting.Wall);

        private HighwayHotkeys hotkeys => drawableRuleset.ChildrenOfType<HighwayHotkeys>().Single();

        private double scrollSpeed => config.Get<double>(ManiaRulesetSetting.ScrollSpeed);

        [SetUpSteps]
        public override void SetUpSteps()
        {
            AddStep("Chunithm preset (no wall), wall in percent", () =>
            {
                config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                config.Highway.GetBindable<WallScale>(HighwaySetting.WallScale).SetDefault();
                config.Highway.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime).SetDefault();
            });

            base.SetUpSteps();
        }

        [Test]
        public void TestShiftF4RaisesTheWall()
        {
            AddAssert("no wall", () => wall, () => Is.Zero);

            pressWithShift(Key.F4);
            AddAssert("wall at 5%", () => wall, () => Is.EqualTo(HighwayHotkeys.WALL_STEP).Within(1e-4f));
            AddUntilStep("the highway shows it", () => highway.Wall.IsPresent && highway.Settings.Wall > 0);

            pressWithShift(Key.F4);
            AddAssert("wall at 10%", () => wall, () => Is.EqualTo(2 * HighwayHotkeys.WALL_STEP).Within(1e-4f));

            pressWithShift(Key.F3);
            AddAssert("back to 5%", () => wall, () => Is.EqualTo(HighwayHotkeys.WALL_STEP).Within(1e-4f));
        }

        /// <summary>
        /// With a game's wall scale chosen, the keys move one step of that game's: here Chunithm's field wall.
        /// </summary>
        [Test]
        public void TestKeysStepTheChosenScale()
        {
            AddStep("Chunithm's wall scale", () => config.Highway.SetValue(HighwaySetting.WallScale, WallScale.Chunithm));

            for (int i = 0; i < 5; i++)
                pressWithShift(Key.F4);

            AddAssert("at Chunithm's wall 5", () => wall, () => Is.EqualTo(WallScales.ChunithmWall(5)).Within(1e-4f));
            AddUntilStep("the highway draws it", () => highway.Settings.Wall, () => Is.EqualTo(WallScales.ChunithmWall(5)).Within(1e-4f));

            pressWithShift(Key.F3);
            AddAssert("back to wall 4", () => wall, () => Is.EqualTo(WallScales.ChunithmWall(4)).Within(1e-4f));
        }

        [Test]
        public void TestWallStaysInRange()
        {
            AddStep("wall just above none", () => config.Highway.SetValue(HighwaySetting.Wall, HighwayHotkeys.WALL_STEP / 2));
            pressWithShift(Key.F3);
            AddAssert("stops at none", () => wall, () => Is.Zero);
            pressWithShift(Key.F3);
            AddAssert("never below it", () => wall, () => Is.Zero);

            AddStep("wall just below its most", () => config.Highway.SetValue(HighwaySetting.Wall, HighwayScroll.MAX_WALL - HighwayHotkeys.WALL_STEP / 2));
            pressWithShift(Key.F4);
            AddAssert("stops at its most", () => wall, () => Is.EqualTo(HighwayScroll.MAX_WALL).Within(1e-4f));
            pressWithShift(Key.F4);
            AddAssert("never past it", () => wall, () => Is.EqualTo(HighwayScroll.MAX_WALL).Within(1e-4f));
        }

        [Test]
        public void TestOtherModifiersLeaveTheWall()
        {
            AddStep("press Ctrl+Shift+F4", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.Key(Key.F4);
                InputManager.ReleaseKey(Key.ShiftLeft);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("no wall", () => wall, () => Is.Zero);
        }

        /// <summary>
        /// The wall keys follow osu!'s scroll speed bindings as they change (Alpha 2026-11-04): with "Increase scroll speed" on
        /// F6, Shift+F6 raises the wall and Shift+F4 no longer does.
        /// </summary>
        [Test]
        public void TestKeysFollowTheScrollSpeedBindings()
        {
            AddStep("bind Increase scroll speed to F6", () => Realm.Write(r =>
            {
                var binding = increaseBinding(r);

                if (binding == null)
                    r.Add(new RealmKeyBinding(GlobalAction.IncreaseScrollSpeed, new KeyCombination(InputKey.F6)));
                else
                {
                    storedIncrease = binding.KeyCombinationString;
                    binding.KeyCombination = new KeyCombination(InputKey.F6);
                }

                increaseRebound = true;
            }));
            AddUntilStep("the wall keys follow", () => hotkeys.RaiseKeys.Select(k => k.ToString()), () => Is.EqualTo(new[] { new KeyCombination(InputKey.F6).ToString() }));
            AddAssert("the wall's toast names them", () => drawableRuleset.Toasts.AsNonNull().DescribeWall(0.1f).Shortcut.ToString(), () => Is.EqualTo("Shift+F3 / Shift+F6"));

            pressWithShift(Key.F6);
            AddAssert("Shift+F6 raised the wall", () => wall, () => Is.EqualTo(HighwayHotkeys.WALL_STEP).Within(1e-4f));

            pressWithShift(Key.F4);
            AddAssert("Shift+F4 no longer does", () => wall, () => Is.EqualTo(HighwayHotkeys.WALL_STEP).Within(1e-4f));
        }

        private bool increaseRebound;
        private string? storedIncrease;

        [TearDownSteps]
        public override void TearDownSteps()
        {
            AddStep("restore Increase scroll speed's keys", () =>
            {
                if (!increaseRebound)
                    return;

                Realm.Write(r =>
                {
                    var binding = increaseBinding(r).AsNonNull();

                    if (storedIncrease == null)
                        r.Remove(binding);
                    else
                        binding.KeyCombinationString = storedIncrease;
                });

                increaseRebound = false;
                storedIncrease = null;
            });
            base.TearDownSteps();
        }

        private static RealmKeyBinding? increaseBinding(Realms.Realm realm)
        {
            int increase = (int)GlobalAction.IncreaseScrollSpeed;
            return realm.All<RealmKeyBinding>().FirstOrDefault(b => b.RulesetName == null && b.Variant == null && b.ActionInt == increase);
        }

        [Test]
        public void TestF3AndF4StillChangeTheScrollSpeed()
        {
            double before = 0;

            // osu! takes these only in the first 10 seconds of a play and in breaks (never while paused), so press them right away.

            AddStep("note the speed", () => before = scrollSpeed);
            AddStep("press F4", () => InputManager.Key(Key.F4));
            AddUntilStep("speed up by one", () => scrollSpeed, () => Is.EqualTo(before + 1));
            AddAssert("the wall stays", () => wall, () => Is.Zero);

            AddStep("press F3", () => InputManager.Key(Key.F3));
            AddUntilStep("speed back", () => scrollSpeed, () => Is.EqualTo(before));
        }

        [Test]
        public void TestHotkeysNeverPressAColumn()
        {
            double speed = 0;

            AddStep("note the speed", () => speed = scrollSpeed);
            AddStep("hold Shift+F4", () =>
            {
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.PressKey(Key.F4);
            });
            AddAssert("no column key is down", () => drawableRuleset.PressedActions.AsNonNull().ToArray(), () => Is.Empty);
            AddAssert("the wall moved", () => wall, () => Is.GreaterThan(0));
            AddAssert("the speed did not", () => scrollSpeed, () => Is.EqualTo(speed));
            AddStep("release", () =>
            {
                InputManager.ReleaseKey(Key.F4);
                InputManager.ReleaseKey(Key.ShiftLeft);
            });
        }

        [Test]
        public void TestHotkeysLeaveTheKeysToMania()
        {
            KeyProbe probe = null!;

            AddStep("listen inside mania's input", () => highway.Overlay.Add(probe = new KeyProbe()));
            AddAssert("the listener is inside mania's input manager", () => probe.FindClosestParent<ManiaInputManager>(), () => Is.Not.Null);

            pressWithShift(Key.F4);
            AddAssert("the wall moved", () => wall, () => Is.GreaterThan(0));
            AddAssert("mania still received Shift+F4", () => probe.ShiftF4Presses, () => Is.EqualTo(1));
        }

        [Test]
        public void TestHotkeysWorkWhileWatchingAReplay()
        {
            TestReplayPlayer replay = null!;

            AddStep("watch an autoplay replay", () =>
            {
                if (Stack.CurrentScreen is Player)
                    Stack.Exit();

                var ruleset = CreatePlayerRuleset();
                Ruleset.Value = ruleset.RulesetInfo;
                Beatmap.Value = CreateWorkingBeatmap(CreateBeatmap(ruleset.RulesetInfo));
                SelectedMods.Value = new[] { ruleset.GetAutoplayMod().AsNonNull() };

                LoadScreen(replay = new TestReplayPlayer(false));
            });
            // Shown and running, as the player tests wait for: while the player still fades in, its content is not present
            // and gets no key presses (seen once in check.sh, the press landing in the frame the replay player entered).
            AddUntilStep("replay playing", () => replay.IsLoaded && replay.IsCurrentScreen() && replay.Alpha == 1
                                                  && replay.GameplayClockContainer.IsRunning
                                                  && replay.DrawableRuleset is DrawableManiaOrthoRuleset { Highway: not null });

            // osu! takes Shift itself while watching a replay (holding it fast-forwards), so the wall keys must not wait for
            // Shift's own key event.
            pressWithShift(Key.F4);
            AddAssert("wall at 5%", () => wall, () => Is.EqualTo(HighwayHotkeys.WALL_STEP).Within(1e-4f));
            pressWithShift(Key.F3);
            AddAssert("no wall", () => wall, () => Is.Zero);
        }

        private void pressWithShift(Key key) => AddStep($"press Shift+{key}", () =>
        {
            InputManager.PressKey(Key.ShiftLeft);
            InputManager.Key(key);
            InputManager.ReleaseKey(Key.ShiftLeft);
        });

        /// <summary>
        /// Counts the Shift+F4 presses that reach it, and handles none.
        /// </summary>
        private partial class KeyProbe : Component
        {
            public int ShiftF4Presses { get; private set; }

            protected override bool OnKeyDown(KeyDownEvent e)
            {
                if (e.Key == Key.F4 && e.ShiftPressed)
                    ShiftF4Presses++;

                return false;
            }
        }
    }
}
