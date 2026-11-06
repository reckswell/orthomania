// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Database;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.OSD;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// The visible time the speed and wall toasts show (SPEC 3 and 4; research/topic-covers.md C3) is the time a note
    /// really takes from the wall, or the far edge, to the judgement line, measured from where the highway draws it.
    /// </summary>
    public partial class TestSceneVisibleTime : PlayerTestScene
    {
        /// <summary>
        /// When the clock is stopped for a measurement: well into the chart, after its first notes.
        /// </summary>
        private const double measure_at = 4000;

        [Cached]
        private readonly OnScreenDisplay onScreenDisplay = new OnScreenDisplay();

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        protected override bool HasCustomSteps => true;

        // With a replay attached (autoplay's), the playfield follows seeks of a stopped clock.
        protected override bool Autoplay => true;

        protected override Ruleset CreatePlayerRuleset() => new ManiaOrthoRuleset();

        /// <summary>
        /// A 4K chart with one timing point and no scroll speed changes, so notes move at a constant speed.
        /// </summary>
        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new ManiaBeatmap(new StageDefinition(4))
            {
                BeatmapInfo = new BeatmapInfo
                {
                    Ruleset = new ManiaRuleset().RulesetInfo,
                    Difficulty = new BeatmapDifficulty { CircleSize = 4 },
                },
            };

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            for (double time = 1500; time < 40000; time += 125)
                beatmap.HitObjects.Add(new Note { StartTime = time, Column = (int)(time / 125) % 4 });

            return beatmap;
        }

        private DrawableManiaOrthoRuleset drawableRuleset => (DrawableManiaOrthoRuleset)Player.DrawableRuleset;

        private HighwayPlayfieldAdjustmentContainer highway => drawableRuleset.Highway.AsNonNull();

        private HighwayToasts toasts => drawableRuleset.Toasts.AsNonNull();

        private ManiaOrthoRulesetConfigManager rulesetConfig => (ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(CreatePlayerRuleset()).AsNonNull();

        private HighwayConfigManager highwayConfig => rulesetConfig.Highway;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Add(onScreenDisplay);
        }

        [TearDownSteps]
        public override void TearDownSteps()
        {
            AddStep("default skin and highway", () =>
            {
                skins.CurrentSkinInfo.SetDefault();
                rulesetConfig.GetBindable<double>(ManiaRulesetSetting.ScrollSpeed).SetDefault();
                highwayConfig.GetBindable<HighwayNoteStyle>(HighwaySetting.NoteStyle).SetDefault();
                highwayConfig.GetBindable<WallScale>(HighwaySetting.WallScale).SetDefault();
                highwayConfig.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime).SetDefault();
                highwayConfig.GetBindable<HighwayPreset>(HighwaySetting.Preset).SetDefault();
            });

            base.TearDownSteps();
        }

        /// <summary>
        /// Both toasts show the time a note takes from coming into view to the line, in real time, within 1 ms: with the
        /// default skin's hit position (+2.3% on mania's label) and the classic skin's, rate mods (whose time range only
        /// differs where mania caps it), the wall shortening it when the speed is kept, and none of the wall under the flat
        /// camera, which draws mania's own stage.
        /// </summary>
        [TestCase(HighwayCameraMode.Perspective, 0f, true, false, "", 29.0)]
        [TestCase(HighwayCameraMode.Perspective, 0.4f, true, false, "DT", 29.0)]
        [TestCase(HighwayCameraMode.Perspective, 0.4f, false, false, "", 29.0)]
        [TestCase(HighwayCameraMode.ParallelTilt, 0.4f, false, true, "HT", 20.0)]
        [TestCase(HighwayCameraMode.Flat, 0.4f, false, true, "", 29.0)]
        [TestCase(HighwayCameraMode.Perspective, 0f, true, false, "DT", 1.0)] // mania caps its time range: 7657 ms, not 11749
        public void TestToastsShowTheMeasuredVisibleTime(HighwayCameraMode camera, float wall, bool keepVisibleTime, bool classicSkin, string mod, double speed)
        {
            double rate = mod == "DT" ? 1.5 : mod == "HT" ? 0.75 : 1;

            setUp(camera, wall, keepVisibleTime, classicSkin, speed);
            AddStep("load the chart", () => LoadPlayer(mod == "DT" ? new Mod[] { new ManiaModDoubleTime() } : mod == "HT" ? new Mod[] { new ManiaModHalfTime() } : Array.Empty<Mod>()));
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            stopAt(measure_at);

            AddAssert("the play runs at the mod's rate", () => Player.GameplayClockContainer.AdjustmentsFromMods.AggregateTempo.Value
                                                              * Player.GameplayClockContainer.AdjustmentsFromMods.AggregateFrequency.Value,
                () => Is.EqualTo(rate).Within(1e-9));

            AddAssert("the wall toast shows it", () => shownMilliseconds(toasts.DescribeWall(wall).Value.ToString(), @"(\d+) ms visible"),
                () => Is.EqualTo(measuredVisibleTime(camera == HighwayCameraMode.Flat) / rate).Within(1));
            AddAssert("the speed toast shows it", () => shownMilliseconds(toasts.DescribeScrollSpeed(speed).Value.ToString(), @"^(\d+)ms"),
                () => Is.EqualTo(measuredVisibleTime(camera == HighwayCameraMode.Flat) / rate).Within(1));
            AddAssert("it is the play's current visible time", () => toasts.CurrentVisibleTime,
                () => Is.EqualTo(measuredVisibleTime(camera == HighwayCameraMode.Flat) / rate).Within(1e-3));

            string cover = camera == HighwayCameraMode.Flat ? $"{wall:0%}, not drawn by the flat camera"
                : wall == 0 ? "Off"
                : keepVisibleTime ? $"{wall:0%} covered" : $"{wall:0%} covered (speed kept)";

            AddAssert("the wall toast names the wall, its cover and its keys", () =>
            {
                var description = toasts.DescribeWall(wall);
                return new[] { description.Name.ToString(), description.Value.ToString().Split(" · ")[0], description.Shortcut.ToString() };
            }, () => Is.EqualTo(new[] { "Wall", cover, HighwayToasts.WALL_SHORTCUT }));
            AddAssert("no wall reads Off", () => toasts.DescribeWall(0).Value.ToString(), () => Does.StartWith("Off · "));
        }

        /// <summary>
        /// The hotkeys show the toasts: Shift+F4 the wall's with its visible time, F4 mania's speed toast with the highway's
        /// visible time in place of mania's <c>11485 / speed</c>.
        /// </summary>
        [Test]
        public void TestHotkeysShowTheToasts()
        {
            setUp(HighwayCameraMode.Perspective, 0, false, false, 29);
            CreateTest();

            // Pressed straight away: osu! takes F3 and F4 only early in a play and in breaks.
            AddStep("press F4", () => InputManager.Key(Key.F4));
            AddUntilStep("speed 30", () => rulesetConfig.Get<double>(ManiaRulesetSetting.ScrollSpeed), () => Is.EqualTo(30));
            AddUntilStep("the speed toast shows the visible time", () => toastText(), () => Does.Contain($"{expected(30, 0)}ms (speed 30.0)"));

            AddStep("press Shift+F4", () =>
            {
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.Key(Key.F4);
                InputManager.ReleaseKey(Key.ShiftLeft);
            });
            AddUntilStep("the wall toast shows the cover and visible time", () => toastText(),
                () => Does.Contain($"5% covered (speed kept) · {expected(30, HighwayHotkeys.WALL_STEP)} ms visible"));

            // The default skin's hit position: mania's own label would say 383ms at speed 30.
            int expected(double scrollSpeed, float coveredByWall)
                => (int)Math.Round(DrawableManiaRuleset.ComputeScrollTime(scrollSpeed) * VisibleTime.HitPositionScale(Stage.HIT_TARGET_POSITION) * (1 - coveredByWall));
        }

        /// <summary>
        /// The wall toast names the wall in the chosen scale's units; Chunithm's keeps the speed, as in the arcade, so its
        /// wall 5 leaves about 71% of the visible time (Alpha: "9.0 with no wall becomes about 6.0 at wall 5").
        /// </summary>
        [Test]
        public void TestWallToastInChunithmUnits()
        {
            setUp(HighwayCameraMode.Perspective, 0, true, false, 29);
            AddStep("Chunithm's wall scale", () => highwayConfig.SetValue(HighwaySetting.WallScale, WallScale.Chunithm));
            AddAssert("which keeps the speed", () => highwayConfig.Get<bool>(HighwaySetting.WallKeepsVisibleTime), () => Is.False);
            CreateTest();

            for (int i = 0; i < 5; i++)
            {
                AddStep("press Shift+F4", () =>
                {
                    InputManager.PressKey(Key.ShiftLeft);
                    InputManager.Key(Key.F4);
                    InputManager.ReleaseKey(Key.ShiftLeft);
                });
            }

            AddUntilStep("the wall toast shows wall 5 and its visible time", () => toastText(),
                () => Does.Contain($"wall 5 (speed kept) · {expected(WallScales.ChunithmWall(5))} ms visible"));

            int expected(float coveredByWall)
                => (int)Math.Round(DrawableManiaRuleset.ComputeScrollTime(29) * VisibleTime.HitPositionScale(Stage.HIT_TARGET_POSITION) * (1 - coveredByWall));
        }

        private void setUp(HighwayCameraMode camera, float wall, bool keepVisibleTime, bool classicSkin, double speed) => AddStep(
            $"{camera}, wall {wall:0%}, keep visible time: {keepVisibleTime}, {(classicSkin ? "classic" : "default")} skin, speed {speed}", () =>
            {
                skins.CurrentSkinInfo.Value = classicSkin ? DefaultLegacySkin.CreateInfo().ToLiveUnmanaged() : ArgonSkin.CreateInfo().ToLiveUnmanaged();
                highwayConfig.GetBindable<WallScale>(HighwaySetting.WallScale).SetDefault();
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                highwayConfig.SetValue(HighwaySetting.NoteStyle, HighwayNoteStyle.Highway);
                highwayConfig.SetValue(HighwaySetting.CameraMode, camera);
                highwayConfig.SetValue(HighwaySetting.Wall, wall);
                highwayConfig.SetValue(HighwaySetting.WallKeepsVisibleTime, keepVisibleTime);
                rulesetConfig.SetValue(ManiaRulesetSetting.ScrollSpeed, speed);
            });

        private void stopAt(double time)
        {
            AddStep($"stop at {time}", () =>
            {
                Player.GameplayClockContainer.Stop();
                Player.GameplayClockContainer.Seek(time);
            });
            AddUntilStep("playfield at that time", () => drawableRuleset.FrameStableClock.CurrentTime, () => Is.EqualTo(time).Within(1));
            AddWaitStep("let the highway gather", 2);
        }

        /// <summary>
        /// The game time a note takes from coming into view to the line, from where it is drawn now: on the highway (tilted
        /// cameras), its depth against the depth where notes appear; in mania's own stage (flat camera), its height against the
        /// column's. Notes move at a constant speed on this chart, so one note in view gives it.
        /// </summary>
        private double measuredVisibleTime(bool flat)
        {
            double now = drawableRuleset.FrameStableClock.CurrentTime;

            if (flat)
            {
                var (note, share) = drawableRuleset.Playfield.Stages[0].Columns
                                                   .SelectMany(c => c.HitObjectContainer.AliveObjects.OfType<DrawableNote>()
                                                                     .Select(n => (note: n, share: -n.Y / c.HitObjectContainer.DrawHeight)))
                                                   .Where(n => n.share > 0.1f && n.share < 0.95f)
                                                   .MinBy(n => n.share);

                return (note.HitObject.StartTime - now) / share;
            }

            float appearDepth = highway.Warp.Scroll.AppearDepth;

            var (hitObject, depth) = Enumerable.Range(0, highway.NoteLayer.Count).Select(highway.NoteLayer.GetQuad)
                                               .Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note)
                                               .GroupBy(q => q.HitObject)
                                               .Select(g => (hitObject: g.Key, depth: g.Min(q => q.Depth0)))
                                               .Where(n => n.depth > 0.1f && n.depth < appearDepth - 0.05f)
                                               .MinBy(n => n.depth);

            return (hitObject.StartTime - now) * appearDepth / depth;
        }

        private static double shownMilliseconds(string text, string pattern)
        {
            var match = Regex.Match(text, pattern);

            if (!match.Success)
                throw new AssertionException($"no time in \"{text}\"");

            return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The text of the toast showing now.
        /// </summary>
        private string toastText() => string.Join(" | ", onScreenDisplay.ChildrenOfType<TrackedSettingToast>().SelectMany(t => t.ChildrenOfType<OsuSpriteText>())
                                                                 .Select(s => s.Current.Value));
    }
}
