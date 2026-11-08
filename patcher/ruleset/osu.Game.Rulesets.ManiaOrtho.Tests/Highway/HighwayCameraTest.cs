// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for <see cref="HighwayCamera"/>: tilt and lane length reproduce the measured games and behave like a lane
    /// pivoting about the judgement line (research/topic-approach-angles.md).
    /// </summary>
    [TestFixture]
    public class HighwayCameraTest
    {
        /// <summary>
        /// Measured screen geometry (judgement line, far edge, far/near width) and the tilt and length research found for it
        /// at the 60° reference field of view.
        /// </summary>
        private static IEnumerable<TestCaseData> measuredGames()
        {
            yield return new TestCaseData(0.926f, 0.163f, 0.24f, 56.3f, 3.30f).SetName("Chunithm arcade");
            yield return new TestCaseData(0.926f, 0f, 0.24f, 47.5f, 3.72f).SetName("Chunithm, stretched to the top");
            yield return new TestCaseData(0.791f, 0.015f, 0.073f, 57.8f, 13.0f).SetName("Project Sekai");
            yield return new TestCaseData(0.941f, 0.284f, 0.106f, 71.3f, 7.71f).SetName("SDVX (USC landscape)");
            yield return new TestCaseData(0.7065f, 0.393f, 0.071f, 81.4f, 11.46f).SetName("SDVX EG frame");
        }

        [TestCaseSource(nameof(measuredGames))]
        public void TestMeasuredGamesRoundTrip(float judgementY, float farY, float ratio, float tilt, float length)
        {
            var camera = HighwayCamera.FromLayout(judgementY, farY, ratio);

            Assert.That(camera.Tilt, Is.EqualTo(tilt).Within(0.1f));
            Assert.That(camera.LaneLength, Is.EqualTo(length).Within(0.01f));

            var (projectedFarY, projectedRatio) = camera.Project(judgementY);

            Assert.That(projectedFarY, Is.EqualTo(farY).Within(1e-4f));
            Assert.That(projectedRatio, Is.EqualTo(ratio).Within(1e-4f));
        }

        [Test]
        public void TestTiltingBackLowersTheLaneEnd()
        {
            float previous = -1;

            for (float tilt = 50; tilt <= HighwayCamera.MAX_TILT; tilt += 5)
            {
                var (farY, _) = new HighwayCamera(tilt, 8).Project(0.9f);
                Assert.That(farY, Is.GreaterThan(previous), $"tilt {tilt}");
                previous = farY;
            }
        }

        [Test]
        public void TestLongerLaneNarrowsTheFarEnd()
        {
            float previous = 2;

            for (float length = HighwayCamera.MIN_LANE_LENGTH; length <= HighwayCamera.MAX_LANE_LENGTH; length += 1)
            {
                var (_, ratio) = new HighwayCamera(75, length).Project(0.9f);
                Assert.That(ratio, Is.LessThan(previous), $"length {length}");
                previous = ratio;
            }
        }

        [TestCase(0f)]
        [TestCase(20f)]
        [TestCase(40f)]
        public void TestLaneIsCutAtTheTopEdge(float tilt)
        {
            var (farY, ratio) = new HighwayCamera(tilt, HighwayCamera.MAX_LANE_LENGTH).Project(0.88f);

            Assert.That(farY, Is.EqualTo(0).Within(1e-5f));
            Assert.That(ratio, Is.InRange(0f, 1f));
        }

        [Test]
        public void TestUprightLaneHasParallelLanes()
        {
            var (farY, ratio) = new HighwayCamera(0, 4).Project(0.88f);

            Assert.That(farY, Is.EqualTo(0).Within(1e-5f));
            Assert.That(ratio, Is.EqualTo(1).Within(1e-5f));
        }

        /// <summary>
        /// A parallel camera shows no depth, so only the tilt counts: an upright lane reaches the top, and a lane leaning back
        /// is <c>cos(tilt)</c> of the screen tall above the line.
        /// </summary>
        [TestCase(1.2f)]
        [TestCase(9f)]
        public void TestParallelCameraKeepsLanesParallel(float length)
        {
            var (farY, ratio) = new HighwayCamera(60, length).Project(0.9f, perspective: false);

            Assert.That(ratio, Is.EqualTo(1));
            Assert.That(farY, Is.EqualTo(0.9f - 0.5f).Within(1e-5f));
            Assert.That(new HighwayCamera(0, length).Project(0.9f, perspective: false).FarY, Is.Zero);
        }

        /// <summary>
        /// Parallel lanes under a perspective camera stand upright, as long as the lane is tall.
        /// </summary>
        [TestCase(0.941f, 0.3f)]
        [TestCase(0.88f, 0f)]
        public void TestUprightPerspectiveLayoutRoundTrips(float judgementY, float farY)
        {
            var camera = HighwayCamera.FromLayout(judgementY, farY, 1);
            var (projectedFarY, ratio) = camera.Project(judgementY);

            Assert.That(camera.Tilt, Is.Zero);
            Assert.That(projectedFarY, Is.EqualTo(farY).Within(1e-4f));
            Assert.That(ratio, Is.EqualTo(1).Within(1e-5f));
        }

        [TestCase(0.9f, 0.4f)]
        [TestCase(0.67f, 0f)]
        public void TestParallelLayoutRoundTrips(float judgementY, float farY)
        {
            var camera = HighwayCamera.FromLayout(judgementY, farY, 1, perspective: false);

            Assert.That(camera.Project(judgementY, perspective: false).FarY, Is.EqualTo(farY).Within(1e-4f));

            if (farY == 0)
                Assert.That(camera.Tilt, Is.Zero, "a lane to the top stands upright");
        }

        /// <summary>
        /// The tilt is a rigid rotation about the judgement line: the near end of the lane keeps its screen position and width
        /// whatever the tilt, so only the part above the line moves.
        /// </summary>
        [Test]
        public void TestJudgementLineStaysPutWhileTilting()
        {
            var screen = new Vector2(1920, 1080);

            foreach (float tilt in new[] { 30f, 55f, 70f, 85f })
            {
                var (farY, ratio) = new HighwayCamera(tilt, 6).Project(0.9f);
                var projection = new HighwayProjection(new HighwayLayout { JudgementY = 0.9f, FarY = farY, NearWidth = 0.5f, FarWidthRatio = ratio }, screen);

                Assert.That(projection.ToScreen(0, 0).Y, Is.EqualTo(0.9f * screen.Y).Within(1e-3f));
                Assert.That(projection.WidthAt(0), Is.EqualTo(0.5f * screen.X).Within(1e-3f));
            }
        }

        [Test]
        public void TestOutOfRangeValuesAreClamped()
        {
            var (farY, ratio) = new HighwayCamera(float.NaN, float.PositiveInfinity).Project(0.9f);

            Assert.That(float.IsFinite(farY) && float.IsFinite(ratio));
            Assert.That(farY, Is.InRange(0f, 0.9f));
        }
    }
}
