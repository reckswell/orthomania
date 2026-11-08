// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.Mods;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// Flashlight on the highway (SPEC 9): a note is lit exactly while mania would light it, wherever the camera draws it.
    /// </summary>
    public partial class TestSceneHighwayFlashlight : PlayerTestScene
    {
        /// <summary>
        /// In this drawable's units: notes this close to an edge of the band are not checked.
        /// </summary>
        private const float margin = 4;

        private const float manias_half_height = 50;

        protected override bool Autoplay => true;

        protected override bool HasCustomSteps => true;

        protected override Ruleset CreatePlayerRuleset() => new ManiaOrthoRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => PlaytestBeatmaps.Load("synthetic-4k");

        private HighwayConfigManager highwayConfig => ((ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(CreatePlayerRuleset()).AsNonNull()).Highway;

        private DrawableManiaOrthoRuleset drawableRuleset => (DrawableManiaOrthoRuleset)Player.DrawableRuleset;

        private HighwayPlayfieldAdjustmentContainer highway => drawableRuleset.Highway.AsNonNull();

        private ManiaOrthoModFlashlight.HighwayFlashlight flashlight => Player.ChildrenOfType<ManiaOrthoModFlashlight.HighwayFlashlight>().Single();

        [SetUpSteps]
        public override void SetUpSteps()
        {
            AddStep("Chunithm preset, native notes, full-size screen", () =>
            {
                Stack.RelativeSizeAxes = Axes.Both;
                Stack.Size = Vector2.One;

                highwayConfig.ForgetGames(); // each game remembers its settings; start from its own
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                highwayConfig.SetValue(HighwaySetting.Wall, 0f); // a game with a wall leaves it to the player
                highwayConfig.GetBindable<HighwayNoteStyle>(HighwaySetting.NoteStyle).SetDefault();
            });

            base.SetUpSteps();

            AddStep("load with Flashlight", () => LoadPlayer(new Mod[] { CreatePlayerRuleset().CreateModFromAcronym("FL").AsNonNull() }));
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("notes gathered", () => highway.NoteLayer.Count > 0);
        }

        [Test]
        public void TestNotesLitWhileManiaLightsThem()
        {
            AddUntilStep("lit notes in the drawn band, dark notes outside it", () =>
            {
                var light = flashlight;
                int last = light.EdgePointCount - 1;
                int litTop = ManiaOrthoModFlashlight.HighwayFlashlight.EDGE_STRIPS;

                // Mania's band, in the flashlight's space: lit within half height of the centre, dark past the soft edge.
                float centre = light.FlatBand.X;
                float halfHeight = light.FlatBand.Y;
                float softHalfHeight = halfHeight * light.FlashlightSmoothness;

                int lit = 0, dark = 0;

                foreach (var note in drawableRuleset.Playfield.Stages.SelectMany(s => s.Columns).SelectMany(c => c.HitObjectContainer.AliveObjects).OfType<DrawableNote>())
                {
                    if (note.Judged || note.Alpha < 1)
                        continue;

                    var quads = Enumerable.Range(0, highway.NoteLayer.Count).Select(highway.NoteLayer.GetQuad).Where(q => q.HitObject == note.HitObject).ToArray();

                    if (quads.Length == 0)
                        continue;

                    // Where mania puts the note's leading edge in its flat stage, and where the highway draws it.
                    float flatY = light.ToLocalSpace(note.ScreenSpaceDrawQuad.BottomLeft).Y;
                    float drawnY = light.ToLocalSpace(highway.ToScreenSpace(new Vector2(highway.DrawWidth / 2, highway.Projection.YAt(quads.Min(q => q.Depth0))))).Y;

                    float distance = Math.Abs(flatY - centre);

                    if (distance < halfHeight - margin)
                    {
                        if (drawnY < light.GetEdgeY(litTop) || drawnY > light.GetEdgeY(litTop + 1))
                            throw new AssertionException($"note at {note.HitObject.StartTime} is lit in mania but drawn at {drawnY}, outside the lit band");

                        lit++;
                    }
                    else if (distance > softHalfHeight + margin)
                    {
                        if (drawnY > light.GetEdgeY(0) && drawnY < light.GetEdgeY(last))
                            throw new AssertionException($"note at {note.HitObject.StartTime} is dark in mania but drawn at {drawnY}, inside the band");

                        dark++;
                    }
                }

                return lit > 0 && dark > 0;
            });
        }

        /// <summary>
        /// Mania's band is 50 playfield units each side of the centre (Flashlight's default size), scaled with the stage on
        /// screen; in portrait the stage is scaled up.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TestBandIsManiasSize(bool portrait)
        {
            if (portrait)
            {
                AddStep("portrait screen", () =>
                {
                    Stack.RelativeSizeAxes = Axes.None;
                    Stack.Size = new Vector2(400, 700);
                });

                AddWaitStep("let the layout settle", 3);
            }

            AddAssert("half height on screen is mania's in playfield units", () => flashlight.FlatBand.Y * pixelsPerUnit(flashlight),
                () => Is.EqualTo(manias_half_height * pixelsPerUnit(drawableRuleset.Playfield)).Within(0.5f));

            if (portrait)
                AddAssert("stage scaled up in portrait", () => pixelsPerUnit(drawableRuleset.Playfield), () => Is.GreaterThan(pixelsPerUnit(flashlight) * 1.1f));
        }

        private static float pixelsPerUnit(Drawable drawable)
            => (drawable.ScreenSpaceDrawQuad.BottomLeft.Y - drawable.ScreenSpaceDrawQuad.TopLeft.Y) / drawable.DrawHeight;

        /// <summary>
        /// The flat stage's rows as the warp draws them: on the track above the line, at flat height below it.
        /// </summary>
        [Test]
        public void TestDrawnRowsFollowTheWarp()
        {
            AddAssert("rows map as the warp draws them", () =>
            {
                var source = highway.Warp.Source;
                var projection = highway.Projection;
                float length = highway.Warp.Scroll.ScrollLength;

                if (Math.Abs(highway.DrawnYOfFlatY(source.Top) - projection.YAt(length)) > 0.5f)
                    throw new AssertionException("top of the scrolling area is not at the scroll length");
                if (Math.Abs(highway.DrawnYOfFlatY(source.Bottom) - projection.YAt(0)) > 0.5f)
                    throw new AssertionException("hit position is not on the judgement line");
                if (Math.Abs(highway.DrawnYOfFlatY(source.Bottom + 30) - (projection.YAt(0) + 30)) > 0.01f)
                    throw new AssertionException("rows below the line are not at their flat height");

                float previous = float.MinValue;

                for (float y = source.Top - 2000; y < highway.DrawHeight + 100; y += 5)
                {
                    float drawn = highway.DrawnYOfFlatY(y);

                    if (drawn < previous - 1e-3f)
                        throw new AssertionException($"row {y} is drawn above the row before it");

                    previous = drawn;
                }

                return true;
            });
        }

        [Test]
        public void TestBandMovesOntoTheTrack()
        {
            AddAssert("band drawn higher than mania's (far on the track)", () =>
            {
                var light = flashlight;
                return light.GetEdgeY(ManiaOrthoModFlashlight.HighwayFlashlight.EDGE_STRIPS) < light.FlatBand.X - light.FlatBand.Y - margin;
            });

            AddStep("flat camera", () => highwayConfig.SetValue(HighwaySetting.CameraMode, HighwayCameraMode.Flat));

            AddUntilStep("band is mania's", () =>
            {
                var light = flashlight;
                int litTop = ManiaOrthoModFlashlight.HighwayFlashlight.EDGE_STRIPS;

                return Math.Abs(light.GetEdgeY(litTop) - (light.FlatBand.X - light.FlatBand.Y)) < 0.01f
                       && Math.Abs(light.GetEdgeY(litTop + 1) - (light.FlatBand.X + light.FlatBand.Y)) < 0.01f;
            });
        }
    }
}
