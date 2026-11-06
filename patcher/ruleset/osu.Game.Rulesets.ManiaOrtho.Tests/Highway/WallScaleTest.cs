// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for the wall scales (SPEC 4; research/topic-covers.md, research/game-chunithm.md): each game's units map to
    /// the wall that game would draw, round trip, and step as that game's keys do.
    /// </summary>
    [TestFixture]
    public class WallScaleTest
    {
        private const float tolerance = 1e-4f;

        /// <summary>
        /// The precision the config stores the wall at.
        /// </summary>
        private const float wall_precision = 0.0001f;

        /// <summary>
        /// Parallel lanes, the Chunithm and Voltex presets' cameras, and others down to the steepest allowed.
        /// </summary>
        private static IEnumerable<float> farWidthRatios() => new[] { 1f, 0.6f, 0.3f, 0.24f, 0.106f, HighwayLayout.MIN_FAR_WIDTH_RATIO };

        private static readonly WallScale[] scales = { WallScale.Percent, WallScale.Chunithm, WallScale.Iidx, WallScale.Lane };

        /// <summary>
        /// Chunithm's wall plane per step: 23 arcade units apart up to step 11, then wider, over the 395 units from the far
        /// edge to the judgement line.
        /// </summary>
        [TestCase(0, 0f)]
        [TestCase(1, 23 / 395f)]
        [TestCase(5, 115 / 395f)] // ~29%: Alpha's "9.0 with no wall becomes about 6.0 at wall 5" is ~0.71 of the time
        [TestCase(11, 253 / 395f)]
        [TestCase(12, 280.6f / 395)]
        [TestCase(14, 349.6f / 395)]
        public void TestChunithmSteps(int step, float wall)
        {
            Assert.That(WallScales.ChunithmWall(step), Is.EqualTo(wall).Within(tolerance));
            Assert.That(WallScales.ToUnits(WallScale.Chunithm, wall, 0.3f), Is.EqualTo(step));
            Assert.That(WallScales.FromUnits(WallScale.Chunithm, step, 0.3f), Is.EqualTo(wall).Within(tolerance));
        }

        [Test]
        public void TestChunithmStepsStayInRange()
        {
            Assert.That(WallScales.ChunithmWall(-1), Is.Zero);
            Assert.That(WallScales.ChunithmWall(15), Is.EqualTo(WallScales.ChunithmWall(WallScales.MAX_CHUNITHM_STEP)));
            Assert.That(WallScales.ChunithmWall(WallScales.MAX_CHUNITHM_STEP), Is.LessThanOrEqualTo(HighwayScroll.MAX_WALL));
            Assert.That(WallScales.ChunithmStep(HighwayScroll.MAX_WALL), Is.EqualTo(WallScales.MAX_CHUNITHM_STEP));
        }

        /// <summary>
        /// The screen-based scales measure the lane's height on screen, so they match the projection the highway draws with:
        /// parallel lanes cover as much screen as depth, converging ones less (the far end is squeezed).
        /// </summary>
        [TestCaseSource(nameof(farWidthRatios))]
        public void TestScreenShareMatchesTheProjection(float ratio)
        {
            var projection = new HighwayProjection(new HighwayLayout { JudgementY = 0.88f, FarY = 0.1f, NearWidth = 0.5f, FarWidthRatio = ratio, CentreX = 0.5f },
                new Vector2(1920, 1080));

            foreach (float wall in new[] { 0f, 0.1f, 0.3f, 0.5f, 0.9f })
            {
                float lane = projection.YAt(0) - projection.YAt(1);
                float covered = projection.YAt(1 - wall) - projection.YAt(1);

                Assert.That(WallScales.ScreenShare(wall, ratio), Is.EqualTo(covered / lane).Within(tolerance), $"wall {wall}");
                Assert.That(WallScales.WallForScreenShare(WallScales.ScreenShare(wall, ratio), ratio), Is.EqualTo(wall).Within(tolerance));

                if (ratio == 1)
                    Assert.That(WallScales.ScreenShare(wall, ratio), Is.EqualTo(wall).Within(tolerance));
                else if (wall > 0 && wall < 1)
                    Assert.That(WallScales.ScreenShare(wall, ratio), Is.LessThan(wall));
            }
        }

        [TestCaseSource(nameof(farWidthRatios))]
        public void TestUnitsRoundTrip(float ratio)
        {
            foreach (var scale in scales)
            {
                for (int units = 0; units <= WallScales.MaxUnits(scale, ratio); units++)
                {
                    float wall = WallScales.FromUnits(scale, units, ratio);
                    Assert.That(wall, Is.InRange(0, HighwayScroll.MAX_WALL), $"{scale} {units}");
                    Assert.That(WallScales.ToUnits(scale, wall, ratio), Is.EqualTo(units), $"{scale} {units}");

                    // Also once stored at the config's precision.
                    float stored = MathF.Round(wall / wall_precision) * wall_precision;
                    Assert.That(WallScales.ToUnits(scale, stored, ratio), Is.EqualTo(units), $"{scale} {units} stored");
                }
            }
        }

        [TestCase(1f, 900, 90)]
        [TestCase(0.3f, 730, 73)] // a perspective camera squeezes the far end: 90% of the depth is 73% of the lane on screen
        [TestCase(0.24f, 684, 68)] // the Chunithm preset
        [TestCase(0.106f, 488, 49)] // the Voltex preset
        public void TestMostUnits(float ratio, int whiteNumber, int lanePercent)
        {
            Assert.That(WallScales.MaxUnits(WallScale.Percent, ratio), Is.EqualTo(90));
            Assert.That(WallScales.MaxUnits(WallScale.Chunithm, ratio), Is.EqualTo(WallScales.MAX_CHUNITHM_STEP));
            Assert.That(WallScales.MaxUnits(WallScale.Iidx, ratio), Is.EqualTo(whiteNumber));
            Assert.That(WallScales.MaxUnits(WallScale.Lane, ratio), Is.EqualTo(lanePercent));
            Assert.That(WallScales.FromUnits(WallScale.Iidx, 1000, ratio), Is.EqualTo(HighwayScroll.MAX_WALL).Within(tolerance));
        }

        /// <summary>
        /// One press of Shift+F3 or Shift+F4 moves one step of the chosen scale, and stops at either end.
        /// </summary>
        [Test]
        public void TestKeySteps()
        {
            const float ratio = 0.3f;

            Assert.That(WallScales.Step(WallScale.Percent, 0, 1, ratio), Is.EqualTo(0.05f).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Chunithm, 0, 1, ratio), Is.EqualTo(WallScales.ChunithmWall(1)).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Chunithm, WallScales.ChunithmWall(5), -1, ratio), Is.EqualTo(WallScales.ChunithmWall(4)).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Iidx, 0, 1, ratio), Is.EqualTo(WallScales.WallForScreenShare(0.05f, ratio)).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Lane, 0, 2, ratio), Is.EqualTo(WallScales.WallForScreenShare(0.1f, ratio)).Within(tolerance));

            // From between two steps (Chunithm's 5 is 0.291, 6 is 0.349), one press lands on the next step in its direction.
            Assert.That(WallScales.Step(WallScale.Chunithm, 0.30f, 1, ratio), Is.EqualTo(WallScales.ChunithmWall(6)).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Chunithm, 0.30f, -1, ratio), Is.EqualTo(WallScales.ChunithmWall(5)).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Chunithm, 0.33f, 1, ratio), Is.EqualTo(WallScales.ChunithmWall(6)).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Chunithm, 0.33f, -1, ratio), Is.EqualTo(WallScales.ChunithmWall(5)).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Percent, 0.076f, 1, ratio), Is.EqualTo(0.12f).Within(tolerance));
            Assert.That(WallScales.Step(WallScale.Percent, 0.076f, -1, ratio), Is.EqualTo(0.03f).Within(tolerance));
        }

        /// <summary>
        /// At either end the keys stop on the wall's own range, and a press never moves the wall against the key or more than
        /// one step, from any wall (a 90% wall set in percent stays at 90% under Chunithm's scale, whose last step is 88.5%).
        /// </summary>
        [TestCaseSource(nameof(farWidthRatios))]
        public void TestKeysStopAtTheEndsAndNeverGoBack(float ratio)
        {
            foreach (var scale in scales)
            {
                Assert.That(WallScales.Step(scale, 0, -1, ratio), Is.Zero, $"{scale} below none");
                Assert.That(WallScales.Step(scale, HighwayScroll.MAX_WALL, 1, ratio), Is.EqualTo(HighwayScroll.MAX_WALL), $"{scale} past its most");
                Assert.That(WallScales.ToUnits(scale, HighwayScroll.MAX_WALL, ratio), Is.EqualTo(WallScales.MaxUnits(scale, ratio)), $"{scale} most");

                for (float wall = 0; wall <= HighwayScroll.MAX_WALL; wall += 0.0137f)
                {
                    float up = WallScales.Step(scale, wall, 1, ratio);
                    float down = WallScales.Step(scale, wall, -1, ratio);
                    int units = WallScales.ToUnits(scale, wall, ratio);

                    Assert.That(up, Is.GreaterThanOrEqualTo(wall), $"{scale} up from {wall}");
                    Assert.That(down, Is.LessThanOrEqualTo(wall), $"{scale} down from {wall}");
                    Assert.That(WallScales.ToUnits(scale, up, ratio) - units, Is.LessThanOrEqualTo(WallScales.KeyStep(scale)), $"{scale} up from {wall}");
                    Assert.That(units - WallScales.ToUnits(scale, down, ratio), Is.LessThanOrEqualTo(WallScales.KeyStep(scale)), $"{scale} down from {wall}");
                }
            }
        }

        /// <summary>
        /// Chunithm, osu!mania and Quaver keep the speed under a wall; IIDX keeps the visible time (Floating Hi-Speed). A plain
        /// percent leaves the choice alone.
        /// </summary>
        [TestCase(WallScale.Percent, null)]
        [TestCase(WallScale.Chunithm, false)]
        [TestCase(WallScale.Iidx, true)]
        [TestCase(WallScale.Lane, false)]
        public void TestSpeedBehaviour(WallScale scale, bool? keepsVisibleTime)
            => Assert.That(WallScales.KeepsVisibleTime(scale), Is.EqualTo(keepsVisibleTime));

        [TestCase(WallScale.Percent, 0.4f, 1f, "40% covered")]
        [TestCase(WallScale.Lane, 0.4f, 0.24f, "14% of the lane covered")] // the Chunithm preset squeezes the far end
        [TestCase(WallScale.Chunithm, 115 / 395f, 0.3f, "wall 5")]
        [TestCase(WallScale.Iidx, 0.35f, 1f, "WN 350")]
        [TestCase(WallScale.Lane, 0.25f, 1f, "25% of the lane covered")]
        public void TestDescriptions(WallScale scale, float wall, float ratio, string expected)
            => Assert.That(WallScales.Describe(scale, wall, ratio), Is.EqualTo(expected));
    }
}
