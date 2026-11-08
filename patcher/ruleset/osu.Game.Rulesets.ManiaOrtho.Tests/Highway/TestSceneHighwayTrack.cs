// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.Skinning.Argon;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// The highway's own track (SPEC 5 and 8): under a tilted camera it replaces the skin's stage, lane backgrounds and
    /// receptors, and a pressed lane lights up from the judgement line. The skin's stage is kept under the flat camera and
    /// with the "Skin" track.
    /// </summary>
    public partial class TestSceneHighwayTrack : PlayerTestScene
    {
        private bool autoplay = true;

        /// <summary>
        /// Whether to play an osu! beatmap converted to mania (which Dual Stages needs) instead of a mania one.
        /// </summary>
        private bool convert;

        protected override bool Autoplay => autoplay;

        protected override Ruleset CreatePlayerRuleset() => new ManiaOrthoRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => convert ? new TestBeatmap(new OsuRuleset().RulesetInfo) : PlaytestBeatmaps.Load("synthetic-4k");

        private DrawableManiaOrthoRuleset drawableRuleset => (DrawableManiaOrthoRuleset)Player.DrawableRuleset;

        private HighwayPlayfieldAdjustmentContainer highway => drawableRuleset.Highway.AsNonNull();

        private HighwayTrack track => highway.Track;

        private Column[] columns => drawableRuleset.Playfield.Stages.SelectMany(s => s.Columns).ToArray();

        /// <summary>
        /// The key binding container mania's columns receive presses from, to press a lane's key as a player would.
        /// </summary>
        private KeyBindingContainer<ManiaAction> keyBindings => drawableRuleset.Playfield.FindClosestParent<KeyBindingContainer<ManiaAction>>().AsNonNull();

        private HighwayConfigManager highwayConfig => ((ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(CreatePlayerRuleset()).AsNonNull()).Highway;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            AddStep("Chunithm preset, highway track", () =>
            {
                autoplay = true;
                convert = false;
                highwayConfig.ForgetGames(); // each game remembers its settings; start from its own
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                highwayConfig.SetValue(HighwaySetting.Wall, 0f); // a game with a wall leaves it to the player
                highwayConfig.SetValue(HighwaySetting.Track, HighwayTrackStyle.Highway);
                highwayConfig.GetBindable<float>(HighwaySetting.TrackOpacity).SetDefault();
                highwayConfig.GetBindable<KeyBeamLength>(HighwaySetting.KeyBeam).SetDefault();
                highwayConfig.GetBindable<float>(HighwaySetting.KiaiSway).SetDefault();
            });

            base.SetUpSteps();
        }

        [TearDownSteps]
        public override void TearDownSteps()
        {
            AddStep("restore highway track", () =>
            {
                highwayConfig.SetValue(HighwaySetting.Track, HighwayTrackStyle.Highway);
                highwayConfig.GetBindable<float>(HighwaySetting.TrackOpacity).SetDefault();
                highwayConfig.GetBindable<KeyBeamLength>(HighwaySetting.KeyBeam).SetDefault();
                highwayConfig.GetBindable<float>(HighwaySetting.KiaiSway).SetDefault();
            });
            base.TearDownSteps();
        }

        [Test]
        public void TestTrackReplacesTheSkinsStage()
        {
            AddUntilStep("the track is shown", () => highway.TrackShown && track.Alpha == 1);

            AddAssert("two stage parts per stage and two per column", () => highway.SkinStageParts.Count,
                () => Is.EqualTo(2 * drawableRuleset.Playfield.Stages.Count + 2 * columns.Length));
            AddAssert("the skin draws them", () => skinStage().Select(d => d.GetType()).Distinct().Count(), () => Is.EqualTo(3));
            AddAssert("all hidden", () => highway.SkinStageParts.All(p => p.Alpha == 0));
            AddAssert("but still present (updated, and in the input queue as in mania)", () => highway.SkinStageParts.All(p => p.IsPresent));
            AddAssert("every lane background, receptor and stage background is inside one", () => skinStage().All(isHidden));
            AddAssert("the skin's hit targets are not", () => drawableRuleset.Playfield.ChildrenOfType<ArgonHitTarget>().All(t => !isHidden(t)));

            AddAssert("the floor spans the stage", () =>
            {
                var lanes = highway.ColumnLanes;
                var stage = track.StageLanes.ToArray().Single();
                return Math.Abs(stage.X - lanes[0].X) < 1e-4f && Math.Abs(stage.Y - lanes[^1].Y) < 1e-4f;
            });

            AddStep("use the skin's track", () => highwayConfig.SetValue(HighwaySetting.Track, HighwayTrackStyle.Skin));
            AddUntilStep("the skin's stage is back", () => !highway.TrackShown && highway.SkinStageParts.All(p => p.Alpha == 1));
            AddAssert("the track is hidden", () => track.Alpha, () => Is.Zero);
            AddAssert("its beams are dark", () => track.BeamAlphas.ToArray(), () => Has.All.Zero);

            AddStep("back to the highway's", () => highwayConfig.SetValue(HighwaySetting.Track, HighwayTrackStyle.Highway));
            AddUntilStep("the skin's stage is hidden again", () => highway.TrackShown && highway.SkinStageParts.All(p => p.Alpha == 0));
        }

        /// <summary>
        /// Sekai's Lane Transparency (research game-sekai.md K9): the floor fades, the rails and beams do not.
        /// </summary>
        [Test]
        public void TestTrackOpacity()
        {
            AddUntilStep("the floor is as designed", () => highway.TrackShown && track.FloorAlpha == HighwayTrack.FLOOR_ALPHA);

            AddStep("track opacity 50%", () => highwayConfig.SetValue(HighwaySetting.TrackOpacity, 0.5f));
            AddUntilStep("the floor is half as opaque", () => track.FloorAlpha, () => Is.EqualTo(HighwayTrack.FLOOR_ALPHA / 2).Within(1e-4f));

            AddStep("track opacity 0%", () => highwayConfig.SetValue(HighwaySetting.TrackOpacity, 0f));
            AddUntilStep("no floor", () => track.FloorAlpha, () => Is.Zero);
            AddAssert("the track is still shown (rails, beams)", () => highway.TrackShown && track.Alpha == 1);
        }

        /// <summary>
        /// DJMAX's Half and Quarter beams (research game-djmax.md R2): that share of the track's height on screen, up to the
        /// wall's edge when there is one.
        /// </summary>
        [Test]
        public void TestKeyBeamLength()
        {
            AddUntilStep("a long beam reaches half the track's depth", () => highway.TrackShown && track.BeamDepth == HighwayTrack.BEAM_LENGTH);

            AddStep("quarter beam", () => highwayConfig.SetValue(HighwaySetting.KeyBeam, KeyBeamLength.Quarter));
            AddUntilStep("the track takes it", () => track.KeyBeam, () => Is.EqualTo(KeyBeamLength.Quarter));
            AddAssert("a quarter of the track's height on screen", () => beamShareOnScreen(), () => Is.EqualTo(0.25f).Within(1e-3f));

            AddStep("wall at 30%", () => highwayConfig.SetValue(HighwaySetting.Wall, 0.3f));
            AddUntilStep("the track ends at the wall", () => track.FarDepth, () => Is.LessThan(1));
            AddAssert("still a quarter of the visible track", () => beamShareOnScreen(), () => Is.EqualTo(0.25f).Within(1e-3f));

            AddStep("half beam", () => highwayConfig.SetValue(HighwaySetting.KeyBeam, KeyBeamLength.Half));
            AddUntilStep("half of the visible track", () => beamShareOnScreen(), () => Is.EqualTo(0.5f).Within(1e-3f));

            AddStep("long beam", () => highwayConfig.SetValue(HighwaySetting.KeyBeam, KeyBeamLength.Long));
            AddUntilStep("half the track's depth again", () => track.BeamDepth, () => Is.EqualTo(HighwayTrack.BEAM_LENGTH));
        }

        /// <summary>
        /// The optional kiai sway (SPEC 2): off by default, and when on only in kiai sections (bars 8-12 of the synthetic chart),
        /// leaning the whole track, notes and warp alike, about a judgement line that never moves.
        /// </summary>
        [Test]
        public void TestKiaiSway()
        {
            double peak = 0;

            // Stopped on a beat inside the section, where the sway is at a side: a running clock could leave the section
            // between steps on a slow runner.
            AddStep("stop on a beat in the kiai section", () =>
            {
                var sway = new KiaiSway(drawableRuleset.Beatmap.ControlPointInfo);

                for (double t = 13000; t < 18000; t++)
                {
                    if (Math.Abs(sway.At(t)) > Math.Abs(sway.At(peak)))
                        peak = t;
                }

                Player.GameplayClockContainer.Stop();
                Player.GameplayClockContainer.Seek(peak);
            });
            AddUntilStep("playfield there", () => drawableRuleset.FrameStableClock.CurrentTime, () => Is.EqualTo(peak).Within(1));
            AddWaitStep("let the highway gather", 2);
            AddAssert("off by default: no sway", () => highway.Projection.Layout.Sway, () => Is.Zero);

            AddStep("kiai sway 100%", () => highwayConfig.SetValue(HighwaySetting.KiaiSway, 1f));
            AddUntilStep("the track leans with the beat", () => Math.Abs(highway.Projection.Layout.Sway), () => Is.GreaterThan(0.9f * HighwaySettings.MAX_KIAI_SWAY));
            AddAssert("by the beat's sway at this time", () => highway.Projection.Layout.Sway,
                () => Is.EqualTo(HighwaySettings.MAX_KIAI_SWAY * new KiaiSway(drawableRuleset.Beatmap.ControlPointInfo).At(highway.Time.Current)).Within(1e-5f));
            AddAssert("the track, warp, guides, notes and lights lean together",
                () => new[] { track.Layout.Sway, highway.Warp.Layout.Sway, highway.Guides.Layout.Sway, highway.NoteLayer.Layout.Sway, highway.SlotLights.Layout.Sway },
                () => Has.All.EqualTo(highway.Projection.Layout.Sway));
            AddAssert("the judgement line stays", () => highway.Projection.ToScreen(0, 0),
                () => Is.EqualTo(new HighwayProjection(highway.Projection.Layout with { Sway = 0 }, highway.Projection.Size).ToScreen(0, 0)));

            AddStep("wall at 30%", () => highwayConfig.SetValue(HighwaySetting.Wall, 0.3f));
            AddUntilStep("the track leans fully again", () => Math.Abs(highway.Projection.Layout.Sway), () => Is.GreaterThan(0.9f * HighwaySettings.MAX_KIAI_SWAY));
            AddAssert("the wall has the track's sway", () => highway.Wall.Layout.Sway, () => Is.EqualTo(highway.Projection.Layout.Sway));
            AddAssert("the wall leans with the lanes it covers", () =>
            {
                var p = highway.Projection;
                var still = new HighwayProjection(p.Layout with { Sway = 0 }, p.Size);
                float depth = highway.Warp.Scroll.AppearDepth;
                var stage = highway.Wall.Stages[0];
                var quad = highway.Wall.GetScreenSpaceCover(0);

                // Its corners move as the lanes' corners do: its lower edge by the sway there, its far edge by the whole sway
                // (less the few pixels it is widened by to cover the edge lines).
                float lower = highway.ToLocalSpace(quad.BottomLeft).X - still.ToScreen(stage.X, depth).X;
                float far = highway.ToLocalSpace(quad.TopLeft).X - still.ToScreen(stage.X, 1).X;

                return new[] { lower - p.SwayAtY(p.YAt(depth)), far - p.SwayAtY(p.YAt(1)) };
            }, () => Has.All.InRange(-3f, 0f));

            AddStep("seek well past the kiai section", () => Player.GameplayClockContainer.Seek(25000));
            AddUntilStep("playfield there", () => drawableRuleset.FrameStableClock.CurrentTime, () => Is.EqualTo(25000).Within(1));
            AddUntilStep("still again", () => highway.Projection.Layout.Sway, () => Is.Zero);
        }

        /// <summary>
        /// How much of the visible track's height on screen (from the judgement line to <see cref="HighwayTrack.FarDepth"/>) the
        /// beams reach.
        /// </summary>
        private float beamShareOnScreen()
        {
            var p = new HighwayProjection(track.Layout, track.DrawSize);
            float line = p.YAt(0);
            return (line - p.YAt(track.BeamDepth)) / (line - p.YAt(track.FarDepth));
        }

        [Test]
        public void TestFlatCameraKeepsTheSkinsStage()
        {
            AddStep("flat camera", () => highwayConfig.SetValue(HighwaySetting.CameraMode, HighwayCameraMode.Flat));
            AddUntilStep("the skin's stage is shown", () => !highway.TrackShown && highway.SkinStageParts.All(p => p.Alpha == 1));
            AddAssert("the track is hidden", () => track.Alpha, () => Is.Zero);
        }

        [Test]
        public void TestPressedLaneLightsUp()
        {
            AddStep("play without autoplay", () =>
            {
                autoplay = false;
                LoadPlayer();
            });
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("the track is shown", () => highway.TrackShown);

            AddStep("press the second lane's key", () => keyBindings.TriggerPressed(columns[1].Action.Value));
            AddUntilStep("its beam lights", () => track.BeamAlphas[1], () => Is.GreaterThanOrEqualTo(HighwayTrack.BEAM_ALPHA));
            AddAssert("the other lanes stay dark", () => track.BeamAlphas.ToArray().Where((_, i) => i != 1), () => Has.All.Zero);

            AddStep("release it", () => keyBindings.TriggerReleased(columns[1].Action.Value));
            AddUntilStep("it goes dark", () => track.BeamAlphas[1], () => Is.Zero);
        }

        [Test]
        public void TestDualStages()
        {
            AddStep("load a convert with Dual Stages, no autoplay", () =>
            {
                autoplay = false;
                convert = true;
                LoadPlayer(new Mod[] { CreatePlayerRuleset().CreateModFromAcronym("DS").AsNonNull() });
            });
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("the track is shown", () => highway.TrackShown);

            AddAssert("two stages", () => drawableRuleset.Playfield.Stages.Count, () => Is.EqualTo(2));
            AddAssert("a floor under each", () => track.StageLanes.Length, () => Is.EqualTo(2));
            AddAssert("two stage parts per stage and two per column", () => highway.SkinStageParts.Count, () => Is.EqualTo(2 * 2 + 2 * columns.Length));
            AddAssert("all hidden", () => highway.SkinStageParts.All(p => p.Alpha == 0));

            AddStep("wall at 30%", () => highwayConfig.SetValue(HighwaySetting.Wall, 0.3f));
            AddUntilStep("the wall covers each stage", () => highway.Wall.Stages.ToArray(), () => Is.EqualTo(track.StageLanes.ToArray()));
            AddAssert("from its first column's left edge to its last column's right edge", () => highway.Wall.Stages.ToArray(), () =>
            {
                var lanes = highway.ColumnLanes;
                int first = drawableRuleset.Playfield.Stages[0].Columns.Length;
                return Is.EqualTo(new[] { new Vector2(lanes[0].X, lanes[first - 1].Y), new Vector2(lanes[first].X, lanes[^1].Y) });
            });
            AddAssert("their lanes, not the gap between them", () =>
            {
                var p = highway.Projection;
                var stages = highway.Wall.Stages;
                var covers = new[] { highway.Wall.GetScreenSpaceCover(0), highway.Wall.GetScreenSpaceCover(1) };
                bool covered(float lane, float depth) => covers.Any(c => c.Contains(highway.ToScreenSpace(p.ToScreen(lane, depth))));

                float gap = (stages[0].Y + stages[1].X) / 2;
                return covered((stages[0].X + stages[0].Y) / 2, 0.9f) && covered((stages[1].X + stages[1].Y) / 2, 0.9f) && !covered(gap, 0.9f);
            });

            AddAssert("no rail covers the other stage", () =>
            {
                var stages = track.StageLanes;
                HighwayTrack.GetRailWidths(stages, 0, out _, out float right0);
                HighwayTrack.GetRailWidths(stages, 1, out float left1, out _);
                return stages[0].Y + right0 <= stages[1].X - left1 + 1e-5f;
            });

            int second = -1;

            AddStep("press a key of the second stage", () =>
            {
                second = drawableRuleset.Playfield.Stages[0].Columns.Length + 1;
                keyBindings.TriggerPressed(columns[second].Action.Value);
            });
            AddUntilStep("its lane lights", () => track.BeamAlphas[second], () => Is.GreaterThanOrEqualTo(HighwayTrack.BEAM_ALPHA));
            AddAssert("only its lane", () => track.BeamAlphas.ToArray().Where((_, i) => i != second), () => Has.All.Zero);
            AddStep("release it", () => keyBindings.TriggerReleased(columns[second].Action.Value));
        }

        [Test]
        public void TestHeldNoteBrightensItsBeam()
        {
            int column = -1;

            AddUntilStep("autoplay holds a note at full strength", () =>
            {
                column = Array.FindIndex(columns, c => c.HitObjectContainer.AliveObjects.OfType<DrawableHoldNote>().Any(h => h.IsHolding.Value));
                return column >= 0 && Math.Abs(track.BeamAlphas[column] - (HighwayTrack.BEAM_ALPHA + HighwayTrack.HOLD_BEAM_BOOST)) < 1e-4f;
            });

            // Autoplay may tap the lane again soon after, which lights it less than a hold does.
            AddUntilStep("its beam dims after release", () => track.BeamAlphas[column], () => Is.LessThan(HighwayTrack.BEAM_ALPHA));
        }

        /// <summary>
        /// The default (argon) skin's stage background, lane backgrounds and key areas in the playfield.
        /// </summary>
        private IEnumerable<Drawable> skinStage()
        {
            var playfield = drawableRuleset.Playfield;

            return playfield.ChildrenOfType<ArgonStageBackground>().Cast<Drawable>()
                            .Concat(playfield.ChildrenOfType<ArgonColumnBackground>())
                            .Concat(playfield.ChildrenOfType<ArgonKeyArea>());
        }

        private bool isHidden(Drawable drawable)
        {
            for (var d = drawable; d != null; d = d.Parent)
            {
                if (highway.SkinStageParts.Contains(d))
                    return true;
            }

            return false;
        }
    }
}
