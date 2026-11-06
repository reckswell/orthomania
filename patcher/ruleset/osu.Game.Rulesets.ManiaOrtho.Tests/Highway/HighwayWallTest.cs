// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for the wall's shape (SPEC 4; Alpha, 2026-11-04): it covers a stage's lanes and their edge lines from its
    /// depth to the far edge, wherever the stage is and however it leans, and nothing beside or above them.
    /// </summary>
    [TestFixture]
    public class HighwayWallTest
    {
        private static readonly Vector2 screen = new Vector2(1280, 720);

        private static HighwayProjection create(float farWidthRatio, float centre = 0.5f, float sway = 0)
            => new HighwayProjection(new HighwayLayout
            {
                JudgementY = 0.88f,
                FarY = 0.1f,
                NearWidth = 0.5f,
                FarWidthRatio = farWidthRatio,
                CentreX = centre,
                Sway = sway,
            }, screen);

        [TestCase(0.24f, 0.5f, 0f)] // the Chunithm preset
        [TestCase(0.106f, 0.5f, 0f)] // Voltex
        [TestCase(1f, 0.5f, 0f)] // parallel lanes
        [TestCase(0.24f, 0.3f, 0f)] // a ported skin's stage to the left
        [TestCase(0.24f, 0.5f, 0.03f)] // the kiai sway at its widest
        public void TestCoversTheLanesOnly(float ratio, float centre, float sway)
        {
            var projection = create(ratio, centre, sway);
            // A stage narrower than the track (a ported skin's column widths).
            var stage = new Vector2(0.1f, 0.85f);
            const float depth = 0.6f;

            var cover = HighwayWall.GetCover(projection, stage, depth, 1);

            foreach (float d in new[] { depth + 0.001f, 0.8f, 0.999f })
            {
                foreach (float lane in new[] { stage.X + 0.001f, (stage.X + stage.Y) / 2, stage.Y - 0.001f })
                    Assert.That(cover.Contains(projection.ToScreen(lane, d)), $"lane {lane} at depth {d}");

                // The stage's edge lines, as wide as the guides draw them.
                float half = Math.Max(HighwayGuides.LINE_WIDTH * projection.WidthAt(d), HighwayGuides.MIN_LINE_PIXELS) / 2;
                Assert.That(cover.Contains(projection.ToScreen(stage.X, d) - new Vector2(half, 0)), $"left edge line at depth {d}");
                Assert.That(cover.Contains(projection.ToScreen(stage.Y, d) + new Vector2(half, 0)), $"right edge line at depth {d}");

                // Beside the lanes: the rails, a skin's stage borders, the background.
                Assert.That(cover.Contains(projection.ToScreen(stage.X, d) - new Vector2(half + 2, 0)), Is.False, $"left of the stage at depth {d}");
                Assert.That(cover.Contains(projection.ToScreen(stage.Y, d) + new Vector2(half + 2, 0)), Is.False, $"right of the stage at depth {d}");
            }

            Assert.That(cover.Contains(projection.ToScreen((stage.X + stage.Y) / 2, depth - 0.01f)), Is.False, "notes before the wall");
            Assert.That(cover.Contains(projection.ToScreen((stage.X + stage.Y) / 2, 1) - new Vector2(0, 2)), Is.False, "above the far edge");
        }

        [Test]
        public void TestEdgeStandsOnTheLowerEdge()
        {
            var projection = create(0.24f, sway: 0.03f);
            var cover = HighwayWall.GetCover(projection, new Vector2(0, 1), 0.6f, 1);
            var edge = HighwayWall.GetEdge(cover, HighwayWall.EDGE_THICKNESS);

            Assert.That(edge.BottomLeft, Is.EqualTo(cover.BottomLeft));
            Assert.That(edge.BottomRight, Is.EqualTo(cover.BottomRight));
            Assert.That(edge.BottomLeft.Y - edge.TopLeft.Y, Is.EqualTo(HighwayWall.EDGE_THICKNESS).Within(1e-3f));
            Assert.That(edge.BottomRight.Y - edge.TopRight.Y, Is.EqualTo(HighwayWall.EDGE_THICKNESS).Within(1e-3f));

            // Its sides run along the cover's.
            float coverSlope = (cover.TopLeft.X - cover.BottomLeft.X) / (cover.TopLeft.Y - cover.BottomLeft.Y);
            float edgeSlope = (edge.TopLeft.X - edge.BottomLeft.X) / (edge.TopLeft.Y - edge.BottomLeft.Y);
            Assert.That(edgeSlope, Is.EqualTo(coverSlope).Within(1e-3f));
        }

        [Test]
        public void TestWallAtTheFarEdgeIsEmpty()
        {
            var cover = HighwayWall.GetCover(create(0.24f), new Vector2(0, 1), 1, 1);
            Assert.That(cover.BottomLeft.Y, Is.EqualTo(cover.TopLeft.Y).Within(1e-3f));
            Assert.That(HighwayWall.GetEdge(cover, HighwayWall.EDGE_THICKNESS).TopLeft.Y, Is.EqualTo(cover.BottomLeft.Y).Within(1e-3f));
        }
    }
}
