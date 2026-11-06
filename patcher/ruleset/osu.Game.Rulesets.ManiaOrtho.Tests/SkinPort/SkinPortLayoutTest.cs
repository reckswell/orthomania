// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.SkinPort;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.SkinPort
{
    /// <summary>
    /// How the skin porter fits a skin's flat layout to the camera, and where it draws the skin's rows.
    /// </summary>
    [TestFixture]
    public class SkinPortLayoutTest
    {
        private static readonly Vector2 size = new Vector2(1920, 1080);

        /// <summary>
        /// A 4K stage like Minty's: hit position 400 (stable), 180 units wide, at 1080p (1.6 × 1080/768 px per stable unit).
        /// </summary>
        private static FlatStage minty()
        {
            const float px_per_stable_unit = 1080f / 480;
            float hitY = 400 * px_per_stable_unit;
            return new FlatStage(hitY, 0, 180 * px_per_stable_unit, 1080f / 768, size);
        }

        private static HighwayLayout chunithm() => HighwayPresets.Get(HighwayPreset.Chunithm).ToLayout();

        [TestCase(SkinKeyArea.Panel, true)]
        [TestCase(SkinKeyArea.Floor, true)]
        [TestCase(SkinKeyArea.Squeezed, false)]
        [TestCase(SkinKeyArea.Receptors, false)]
        [TestCase(SkinKeyArea.Hidden, false)]
        public void TestLiftedLineSitsAtTheSkinsHitPosition(SkinKeyArea keyArea, bool lifts)
        {
            var stage = minty();
            var layout = SkinPortLayout.Adjust(chunithm(), keyArea, SkinStageFit.Camera, stage, null);

            Assert.That(layout.JudgementY, Is.EqualTo(lifts ? stage.HitY / size.Y : chunithm().JudgementY).Within(1e-5f));
        }

        [Test]
        public void TestLineIsNeverLoweredBelowTheCameras()
        {
            // Ākala 4K: hit position at the very bottom of the screen.
            var stage = minty() with { HitY = size.Y };
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Panel, SkinStageFit.Camera, stage, null);

            Assert.That(layout.JudgementY, Is.EqualTo(chunithm().JudgementY));
        }

        [Test]
        public void TestCameraFitKeepsTheCamerasTrack()
        {
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.Camera, minty(), 200);

            Assert.That(layout.CentreX, Is.EqualTo(0.5f));
            Assert.That(layout.NearWidth, Is.EqualTo(chunithm().NearWidth));
        }

        [Test]
        public void TestSkinPositionCentresTheTrackWhereStableCentresTheStage()
        {
            // R Skin 4K: ColumnStart 200, 208 units wide: centred at 304 of 853 stable units at 16:9.
            const float px_per_stable_unit = 1080f / 480;
            var stage = minty() with { Width = 208 * px_per_stable_unit };

            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinPosition, stage, 200);

            Assert.That(layout.CentreX, Is.EqualTo(304f / (480 * 16f / 9)).Within(1e-4f));
            Assert.That(layout.NearWidth, Is.EqualTo(chunithm().NearWidth), "the camera's width is kept");
        }

        [TestCase(SkinStageFit.SkinPosition)]
        [TestCase(SkinStageFit.SkinLayout)]
        public void TestStageToOneSideKeepsTheSkinsCentreAndNarrowsToFit(SkinStageFit fit)
        {
            // A stage pushed all the way to the left (an IIDX 1P layout): the camera's wide track would leave the screen around
            // the skin's centre, so it is narrowed there instead of moved.
            var stage = minty();
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, fit, stage, 0);
            float centre = stage.Width / 2 / size.X;

            Assert.That(layout.CentreX, Is.EqualTo(centre).Within(1e-5f), "the skin's centre, exactly");
            Assert.That(layout.CentreX - layout.NearWidth / 2, Is.GreaterThanOrEqualTo(-1e-5f), "inside the screen");
            Assert.That(layout.NearWidth, Is.LessThanOrEqualTo(chunithm().NearWidth + 1e-5f));
        }

        [Test]
        public void TestFloorKeyAreaFitsTheBottomRow()
        {
            // The floor runs on below the line along the lanes, so its bottom row (wider than the line) must fit too.
            var stage = minty() with { HitY = 0.8f * size.Y };
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Floor, SkinStageFit.SkinPosition, stage, 40);
            var projection = new HighwayProjection(layout, size);

            Assert.That(SkinPortLayout.WideningToBottom(projection), Is.GreaterThan(1.1f));
            Assert.That(layout.CentreX, Is.EqualTo(SkinPortLayout.StableCentre(stage, 40)).Within(1e-5f), "the skin's centre, exactly");
            Assert.That(projection.XAt(0, size.Y), Is.GreaterThanOrEqualTo(-0.5f), "the bottom row inside the screen");
            Assert.That(projection.XAt(0, size.Y), Is.LessThan(1), "and no narrower than that needs");
        }

        [Test]
        public void TestNeverNarrowerThanTheSkinsOwnStage()
        {
            // A stage the skin draws partly off the left edge: the track keeps the skin's centre and at least its width.
            var stage = minty();
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Panel, SkinStageFit.SkinPosition, stage, -40);

            Assert.That(layout.CentreX, Is.EqualTo(SkinPortLayout.StableCentre(stage, -40)).Within(1e-5f));
            Assert.That(layout.NearWidth, Is.EqualTo(stage.Width / size.X).Within(1e-5f));
        }

        [Test]
        public void TestStageToTheRightKeepsTheSkinsCentre()
        {
            // ColumnStart 560 at 16:9: the stage's right edge near the screen's.
            var stage = minty();
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinPosition, stage, 560);
            float centre = SkinPortLayout.StableCentre(stage, 560);

            Assert.That(centre, Is.GreaterThan(1 - chunithm().NearWidth / 2), "the camera's track would not fit around it");
            Assert.That(layout.CentreX, Is.EqualTo(centre).Within(1e-5f));
            Assert.That(layout.CentreX + layout.NearWidth / 2, Is.LessThanOrEqualTo(1 + 1e-5f));
        }

        /// <summary>
        /// No lean: the stage's centre line stays upright at the skin's centre, from the judgement line to the far edge.
        /// </summary>
        [TestCase(HighwayPreset.Chunithm)]
        [TestCase(HighwayPreset.Voltex)]
        [TestCase(HighwayPreset.Parallel)]
        public void TestNoLeanKeepsTheCentreLineUpright(HighwayPreset preset)
        {
            var stage = minty();
            var camera = HighwayPresets.Get(preset).ToLayout();
            var projection = new HighwayProjection(SkinPortLayout.Adjust(camera, SkinKeyArea.Hidden, SkinStageFit.SkinPosition, stage, 40), size);
            float centre = SkinPortLayout.StableCentre(stage, 40) * size.X;

            Assert.That(projection.ToScreen(0.5f, 0).X, Is.EqualTo(centre).Within(0.01f), "at the judgement line");
            Assert.That(projection.ToScreen(0.5f, 0.5f).X, Is.EqualTo(centre).Within(0.01f), "halfway up");
            Assert.That(projection.ToScreen(0.5f, 1).X, Is.EqualTo(centre).Within(0.01f), "at the far edge");

            if (projection.VanishingPoint is Vector2 vanishingPoint)
                Assert.That(vanishingPoint.X, Is.EqualTo(centre).Within(0.01f));
        }

        /// <summary>
        /// A lean brings the stage's far end in towards the middle, but its judgement line and key area stay where the skin puts
        /// them, and the lanes still meet in one point: that much of the way to the screen's centre.
        /// </summary>
        [TestCase(1f)]
        [TestCase(0.5f)]
        [TestCase(0.25f)]
        public void TestLeanKeepsTheLine(float towardsScreen)
        {
            var stage = minty();
            var projection = new HighwayProjection(SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinPosition, stage, 40, lean: towardsScreen), size);
            float centre = SkinPortLayout.StableCentre(stage, 40) * size.X;
            float lineY = projection.YAt(0);

            Assert.That(projection.ToScreen(0.5f, 0).X, Is.EqualTo(centre).Within(0.01f), "the line's centre stays at the skin's");
            Assert.That(projection.XAt(0.5f, lineY + 50), Is.EqualTo(centre).Within(0.01f), "and so does the key area below it");
            Assert.That(projection.ToScreen(0.5f, 1).X, Is.GreaterThan(centre + 1), "the far edge leans in towards the middle");

            var vanishingPoint = projection.VanishingPoint;
            Assert.That(vanishingPoint, Is.Not.Null);
            Assert.That(vanishingPoint!.Value.X, Is.EqualTo(centre + (size.X / 2 - centre) * towardsScreen).Within(0.05f));

            // Every lane edge is a straight line through that point.
            for (float lane = 0; lane <= 1; lane += 0.25f)
            {
                var near = projection.ToScreen(lane, 0);
                var far = projection.ToScreen(lane, 1);
                float t = (vanishingPoint.Value.Y - near.Y) / (far.Y - near.Y);
                Assert.That(near.X + (far.X - near.X) * t, Is.EqualTo(vanishingPoint.Value.X).Within(0.05f), $"lane edge {lane}");
            }
        }

        [Test]
        public void TestParallelLanesDoNotLean()
        {
            var parallel = HighwayPresets.Get(HighwayPreset.Parallel).ToLayout();
            var layout = SkinPortLayout.Adjust(parallel, SkinKeyArea.Hidden, SkinStageFit.SkinPosition, minty(), 40, lean: 1);

            Assert.That(layout.Lean, Is.Zero);
        }

        [Test]
        public void TestCentredSkinDoesNotLean()
        {
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinPosition, minty(), null, 1, 1);

            Assert.That(layout.CentreX, Is.EqualTo(0.5f));
            Assert.That(layout.Lean, Is.Zero);
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void TestPullMovesTheStageTowardsTheCentre(float pull)
        {
            var stage = minty();
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinLayout, stage, 40, pull);
            float centre = SkinPortLayout.StableCentre(stage, 40);

            Assert.That(layout.CentreX, Is.EqualTo(centre + (0.5f - centre) * pull).Within(1e-5f));
            Assert.That(layout.NearWidth, Is.EqualTo(stage.Width / size.X).Within(1e-5f), "the skin's width either way");
        }

        /// <summary>
        /// Pull and lean together: the lean is measured from where the pull put the stage, so a full lean still meets above the
        /// screen's centre, and a fully pulled stage is centred and has nothing to lean towards.
        /// </summary>
        [TestCase(0.5f, 1f)]
        [TestCase(0.25f, 0.5f)]
        [TestCase(1f, 1f)]
        public void TestPullThenLean(float pull, float lean)
        {
            var stage = minty();
            var projection = new HighwayProjection(SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinLayout, stage, 40, pull, lean), size);
            float centre = SkinPortLayout.StableCentre(stage, 40);
            float pulled = (centre + (0.5f - centre) * pull) * size.X;

            Assert.That(projection.ToScreen(0.5f, 0).X, Is.EqualTo(pulled).Within(0.01f), "the line's centre is where the pull put it");
            Assert.That(projection.VanishingPoint!.Value.X, Is.EqualTo(pulled + (size.X / 2 - pulled) * lean).Within(0.05f));

            if (pull == 1)
                Assert.That(projection.Layout.Lean, Is.EqualTo(0).Within(1e-6f));
        }

        /// <summary>
        /// Out-of-range values (a hand-edited config) act as the nearest end of the slider.
        /// </summary>
        [TestCase(2f, 1f)]
        [TestCase(-1f, 0f)]
        public void TestPullAndLeanAreClamped(float value, float clamped)
        {
            var stage = minty();
            var outside = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinLayout, stage, 40, value, value);
            var inside = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinLayout, stage, 40, clamped, clamped);

            Assert.That(outside.CentreX, Is.EqualTo(inside.CentreX));
            Assert.That(outside.Lean, Is.EqualTo(inside.Lean));
        }

        [Test]
        public void TestSkinLayoutLanesAreAsWideAsTheSkinsAtTheLine()
        {
            // Off-centre and leaning, every lane still spans the skin's own column width at the judgement line.
            var stage = minty();
            var projection = new HighwayProjection(SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Panel, SkinStageFit.SkinLayout, stage, 40, lean: 0.5f), size);
            float left = SkinPortLayout.StableCentre(stage, 40) * size.X - stage.Width / 2;

            Assert.That(projection.ToScreen(0, 0).X, Is.EqualTo(left).Within(0.05f));
            Assert.That(projection.ToScreen(0.25f, 0).X - projection.ToScreen(0, 0).X, Is.EqualTo(stage.Width / 4).Within(0.05f));
            Assert.That(projection.ToScreen(1, 0).X, Is.EqualTo(left + stage.Width).Within(0.05f));
        }

        [Test]
        public void TestCentredSkinStaysCentred()
        {
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinPosition, minty(), null);
            Assert.That(layout.CentreX, Is.EqualTo(0.5f));
        }

        [Test]
        public void TestSkinLayoutUsesTheSkinsWidth()
        {
            var stage = minty();
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Hidden, SkinStageFit.SkinLayout, stage, null);

            Assert.That(layout.NearWidth, Is.EqualTo(stage.Width / size.X).Within(1e-5f));
        }

        [Test]
        public void TestInvalidStageChangesNothing()
        {
            var layout = SkinPortLayout.Adjust(chunithm(), SkinKeyArea.Panel, SkinStageFit.SkinLayout, default, 200);
            Assert.That(layout, Is.EqualTo(chunithm()));
        }

        [TestCase(HighwayPreset.Chunithm)]
        [TestCase(HighwayPreset.Voltex)]
        [TestCase(HighwayPreset.Parallel)]
        public void TestNoteDepthKeepsTheImagesProportionsAtTheLine(HighwayPreset preset)
        {
            var projection = new HighwayProjection(HighwayPresets.Get(preset).ToLayout(), size);
            const float lane = 0.25f;
            const float aspect = 152f / 296; // Ākala's bars.

            float depth = SkinPortLayout.NoteDepth(projection, lane, aspect, 1);

            float height = projection.YAt(0) - projection.YAt(depth);
            float width = lane * projection.WidthAt(0);

            Assert.That(height / width, Is.EqualTo(aspect).Within(1e-3f));
        }

        [Test]
        public void TestPlacementMapsRowsAsTheWarpDoes()
        {
            var stage = minty();
            var projection = new HighwayProjection(chunithm(), size);
            var placement = new PortPlacement(projection, stage, SkinKeyArea.Receptors, 1.2f, 1);

            Assert.That(placement.DepthOfFlatY(stage.HitY), Is.Zero);
            Assert.That(placement.DepthOfFlatY(stage.ScrollTop), Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(placement.ScreenYBelow(stage.HitY + 10), Is.EqualTo(placement.LineY + 10).Within(1e-3f), "kept at its size below the line");
            Assert.That(placement.BelowScale, Is.EqualTo(1));
        }

        [Test]
        public void TestSqueezedKeyAreaFitsBelowTheLine()
        {
            var stage = minty();
            var projection = new HighwayProjection(chunithm(), size);
            var placement = new PortPlacement(projection, stage, SkinKeyArea.Squeezed, 1, 1);

            Assert.That(placement.ScreenYBelow(size.Y), Is.EqualTo(size.Y).Within(1e-2f), "the bottom of the key area at the bottom of the screen");
        }

        [Test]
        public void TestPanelHasUprightSides()
        {
            var projection = new HighwayProjection(chunithm(), size);
            var placement = new PortPlacement(projection, minty(), SkinKeyArea.Panel, 1, 1);

            Assert.That(placement.XBelow(0, size.Y), Is.EqualTo(projection.XAt(0, placement.LineY)).Within(1e-3f));

            var floor = new PortPlacement(projection, minty(), SkinKeyArea.Floor, 1, 1);
            Assert.That(floor.XBelow(0, size.Y), Is.LessThan(projection.XAt(0, placement.LineY)), "a floor widens below the line");
        }

        [Test]
        public void TestReadingColumnStartStillWorks()
        {
            // ColumnStart is read from an internal field of osu!'s legacy skin; this fails if osu! renames it.
            Assert.That(PortedSkin.CanReadColumnStart, Is.True);
        }

        [Test]
        public void TestLaneLightFadesAfterRelease()
        {
            Assert.That(PortedTrack.LightLevel(true, double.PositiveInfinity), Is.EqualTo(1));
            Assert.That(PortedTrack.LightLevel(false, 0), Is.EqualTo(1));
            Assert.That(PortedTrack.LightLevel(false, PortedTrack.LIGHT_FADE / 2), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(PortedTrack.LightLevel(false, PortedTrack.LIGHT_FADE), Is.Zero);
            Assert.That(PortedTrack.LightLevel(false, -10), Is.Zero, "rewound before the release");
        }

        [Test]
        public void TestHitLightingFadesInThenOut()
        {
            Assert.That(PortedLighting.HitAlpha(-1), Is.Zero);
            Assert.That(PortedLighting.HitAlpha(PortedLighting.FADE_IN / 2), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(PortedLighting.HitAlpha(PortedLighting.FADE_IN), Is.EqualTo(1).Within(1e-5f));
            Assert.That(PortedLighting.HitAlpha(PortedLighting.FADE_IN + PortedLighting.FADE_OUT), Is.Zero);
        }

        /// <summary>
        /// As mania's pooled explosions do, a hit while the column's previous explosion still plays adds a second one; the
        /// oldest is replaced only after <see cref="PortedLighting.HITS_PER_COLUMN"/> hits; rewinding forgets later hits.
        /// </summary>
        [Test]
        public void TestHitsInOneColumnOverlap()
        {
            const int columns = 2;
            double[] hitTimes = new double[columns * PortedLighting.HITS_PER_COLUMN];
            int[] nextHit = new int[columns];
            System.Array.Fill(hitTimes, double.NegativeInfinity);

            // A hold's press and its release 150 ms later, in column 1.
            PortedLighting.RecordHit(hitTimes, nextHit, 1, 1000);
            PortedLighting.RecordHit(hitTimes, nextHit, 1, 1150);

            Assert.That(column(1), Is.EquivalentTo(new[] { 1000, 1150, double.NegativeInfinity, double.NegativeInfinity }));
            Assert.That(column(0), Has.All.EqualTo(double.NegativeInfinity), "other columns untouched");
            Assert.That(PortedLighting.HitAlpha(1160 - 1000), Is.GreaterThan(0), "both lit at 1160");
            Assert.That(PortedLighting.HitAlpha(1160 - 1150), Is.GreaterThan(0));

            for (int i = 0; i < PortedLighting.HITS_PER_COLUMN; i++)
                PortedLighting.RecordHit(hitTimes, nextHit, 1, 2000 + i);

            Assert.That(column(1), Is.EquivalentTo(new double[] { 2000, 2001, 2002, 2003 }), "the oldest replaced first");

            PortedLighting.ForgetHitsAfter(hitTimes, 2001.5);
            Assert.That(column(1), Is.EquivalentTo(new[] { 2000, 2001, double.NegativeInfinity, double.NegativeInfinity }));

            double[] column(int c) => hitTimes[(c * PortedLighting.HITS_PER_COLUMN)..((c + 1) * PortedLighting.HITS_PER_COLUMN)];
        }
    }
}
