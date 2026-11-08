// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Tests.Visual;
using osuTK.Input;
using Realms;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Configuration
{
    /// <summary>
    /// maniaortho plays with the keys bound for osu!mania (Alpha 2026-11-04: changing keys matches the gameplay field), and
    /// the maniaortho section of osu!'s key binding settings shows and changes the same keys (<see cref="KeyBindingMirror"/>),
    /// unless "Share osu!mania's keys" is off (Alpha 2026-11-07: an option), when it plays with its own.
    /// </summary>
    public partial class TestSceneKeyBindings : PlayerTestScene
    {
        /// <summary>
        /// One 4K stage, as synthetic-4k plays.
        /// </summary>
        private const int variant = 4;

        private Ruleset ruleset = new ManiaOrthoRuleset();

        private KeyBindingMirror? mirror;

        protected override bool Autoplay => false;

        protected override Ruleset CreatePlayerRuleset() => ruleset;

        protected override IBeatmap CreateBeatmap(RulesetInfo rulesetInfo) => PlaytestBeatmaps.Load("synthetic-4k");

        [SetUpSteps]
        public void ShareKeys() => AddStep("share osu!mania's keys", () => shareManiaKeys.SetDefault());

        /// <summary>
        /// A key rebound for osu!mania presses the same column in both rulesets, and on the highway lights that lane. Keys
        /// maniaortho kept from before are not used: there is one set of keys.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TestRebindingPressesTheSameLane(bool ortho)
        {
            AddStep("osu!mania's 4K on Q W O P, maniaortho's old ones on A S K L", () => Realm.Write(r =>
            {
                setKeys(r, ManiaRuleset.SHORT_NAME, InputKey.Q, InputKey.W, InputKey.O, InputKey.P);
                setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.A, InputKey.S, InputKey.K, InputKey.L);
            }));

            // Written before the player loads, so its keys are read from them as it starts.
            AddStep(ortho ? "play maniaortho" : "play osu!mania", () =>
            {
                ruleset = ortho ? new ManiaOrthoRuleset() : new ManiaRuleset();
                LoadPlayer();
            });
            AddUntilStep("player running", () => Player.IsLoaded && Player.Alpha == 1 && Player.GameplayClockContainer.IsRunning);

            AddStep("hold P", () => InputManager.PressKey(Key.P));
            AddAssert("the fourth column is pressed", () => keyBindingContainer.PressedActions, () => Is.EquivalentTo(new[] { ManiaAction.Key4 }));

            if (ortho)
                AddUntilStep("its lane lights on the highway", () => highway.Track.BeamAlphas[3], () => Is.GreaterThan(0));

            AddStep("release P", () => InputManager.ReleaseKey(Key.P));

            AddStep("hold L", () => InputManager.PressKey(Key.L));
            AddAssert("maniaortho's old key presses nothing", () => keyBindingContainer.PressedActions, () => Is.Empty);
            AddStep("release L", () => InputManager.ReleaseKey(Key.L));
        }

        /// <summary>
        /// Keys set for maniaortho while osu!mania's were left at their defaults (as when maniaortho had keys of its own) move
        /// to osu!mania as maniaortho starts, without the settings ever opening: the player keeps the keys they set.
        /// </summary>
        [Test]
        public void TestKeysSetForManiaOrthoMoveToMania()
        {
            AddStep("osu!mania's 4K at its defaults, maniaortho's on A S K L", () => Realm.Write(r =>
            {
                setDefaultKeys(r, ManiaRuleset.SHORT_NAME);
                setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.A, InputKey.S, InputKey.K, InputKey.L);
            }));
            AddStep("play maniaortho", () =>
            {
                ruleset = new ManiaOrthoRuleset();
                LoadPlayer();
            });
            AddUntilStep("player running", () => Player.IsLoaded && Player.Alpha == 1 && Player.GameplayClockContainer.IsRunning);

            AddAssert("osu!mania has them", () => keysOf(ManiaRuleset.SHORT_NAME), () => Is.EqualTo("1:A 2:S 3:K 4:L"));
            AddStep("hold L", () => InputManager.PressKey(Key.L));
            AddAssert("the fourth column is pressed", () => keyBindingContainer.PressedActions, () => Is.EquivalentTo(new[] { ManiaAction.Key4 }));
            AddStep("release L", () => InputManager.ReleaseKey(Key.L));
        }

        /// <summary>
        /// Which keys win when the two sides first differ: the side changed from osu!mania's defaults, and osu!mania's when both
        /// were.
        /// </summary>
        [Test]
        public void TestMergeKeepsTheChangedSide()
        {
            AddStep("osu!mania's at defaults, maniaortho's on A S K L", () => Realm.Write(r =>
            {
                setDefaultKeys(r, ManiaRuleset.SHORT_NAME);
                setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.A, InputKey.S, InputKey.K, InputKey.L);
                KeyBindingMirror.Merge(r);
            }));
            AddAssert("both have maniaortho's", () => (keysOf(ManiaRuleset.SHORT_NAME), keysOf(ManiaOrthoRuleset.SHORT_NAME)),
                () => Is.EqualTo(("1:A 2:S 3:K 4:L", "1:A 2:S 3:K 4:L")));

            AddStep("osu!mania's on Q W O P, maniaortho's at defaults", () => Realm.Write(r =>
            {
                setKeys(r, ManiaRuleset.SHORT_NAME, InputKey.Q, InputKey.W, InputKey.O, InputKey.P);
                setDefaultKeys(r, ManiaOrthoRuleset.SHORT_NAME);
                KeyBindingMirror.Merge(r);
            }));
            AddAssert("both have osu!mania's", () => (keysOf(ManiaRuleset.SHORT_NAME), keysOf(ManiaOrthoRuleset.SHORT_NAME)),
                () => Is.EqualTo(("1:Q 2:W 3:O 4:P", "1:Q 2:W 3:O 4:P")));

            AddStep("osu!mania's on Q W O P, maniaortho's on A S K L", () => Realm.Write(r =>
            {
                setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.A, InputKey.S, InputKey.K, InputKey.L);
                KeyBindingMirror.Merge(r);
            }));
            AddAssert("both have osu!mania's", () => (keysOf(ManiaRuleset.SHORT_NAME), keysOf(ManiaOrthoRuleset.SHORT_NAME)),
                () => Is.EqualTo(("1:Q 2:W 3:O 4:P", "1:Q 2:W 3:O 4:P")));
        }

        /// <summary>
        /// With sharing off, maniaortho plays with its own keys and leaves osu!mania's alone; turned on in play, the keys
        /// switch to osu!mania's at once.
        /// </summary>
        [Test]
        public void TestUnsharedKeysAreManiaOrthosOwn()
        {
            AddStep("osu!mania's 4K on Q W O P, maniaortho's on A S K L", () => Realm.Write(r =>
            {
                setKeys(r, ManiaRuleset.SHORT_NAME, InputKey.Q, InputKey.W, InputKey.O, InputKey.P);
                setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.A, InputKey.S, InputKey.K, InputKey.L);
            }));
            AddStep("stop sharing", () => shareManiaKeys.Value = false);
            AddStep("play maniaortho", () =>
            {
                ruleset = new ManiaOrthoRuleset();
                LoadPlayer();
            });
            AddUntilStep("player running", () => Player.IsLoaded && Player.Alpha == 1 && Player.GameplayClockContainer.IsRunning);

            AddAssert("nothing merged", () => (keysOf(ManiaRuleset.SHORT_NAME), keysOf(ManiaOrthoRuleset.SHORT_NAME)),
                () => Is.EqualTo(("1:Q 2:W 3:O 4:P", "1:A 2:S 3:K 4:L")));

            AddStep("hold L", () => InputManager.PressKey(Key.L));
            AddAssert("maniaortho's key presses the fourth column", () => keyBindingContainer.PressedActions, () => Is.EquivalentTo(new[] { ManiaAction.Key4 }));
            AddUntilStep("its lane lights on the highway", () => highway.Track.BeamAlphas[3], () => Is.GreaterThan(0));
            AddStep("release L", () => InputManager.ReleaseKey(Key.L));

            AddStep("hold P", () => InputManager.PressKey(Key.P));
            AddAssert("osu!mania's key presses nothing", () => keyBindingContainer.PressedActions, () => Is.Empty);
            AddStep("release P", () => InputManager.ReleaseKey(Key.P));

            AddStep("share again", () => shareManiaKeys.Value = true);
            AddStep("hold P", () => InputManager.PressKey(Key.P));
            AddAssert("osu!mania's key presses the fourth column", () => keyBindingContainer.PressedActions, () => Is.EquivalentTo(new[] { ManiaAction.Key4 }));
            AddStep("release P", () => InputManager.ReleaseKey(Key.P));
        }

        /// <summary>
        /// A copy limited to some columns leaves the rest as they are, so a column changed on the other side meanwhile is not
        /// carried back.
        /// </summary>
        [Test]
        public void TestCopyLeavesOtherColumns()
        {
            int key1 = (int)ManiaAction.Key1;

            AddStep("osu!mania's on Q W J K, maniaortho's on D F J K", () => Realm.Write(r =>
            {
                setKeys(r, ManiaRuleset.SHORT_NAME, InputKey.Q, InputKey.W, InputKey.J, InputKey.K);
                setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.D, InputKey.F, InputKey.J, InputKey.K);
            }));
            AddAssert("copying the first column changes it", () => Realm.Write(r =>
                KeyBindingMirror.Copy(r, ManiaRuleset.SHORT_NAME, ManiaOrthoRuleset.SHORT_NAME, slot => slot.Variant == variant && slot.Action == key1)));
            AddAssert("only the first column was copied", () => keysOf(ManiaOrthoRuleset.SHORT_NAME), () => Is.EqualTo("1:Q 2:F 3:J 4:K"));
        }

        /// <summary>
        /// The settings' maniaortho section takes osu!mania's keys when the mirror starts, then follows a change on either
        /// side, a second key on an action included.
        /// </summary>
        [Test]
        public void TestSettingsSectionMirrorsMania()
        {
            AddStep("osu!mania's 4K on D F J K, maniaortho's on A S K L", () => Realm.Write(r =>
            {
                setKeys(r, ManiaRuleset.SHORT_NAME, InputKey.D, InputKey.F, InputKey.J, InputKey.K);
                setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.A, InputKey.S, InputKey.K, InputKey.L);
            }));

            AddStep("start the mirror", () => mirror = new KeyBindingMirror(Realm));
            AddUntilStep("maniaortho shows osu!mania's keys", () => keysOf(ManiaOrthoRuleset.SHORT_NAME), () => Is.EqualTo("1:D 2:F 3:J 4:K"));
            AddAssert("osu!mania's kept", () => keysOf(ManiaRuleset.SHORT_NAME), () => Is.EqualTo("1:D 2:F 3:J 4:K"));

            AddStep("change osu!mania's first key to Q", () => Realm.Write(r => bindingFor(r, ManiaRuleset.SHORT_NAME, ManiaAction.Key1).KeyCombination = new KeyCombination(InputKey.Q)));
            AddUntilStep("maniaortho follows", () => keysOf(ManiaOrthoRuleset.SHORT_NAME), () => Is.EqualTo("1:Q 2:F 3:J 4:K"));

            AddStep("change maniaortho's second key to W", () => Realm.Write(r => bindingFor(r, ManiaOrthoRuleset.SHORT_NAME, ManiaAction.Key2).KeyCombination = new KeyCombination(InputKey.W)));
            AddUntilStep("osu!mania follows", () => keysOf(ManiaRuleset.SHORT_NAME), () => Is.EqualTo("1:Q 2:W 3:J 4:K"));

            AddStep("add Space as osu!mania's second key for the third column", () => Realm.Write(r =>
                r.Add(new RealmKeyBinding(ManiaAction.Key3, new KeyCombination(InputKey.Space), ManiaRuleset.SHORT_NAME, variant))));
            AddUntilStep("maniaortho has it after J", () => keysOf(ManiaOrthoRuleset.SHORT_NAME), () => Is.EqualTo("1:Q 2:W 3:J 3:Space 4:K"));

            AddAssert("both sides equal, nothing left to copy", () => Realm.Write(r =>
                KeyBindingMirror.Copy(r, ManiaOrthoRuleset.SHORT_NAME, ManiaRuleset.SHORT_NAME)), () => Is.False);
        }

        [TearDownSteps]
        public override void TearDownSteps()
        {
            AddStep("stop the mirror", () =>
            {
                mirror?.Dispose();
                mirror = null;
            });
            base.TearDownSteps();
        }

        private Bindable<bool> shareManiaKeys
            => ((ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(new ManiaOrthoRuleset()).AsNonNull()).Highway.GetBindable<bool>(HighwaySetting.ShareManiaKeys);

        private KeyBindingContainer<ManiaAction> keyBindingContainer => Player.DrawableRuleset.ChildrenOfType<ManiaInputManager>().First().KeyBindingContainer;

        private HighwayPlayfieldAdjustmentContainer highway => ((DrawableManiaOrthoRuleset)Player.DrawableRuleset).Highway!;

        /// <summary>
        /// Replaces a ruleset's 4K keys, one per column in order.
        /// </summary>
        private static void setKeys(Realm realm, string rulesetName, params InputKey[] keys)
        {
            realm.RemoveRange(realm.All<RealmKeyBinding>().Where(b => b.RulesetName == rulesetName && b.Variant == variant));

            for (int i = 0; i < keys.Length; i++)
                realm.Add(new RealmKeyBinding(ManiaAction.Key1 + i, new KeyCombination(keys[i]), rulesetName, variant));
        }

        /// <summary>
        /// Puts a ruleset's 4K keys back to osu!mania's defaults.
        /// </summary>
        private static void setDefaultKeys(Realm realm, string rulesetName)
        {
            realm.RemoveRange(realm.All<RealmKeyBinding>().Where(b => b.RulesetName == rulesetName && b.Variant == variant));

            foreach (var binding in new ManiaRuleset().GetDefaultKeyBindings(variant))
                realm.Add(new RealmKeyBinding(binding.Action, binding.KeyCombination, rulesetName, variant));
        }

        private static RealmKeyBinding bindingFor(Realm realm, string rulesetName, ManiaAction action)
        {
            int actionInt = (int)action;
            return realm.All<RealmKeyBinding>().First(b => b.RulesetName == rulesetName && b.Variant == variant && b.ActionInt == actionInt);
        }

        /// <summary>
        /// A ruleset's 4K keys as "column:key", by column, each column's keys in their order.
        /// </summary>
        private string keysOf(string rulesetName) => Realm.Run(r => string.Join(' ', r.All<RealmKeyBinding>()
                                                                                       .Where(b => b.RulesetName == rulesetName && b.Variant == variant)
                                                                                       .AsEnumerable()
                                                                                       .GroupBy(b => b.ActionInt)
                                                                                       .OrderBy(g => g.Key)
                                                                                       .SelectMany(g => g.Select(b => $"{g.Key - (int)ManiaAction.Key1 + 1}:{b.KeyCombination.Keys.Single()}"))));
    }
}
