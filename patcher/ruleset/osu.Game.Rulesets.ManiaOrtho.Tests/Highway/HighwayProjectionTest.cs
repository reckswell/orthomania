// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for <see cref="HighwayProjection"/> (SPEC sections 1, 2 and 6).
    /// </summary>
    [TestFixture]
    public class HighwayProjectionTest
    {
        private const float tolerance = 1e-3f;

        private static readonly Vector2 screen = new Vector2(1920, 1080);

        private static IEnumerable<float> farWidthRatios() => new[] { 1f, 0.8f, 0.5f, 0.3f, 0.1f };

        private static HighwayProjection create(float farWidthRatio, float judgementY = 0.88f, float farY = 0f, float nearWidth = 0.5f, float sway = 0)
            => new HighwayProjection(new HighwayLayout
            {
                JudgementY = judgementY,
                FarY = farY,
                NearWidth = nearWidth,
                FarWidthRatio = farWidthRatio,
                CentreX = 0.5f,
                Sway = sway,
            }, screen);

        [TestCaseSource(nameof(farWidthRatios))]
        public void TestCornersLandOnTheLayout(float ratio)
        {
            var p = create(ratio, judgementY: 0.85f, farY: 0.1f, nearWidth: 0.6f);

            float nearHalf = 0.3f * screen.X;
            float farHalf = nearHalf * ratio;

            assertNear(p.ToScreen(0, 0), new Vector2(screen.X / 2 - nearHalf, 0.85f * screen.Y));
            assertNear(p.ToScreen(1, 0), new Vector2(screen.X / 2 + nearHalf, 0.85f * screen.Y));
            assertNear(p.ToScreen(0, 1), new Vector2(screen.X / 2 - farHalf, 0.1f * screen.Y));
            assertNear(p.ToScreen(1, 1), new Vector2(screen.X / 2 + farHalf, 0.1f * screen.Y));
        }

        [TestCaseSource(nameof(farWidthRatios))]
        public void TestDepthIsMonotonicAndFarIsSlower(float ratio)
        {
            var p = create(ratio);

            float previousY = p.YAt(-0.05f);
            float previousStep = float.MaxValue;

            for (float d = -0.04f; d <= 1.0001f; d += 0.01f)
            {
                float y = p.YAt(d);
                float step = previousY - y;

                Assert.That(step, Is.GreaterThan(0), $"y must move up the screen as depth grows (d = {d})");

                // Perspective: equal steps in depth cover fewer pixels further away. Parallel: exactly equal.
                if (ratio < 1)
                    Assert.That(step, Is.LessThan(previousStep), $"far notes must move slower on screen (d = {d})");
                else
                    Assert.That(step, Is.EqualTo(0.01f * (0.88f * screen.Y)).Within(0.01f));

                previousY = y;
                previousStep = step;
            }
        }

        [TestCaseSource(nameof(farWidthRatios))]
        public void TestLaneEdgesAreStraightLines(float ratio)
        {
            var p = create(ratio);

            foreach (float lane in new[] { 0f, 0.25f, 0.5f, 0.9f, 1f })
            {
                Vector2 near = p.ToScreen(lane, 0);
                Vector2 far = p.ToScreen(lane, 1);

                for (float d = 0.1f; d < 1; d += 0.1f)
                {
                    Vector2 point = p.ToScreen(lane, d);
                    Vector2 expected = Vector2.Lerp(near, far, (point.Y - near.Y) / (far.Y - near.Y));
                    Assert.That(point.X, Is.EqualTo(expected.X).Within(0.01f), $"lane {lane} bends at depth {d}");
                }
            }
        }

        [TestCaseSource(nameof(farWidthRatios))]
        public void TestLanesMeetAtTheVanishingPoint(float ratio)
        {
            var p = create(ratio);

            if (ratio == 1)
            {
                Assert.That(p.VanishingPoint, Is.Null);
                return;
            }

            Vector2 vp = p.VanishingPoint!.Value;

            // SPEC 2: notes enter from the top edge, so the vanishing point must be above the far edge, never on the track.
            Assert.That(vp.Y, Is.LessThan(p.YAt(1)));

            foreach (float lane in new[] { 0f, 0.3f, 1f })
            {
                Vector2 near = p.ToScreen(lane, 0);
                Vector2 far = p.ToScreen(lane, 1);
                float xAtVanishingPoint = near.X + (far.X - near.X) * (vp.Y - near.Y) / (far.Y - near.Y);
                Assert.That(xAtVanishingPoint, Is.EqualTo(vp.X).Within(0.05f));
            }
        }

        [Test]
        public void TestMidpointFollowsTruePerspective()
        {
            // r = 0.5 gives c = 1, so depth 0.5 sits at 0.5 * 2 / 1.5 = 2/3 of the way up the track, with width scale 2/3.
            var p = create(0.5f, judgementY: 1, farY: 0);

            Assert.That(p.YAt(0.5f), Is.EqualTo(screen.Y / 3).Within(tolerance));
            Assert.That(p.ScaleAt(0.5f), Is.EqualTo(2 / 3f).Within(tolerance));
        }

        [TestCaseSource(nameof(farWidthRatios))]
        public void TestDepthAtYInvertsYAt(float ratio)
        {
            var p = create(ratio);

            for (float d = -0.04f; d <= 1; d += 0.05f)
                Assert.That(p.DepthAtY(p.YAt(d)), Is.EqualTo(d).Within(1e-4f));
        }

        [TestCaseSource(nameof(farWidthRatios))]
        public void TestNearPlaneCullsOnlyBelowTheLine(float ratio)
        {
            var p = create(ratio);

            Assert.That(p.MinDepth, Is.LessThan(0));

            if (ratio == 1)
                Assert.That(float.IsNegativeInfinity(p.MinDepth));
            else
                Assert.That(float.IsFinite(p.ScaleAt(p.MinDepth)) && p.ScaleAt(p.MinDepth) > 0);
        }

        [Test]
        public void TestOutOfRangeLayoutIsSanitised()
        {
            var p = new HighwayProjection(new HighwayLayout { JudgementY = 0.2f, FarY = 0.5f, FarWidthRatio = 0, NearWidth = 3 }, screen);

            Assert.That(p.Layout.FarY, Is.LessThan(p.Layout.JudgementY));
            Assert.That(p.Layout.FarWidthRatio, Is.EqualTo(HighwayLayout.MIN_FAR_WIDTH_RATIO));
            Assert.That(p.Layout.NearWidth, Is.EqualTo(1));
            Assert.That(float.IsFinite(p.YAt(1)) && float.IsFinite(p.ScaleAt(1)));
        }

        /// <summary>
        /// The kiai sway (SPEC 2) leans the track about the judgement line: the line and everything below it stay, the far edge
        /// moves by the whole sway, and lanes stay straight and still meet at one (moved) vanishing point.
        /// </summary>
        [TestCaseSource(nameof(farWidthRatios))]
        public void TestSwayLeansTheTrackAboutTheLine(float ratio)
        {
            var still = create(ratio, judgementY: 0.926f, farY: 0.05f);
            var swayed = create(ratio, judgementY: 0.926f, farY: 0.05f, sway: -0.03f);

            foreach (float lane in new[] { 0f, 0.3f, 1f })
            {
                Vector2 near = swayed.ToScreen(lane, 0);
                Vector2 far = swayed.ToScreen(lane, 1);

                Assert.That(near, Is.EqualTo(still.ToScreen(lane, 0)), "the judgement line stays");
                Assert.That(swayed.XAt(lane, near.Y + 60), Is.EqualTo(still.XAt(lane, near.Y + 60)), "below the line stays");
                Assert.That(far.X - still.ToScreen(lane, 1).X, Is.EqualTo(-0.03f * screen.X).Within(tolerance), "the far edge moves");
                Assert.That(far.Y, Is.EqualTo(still.ToScreen(lane, 1).Y), "only sideways");

                for (float d = 0.1f; d < 1; d += 0.1f)
                {
                    Vector2 point = swayed.ToScreen(lane, d);
                    Vector2 expected = Vector2.Lerp(near, far, (point.Y - near.Y) / (far.Y - near.Y));
                    Assert.That(point.X, Is.EqualTo(expected.X).Within(0.01f), $"lane {lane} bends at depth {d}");
                    Assert.That(swayed.XAt(lane, point.Y), Is.EqualTo(point.X).Within(tolerance));
                }

                if (ratio < 1)
                {
                    Vector2 vp = swayed.VanishingPoint!.Value;
                    float xAtVanishingPoint = near.X + (far.X - near.X) * (vp.Y - near.Y) / (far.Y - near.Y);
                    Assert.That(xAtVanishingPoint, Is.EqualTo(vp.X).Within(0.05f), "lanes meet at the vanishing point");
                }
            }
        }

        [Test]
        public void TestSwayIsSanitised()
        {
            Assert.That(create(0.5f, sway: 5).Layout.Sway, Is.EqualTo(HighwayLayout.MAX_SWAY));
            Assert.That(create(0.5f, sway: -5).Layout.Sway, Is.EqualTo(-HighwayLayout.MAX_SWAY));
            Assert.That(create(0.5f, sway: float.NaN).Layout.Sway, Is.Zero);
        }

        /// <summary>
        /// A lean moves where the lanes converge (the kiai sway adds on top of it), and leaves the judgement line in place.
        /// </summary>
        [Test]
        public void TestLeanMovesTheVanishingPoint()
        {
            var layout = new HighwayLayout { JudgementY = 0.9f, FarWidthRatio = 0.25f, NearWidth = 0.3f, CentreX = 0.2f };
            var leaning = new HighwayProjection(layout.LeanTowards(0.5f), screen);
            var upright = new HighwayProjection(layout, screen);

            Assert.That(leaning.VanishingPoint!.Value.X, Is.EqualTo(0.5f * screen.X).Within(0.01f));
            Assert.That(upright.VanishingPoint!.Value.X, Is.EqualTo(0.2f * screen.X).Within(0.01f));
            Assert.That(leaning.ToScreen(0.3f, 0).X, Is.EqualTo(upright.ToScreen(0.3f, 0).X).Within(1e-3f), "the line stays");

            var swaying = new HighwayProjection(layout.LeanTowards(0.5f) with { Sway = 0.01f }, screen);
            Assert.That(swaying.ToScreen(0.5f, 1).X - leaning.ToScreen(0.5f, 1).X, Is.EqualTo(0.01f * screen.X).Within(0.01f));

            Assert.That(new HighwayLayout { Lean = float.NaN }.Sanitised().Lean, Is.Zero);
            Assert.That(new HighwayLayout { Lean = 3 }.Sanitised().Lean, Is.EqualTo(1));
        }

        [TestCaseSource(nameof(farWidthRatios))]
        public void TestLaneEdgesContinueStraightBelowTheLine(float ratio)
        {
            var p = create(ratio);

            // On the track, the screen-y form agrees with the depth form.
            for (float d = 0; d <= 1.0001f; d += 0.25f)
            {
                Assert.That(p.WidthAtY(p.YAt(d)), Is.EqualTo(p.WidthAt(d)).Within(tolerance));
                Assert.That(p.XAt(0, p.YAt(d)), Is.EqualTo(p.ToScreen(0, d).X).Within(tolerance));
            }

            // Below the line, each lane edge stays on the straight line through its near and far corners.
            Vector2 near = p.ToScreen(0, 0);
            Vector2 far = p.ToScreen(0, 1);

            foreach (float below in new[] { 10f, 60f, 120f })
            {
                float y = near.Y + below;
                float expectedX = near.X + (far.X - near.X) * (y - near.Y) / (far.Y - near.Y);

                Assert.That(p.XAt(0, y), Is.EqualTo(expectedX).Within(tolerance));
                Assert.That(p.WidthAtY(y), Is.GreaterThanOrEqualTo(p.WidthAt(0) - tolerance), "the track does not narrow below the line");
            }
        }

        private static void assertNear(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.01f), "x");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.01f), "y");
        }
    }
}
