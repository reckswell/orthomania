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
    /// L0 checks for <see cref="ApproachCurve"/>: the ends stay put, Sekai's curve is Sekai's, and Even is a steady on-screen
    /// speed (research/topic-approach-angles.md, research/game-sekai.md).
    /// </summary>
    [TestFixture]
    public class ApproachCurveTest
    {
        private static IEnumerable<float> approaches() => new[] { ApproachCurve.PERSPECTIVE, -0.5f, ApproachCurve.SEKAI, 0.5f, ApproachCurve.EVEN };

        private static IEnumerable<float> ratios() => new[] { 0.073f, 0.24f, 0.5f, 0.9f };

        [Test]
        public void TestEndsStayPut([ValueSource(nameof(approaches))] float approach, [ValueSource(nameof(ratios))] float ratio)
        {
            float c = 1 / ratio - 1;

            Assert.That(ApproachCurve.Ease(0, approach, c), Is.EqualTo(0).Within(1e-5f));
            Assert.That(ApproachCurve.Ease(1, approach, c), Is.EqualTo(1).Within(1e-4f));
        }

        [Test]
        public void TestMonotonicAndInvertible([ValueSource(nameof(approaches))] float approach, [ValueSource(nameof(ratios))] float ratio)
        {
            float c = 1 / ratio - 1;
            float previous = float.NegativeInfinity;

            for (float t = -0.2f; t <= 1.0001f; t += 0.02f)
            {
                float eased = ApproachCurve.Ease(t, approach, c);

                Assert.That(eased, Is.GreaterThan(previous), $"t {t}");
                Assert.That(ApproachCurve.Unease(eased, approach, c), Is.EqualTo(t).Within(1e-4f), $"t {t}");
                previous = eased;
            }
        }

        /// <summary>
        /// Rows of mania's stage far above the track (depth up to 1000) stay finite and never move back down: the eased curves
        /// stop at the vanishing point, as true perspective does.
        /// </summary>
        [Test]
        public void TestFarPastTheFarEdgeStaysFinite([ValueSource(nameof(approaches))] float approach, [Values(-0.05f, 0.05f)] float nudge)
        {
            var layout = new HighwayLayout { JudgementY = 0.791f, FarY = 0.016f, NearWidth = 0.787f, FarWidthRatio = 0.073f, Approach = approach + nudge };
            var projection = new HighwayProjection(layout, new Vector2(1920, 1080));
            float previous = float.PositiveInfinity;

            foreach (float depth in new[] { 0.5f, 1, 2, 10, 34, 100, 1000, 1e6f })
            {
                float y = projection.YAt(depth);

                Assert.That(float.IsFinite(y), $"depth {depth}");
                Assert.That(y, Is.LessThanOrEqualTo(previous), $"depth {depth}");
                Assert.That(y, Is.GreaterThanOrEqualTo(projection.VanishingPoint!.Value.Y - 0.01f), $"depth {depth}");
                previous = y;
            }
        }

        [Test]
        public void TestPerspectiveIsLinearDepth()
        {
            Assert.That(ApproachCurve.Ease(0.37f, ApproachCurve.PERSPECTIVE, 3), Is.EqualTo(0.37f));
        }

        /// <summary>
        /// Sekai scales a note by <c>1.06^(45 (u - 1))</c> about the vanishing point (sonolus-pjsekai-engine <c>note.ts</c>),
        /// with u the elapsed share of the approach; its stage's far/near ratio is <c>1.06^-45</c>.
        /// </summary>
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(0.9f)]
        public void TestSekaiMatchesSekai(float timeToGo)
        {
            double ratio = Math.Pow(1.06, -45);
            float c = (float)(1 / ratio - 1);

            float depth = ApproachCurve.Ease(timeToGo, ApproachCurve.SEKAI, c);
            double scale = 1 / (1 + c * depth);

            Assert.That(scale, Is.EqualTo(Math.Pow(1.06, -45 * timeToGo)).Within(1e-4));
        }

        [TestCaseSource(nameof(ratios))]
        public void TestEvenIsSteadyOnScreen(float ratio)
        {
            var projection = new HighwayProjection(new HighwayLayout { JudgementY = 0.9f, FarY = 0.1f, NearWidth = 0.5f, FarWidthRatio = ratio }, new Vector2(1920, 1080));

            for (float t = 0; t <= 1.0001f; t += 0.1f)
            {
                float y = projection.YAt(ApproachCurve.Ease(t, ApproachCurve.EVEN, projection.Compression));
                Assert.That(y, Is.EqualTo((0.9f - 0.8f * t) * 1080).Within(0.05f), $"t {t}");
            }
        }

        /// <summary>
        /// Sekai's reason to ease: on its own lane, a note spends far longer in the near half than true perspective allows
        /// (research/game-sekai.md: 23.8% against 6.8% of its time).
        /// </summary>
        [Test]
        public void TestSekaiSpendsLongerNearTheLine()
        {
            var projection = new HighwayProjection(new HighwayLayout { JudgementY = 0.791f, FarY = 0.015f, NearWidth = 0.787f, FarWidthRatio = 0.073f },
                new Vector2(1920, 1080));

            float halfway = (projection.YAt(0) + projection.YAt(1)) / 2;

            Assert.That(timeInNearHalf(ApproachCurve.SEKAI), Is.EqualTo(0.238).Within(0.01));
            Assert.That(timeInNearHalf(ApproachCurve.PERSPECTIVE), Is.EqualTo(0.068).Within(0.01));

            double timeInNearHalf(float approach)
            {
                // The time still to go when the note crosses the halfway height.
                float depth = projection.DepthAtY(halfway);
                return ApproachCurve.Unease(depth, approach, projection.Compression);
            }
        }
    }
}
