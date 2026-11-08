// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// Hit lighting at the judgement line (SPEC 5 and 7): bursts in the hit lane's projected position, the lane's piece of the
    /// line lit in the result's colour, the skin's explosions hidden only while the highway's are shown, a glow while a hold
    /// is held, and nothing on a miss.
    /// </summary>
    public partial class TestSceneHighwayHitEffects : PlayerTestScene
    {
        private bool autoplay = true;

        protected override bool Autoplay => autoplay;

        protected override Ruleset CreatePlayerRuleset() => new ManiaOrthoRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => PlaytestBeatmaps.Load("synthetic-4k");

        private DrawableManiaOrthoRuleset drawableRuleset => (DrawableManiaOrthoRuleset)Player.DrawableRuleset;

        private HighwayPlayfieldAdjustmentContainer highway => drawableRuleset.Highway.AsNonNull();

        private HighwayHitEffectLayer effects => highway.HitEffects;

        private Column[] columns => drawableRuleset.Playfield.Stages.SelectMany(s => s.Columns).ToArray();

        private HighwayConfigManager highwayConfig => ((ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(CreatePlayerRuleset()).AsNonNull()).Highway;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            AddStep("Chunithm preset, highway effects", () =>
            {
                autoplay = true;
                highwayConfig.ForgetGames(); // each game remembers its settings; start from its own
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                highwayConfig.SetValue(HighwaySetting.Wall, 0f); // a game with a wall leaves it to the player
                highwayConfig.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Highway);
            });

            base.SetUpSteps();
        }

        [TearDownSteps]
        public override void TearDownSteps()
        {
            AddStep("restore highway effects", () => highwayConfig.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Highway));
            base.TearDownSteps();
        }

        [Test]
        public void TestBurstsSitOnTheLineInTheirLane()
        {
            var hitColumns = new List<int>();

            AddStep("record which columns are hit", () =>
            {
                hitColumns.Clear();

                for (int i = 0; i < columns.Length; i++)
                {
                    int index = i;

                    // The results mania shows an explosion for (Column.OnNewResult).
                    columns[i].NewResult += (judgedObject, result) =>
                    {
                        if (result.IsHit && judgedObject.DisplayResult)
                            hitColumns.Add(index);
                    };
                }
            });

            AddUntilStep("a few bursts are shown", () => effects.Bursts.Count >= 2);
            AddStep("freeze", () => Player.GameplayClockContainer.Stop());
            AddWaitStep("let the highway place them", 2);

            AddAssert("bursts are still shown", () => effects.Bursts.Count, () => Is.GreaterThan(0));

            AddAssert("each burst is in the column that was hit", () => effects.Bursts.Select(b => b.Column),
                () => Is.EqualTo(hitColumns.Skip(hitColumns.Count - effects.Bursts.Count)));

            AddAssert("the skin's explosions are hidden", () => columns.All(c => c.HitObjectArea.Explosions.Alpha == 0));
            AddAssert("and handed back, not kept", () => columns.Sum(c => c.HitObjectArea.Explosions.Count), () => Is.Zero);

            AddAssert("each burst is at its lane's centre on the judgement line", () => effects.Bursts.All(burst =>
            {
                Vector2 lanes = highway.ColumnLanes[burst.Column];
                Vector2 expected = highway.Projection.ToScreen((lanes.X + lanes.Y) / 2, 0);
                Vector2 drawn = highway.ToLocalSpace(burst.ScreenSpaceDrawQuad.Centre);

                return Vector2.Distance(drawn, expected) < 0.5f;
            }));

            AddAssert("autoplay's bursts are Perfect gold", () => effects.Bursts.All(b => b.Result == HitResult.Perfect && b.Style.Colour == HitBurstStyle.PERFECT_COLOUR));

            AddAssert("rings lie flat on the track", () => effects.Bursts.All(burst =>
            {
                var quad = burst.Ring.ScreenSpaceDrawQuad;
                return Math.Abs(quad.Height / quad.Width - HighwayHitEffectLayer.FLATTEN) < 0.01f;
            }));

            AddAssert("a fully grown ring fits the lane and the cap", () => effects.Bursts.All(burst =>
            {
                Vector2 lanes = highway.ColumnLanes[burst.Column];
                float trackWidth = highway.Projection.WidthAt(0);
                float grown = burst.Style.RingEnd * HighwayHitBurst.UNIT * burst.Scale.X;

                return grown <= HighwayHitEffectLayer.MAX_RING_SHARE * trackWidth + 0.5f
                       && grown <= burst.Style.RingEnd * (lanes.Y - lanes.X) * trackWidth + 0.5f;
            }));
        }

        /// <summary>
        /// Sekai's slot lights (research game-sekai.md K7): each hit lights its lane's piece of the judgement line in the
        /// result's colour, fading over 300 ms.
        /// </summary>
        [Test]
        public void TestHitsLightTheirLanesPieceOfTheLine()
        {
            var lastHit = new Dictionary<int, double>();

            AddStep("record when each column is hit", () =>
            {
                lastHit.Clear();

                for (int i = 0; i < columns.Length; i++)
                {
                    int index = i;

                    columns[i].NewResult += (judgedObject, result) =>
                    {
                        if (result.IsHit && judgedObject.DisplayResult)
                            lastHit[index] = columns[index].Time.Current;
                    };
                }
            });

            AddUntilStep("a few slots lit", () => effects.SlotsLit >= 2 && litSlots().Any());
            AddStep("freeze", () => Player.GameplayClockContainer.Stop());
            AddWaitStep("let the highway place them", 2);

            AddAssert("exactly the lanes hit in the last 300 ms are lit", () => litSlots(), () => Is.EquivalentTo(lastHit
                .Where(h => effects.Time.Current - h.Value < HighwayHitEffectLayer.SLOT_DURATION)
                .Select(h => h.Key)));

            AddAssert("each in a Perfect's pale gold, as strong as its age allows", () => litSlots().All(i =>
            {
                Color4 light = effects.SlotLights[i];
                Color4 perfect = HighwayHitEffectLayer.PERFECT_SLOT_COLOUR;
                float expected = HighwayHitEffectLayer.SLOT_ALPHA * HighwayHitEffectLayer.SlotLevel(effects.Time.Current - lastHit[i]);

                return light.R == perfect.R && light.G == perfect.G && light.B == perfect.B && Math.Abs(light.A - expected) < 1e-4f;
            }));

            AddAssert("drawn as set", () => highway.SlotLights.Lights.ToArray(), () => Is.EqualTo(effects.SlotLights.ToArray()));
            AddAssert("shown", () => highway.SlotLights.Alpha, () => Is.EqualTo(1));
            AddAssert("over the judgement line, under the notes", () =>
            {
                int slots = highway.Overlay.IndexOf(highway.SlotLights);
                return highway.Overlay.IndexOf(highway.Guides) < slots && slots < highway.Overlay.IndexOf(highway.NoteLayer);
            });
        }

        [Test]
        public void TestSlotsGoDarkWhenTheEffectsAreSwitchedOff()
        {
            AddUntilStep("a slot is lit", () => litSlots().Any());
            AddStep("freeze", () => Player.GameplayClockContainer.Stop());
            AddWaitStep("let the highway place them", 2);
            AddAssert("still lit", () => litSlots().Any());

            // Time stands still, so a light kept from before would still be lit.
            AddStep("use the skin's effects", () => highwayConfig.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Skin));
            AddUntilStep("the highway's are off", () => !effects.Active);
            AddStep("back to the highway's", () => highwayConfig.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Highway));
            AddUntilStep("the highway's are on", () => effects.Active);
            AddWaitStep("let the highway place them", 2);
            AddAssert("no light kept from before", () => litSlots(), () => Is.Empty);
            AddAssert("none drawn", () => highway.SlotLights.Lights.ToArray().All(c => c.A == 0));
        }

        private int[] litSlots() => Enumerable.Range(0, effects.SlotLights.Length).Where(i => effects.SlotLights[i].A > 0).ToArray();

        [Test]
        public void TestSkinEffects()
        {
            AddUntilStep("a burst is shown", () => effects.Bursts.Count > 0);

            AddAssert("no skin explosions are kept meanwhile", () => columns.Sum(c => c.HitObjectArea.Explosions.Count), () => Is.Zero);

            AddStep("use the skin's effects", () => highwayConfig.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Skin));
            AddUntilStep("the skin's explosions are back", () => columns.All(c => c.HitObjectArea.Explosions.Alpha == 1));
            AddUntilStep("the bursts are gone", () => effects.Bursts.Count == 0);
            AddAssert("so are the slot lights", () => litSlots().Length == 0 && highway.SlotLights.Alpha == 0);
            AddUntilStep("the skin shows an explosion", () => columns.Any(c => c.HitObjectArea.Explosions.Count > 0));
            AddAssert("and no new bursts", () => effects.Bursts.Count, () => Is.Zero);

            AddStep("back to the highway's", () => highwayConfig.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Highway));
            AddUntilStep("bursts again", () => effects.Bursts.Count > 0);
            AddAssert("the skin's explosions are hidden", () => columns.All(c => c.HitObjectArea.Explosions.Alpha == 0));
        }

        [Test]
        public void TestFlatCameraKeepsTheSkinsEffects()
        {
            AddStep("flat camera", () => highwayConfig.SetValue(HighwaySetting.CameraMode, HighwayCameraMode.Flat));
            AddUntilStep("the skin's explosions are shown", () => columns.All(c => c.HitObjectArea.Explosions.Alpha == 1));
            AddAssert("the highway's are off", () => !effects.Active && effects.Alpha == 0 && highway.SlotLights.Alpha == 0);
            AddUntilStep("the skin shows an explosion", () => columns.Any(c => c.HitObjectArea.Explosions.Count > 0));
            AddAssert("and no bursts", () => effects.Bursts.Count, () => Is.Zero);
        }

        [Test]
        public void TestHeldNoteLightsItsLane()
        {
            int column = -1;

            AddUntilStep("a hold note is being held, its lane lit", () =>
            {
                column = Array.FindIndex(columns, c => c.HitObjectContainer.AliveObjects.OfType<DrawableHoldNote>().Any(h => h.IsHolding.Value));
                return column >= 0 && effects.HoldGlows[column].Holding && effects.HoldGlows[column].Alpha > 0.5f;
            });

            AddAssert("only held lanes are lit", () => Enumerable.Range(0, columns.Length).All(i =>
                effects.HoldGlows[i].Holding == columns[i].HitObjectContainer.AliveObjects.OfType<DrawableHoldNote>().Any(h => h.IsHolding.Value)));

            AddAssert("the glow is at its lane's centre on the line", () =>
            {
                Vector2 lanes = highway.ColumnLanes[column];
                Vector2 expected = highway.Projection.ToScreen((lanes.X + lanes.Y) / 2, 0);

                return Vector2.Distance(highway.ToLocalSpace(effects.HoldGlows[column].ScreenSpaceDrawQuad.Centre), expected) < 0.5f;
            });

            AddUntilStep("its lane goes dark after release", () => !effects.HoldGlows[column].Holding && effects.HoldGlows[column].Alpha == 0);
        }

        [Test]
        public void TestNoBurstOnAMiss()
        {
            var misses = new List<HitResult>();

            AddStep("play without input", () =>
            {
                autoplay = false;
                LoadPlayer();
            });
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddStep("watch results", () =>
            {
                misses.Clear();
                Player.ScoreProcessor.NewJudgement += r => misses.Add(r.Type);
            });

            AddUntilStep("notes are missed", () => misses.Count(r => r == HitResult.Miss) >= 3);
            AddAssert("the highway's effects are on", () => effects.Active);
            AddAssert("no bursts", () => effects.Bursts.Count, () => Is.Zero);
            AddAssert("no slot lit", () => effects.SlotsLit, () => Is.Zero);
        }
    }
}
