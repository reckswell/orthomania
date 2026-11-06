// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.Timing;
using osu.Game.Rulesets.UI.Scrolling.Algorithms;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for time to depth (SPEC sections 3, 4 and 6, acceptance rows "visible time" and "wall").
    /// </summary>
    [TestFixture]
    public class HighwayScrollTest
    {
        private const double time_range = 400;
        private const double hit_time = 10_000;

        private static IEnumerable<IScrollAlgorithm> algorithms()
        {
            yield return new ConstantScrollAlgorithm();

            // 0.5x then 1.75x scroll velocity, like the synthetic playtest chart.
            var points = new List<MultiplierControlPoint>
            {
                new MultiplierControlPoint(0) { Velocity = 1 },
                new MultiplierControlPoint(hit_time - 300) { Velocity = 0.5 },
                new MultiplierControlPoint(hit_time - 100) { Velocity = 1.75 },
            };

            yield return new SequentialScrollAlgorithm(points);
            var sorted = new osu.Framework.Lists.SortedList<MultiplierControlPoint>(Comparer<MultiplierControlPoint>.Default);
            sorted.AddRange(points);
            yield return new OverlappingScrollAlgorithm(sorted);
        }

        private static IEnumerable<object[]> walls()
        {
            foreach (float wall in new[] { 0f, 0.4f, 0.7f })
            {
                yield return new object[] { wall, true };
                yield return new object[] { wall, false };
            }
        }

        [TestCaseSource(nameof(walls))]
        public void TestNoteCrossesTheLineAtItsHitTime(float wall, bool keepVisibleTime)
        {
            var scroll = new HighwayScroll(wall, keepVisibleTime);

            foreach (var algorithm in algorithms())
            {
                Assert.That(scroll.DepthAt(algorithm, hit_time, hit_time, time_range), Is.EqualTo(0).Within(1e-5f), algorithm.GetType().Name);
                Assert.That(scroll.DepthAt(algorithm, hit_time, hit_time + 1, time_range), Is.LessThan(0), "past notes go below the line");
            }
        }

        [TestCaseSource(nameof(walls))]
        public void TestNoteAppearsAtTheWallAfterTheVisibleTime(float wall, bool keepVisibleTime)
        {
            var scroll = new HighwayScroll(wall, keepVisibleTime);
            var algorithm = new ConstantScrollAlgorithm();

            double visibleTime = scroll.VisibleTime(time_range);

            Assert.That(scroll.DepthAt(algorithm, hit_time, hit_time - visibleTime, time_range), Is.EqualTo(scroll.AppearDepth).Within(1e-5f));
            Assert.That(scroll.AppearDepth, Is.EqualTo(1 - wall).Within(1e-6f));
        }

        [Test]
        public void TestDefaultWallKeepsVisibleTime()
        {
            foreach (float wall in new[] { 0f, 0.4f, 0.7f })
                Assert.That(new HighwayScroll(wall, true).VisibleTime(time_range), Is.EqualTo(time_range));
        }

        [Test]
        public void TestSpeedLockKeepsTrackSpeed()
        {
            var algorithm = new ConstantScrollAlgorithm();
            float depthPerMsWithoutWall = new HighwayScroll(0, false).DepthAt(algorithm, hit_time, hit_time - 100, time_range) / 100;

            foreach (float wall in new[] { 0.4f, 0.7f })
            {
                var scroll = new HighwayScroll(wall, false);
                Assert.That(scroll.DepthAt(algorithm, hit_time, hit_time - 100, time_range) / 100, Is.EqualTo(depthPerMsWithoutWall).Within(1e-7f));
                Assert.That(scroll.VisibleTime(time_range), Is.EqualTo(time_range * (1 - wall)).Within(1e-3));
            }
        }

        [Test]
        public void TestWallIsClamped()
        {
            Assert.That(new HighwayScroll(2, true).Wall, Is.EqualTo(HighwayScroll.MAX_WALL));
            Assert.That(new HighwayScroll(-1, true).Wall, Is.EqualTo(0));
        }

        [TestCase(HighwayCameraMode.Perspective, 0.5f)]
        [TestCase(HighwayCameraMode.ParallelTilt, 1f)]
        public void TestNoteLeadingEdgeOnTheLineOnScreen(HighwayCameraMode mode, float expectedRatio)
        {
            // End to end: settings -> layout + scroll -> screen. At its hit time a note is exactly on the judgement line.
            var settings = new HighwaySettings { CameraMode = mode, FarWidthRatio = 0.5f, JudgementLineHeight = 0.12f };
            var projection = new HighwayProjection(settings.ToLayout(), new Vector2(1920, 1080));
            var scroll = settings.ToScroll();

            Assert.That(projection.Layout.FarWidthRatio, Is.EqualTo(expectedRatio));

            float depth = scroll.DepthAt(new ConstantScrollAlgorithm(), hit_time, hit_time, time_range);
            Assert.That(projection.YAt(depth), Is.EqualTo(0.88f * 1080).Within(0.01f));

            float appear = scroll.DepthAt(new ConstantScrollAlgorithm(), hit_time, hit_time - scroll.VisibleTime(time_range), time_range);
            Assert.That(projection.YAt(appear), Is.EqualTo(projection.YAt(scroll.AppearDepth)).Within(0.01f));
        }
    }
}
