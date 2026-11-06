// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for <see cref="TrackGrid"/>, which places every cell of the warped stage (SPEC sections 1 and 6). The warp
    /// maps the stage's hit position to depth 0 and the top of its scrolling area to the scroll length; these tests check
    /// that those texture rows land exactly on those depths and that nothing tears between cells.
    /// </summary>
    [TestFixture]
    public class TrackGridTest
    {
        private const float tolerance = 1e-3f;

        private static readonly RectangleF texture = new RectangleF(100, 50, 400, 600);

        private static HighwayProjection create(float farWidthRatio) => new HighwayProjection(new HighwayLayout
        {
            JudgementY = 0.926f,
            FarY = 0,
            NearWidth = 0.61f,
            FarWidthRatio = farWidthRatio,
            CentreX = 0.5f,
        }, new Vector2(1920, 1080));

        [TestCase(1f, 1f)]
        [TestCase(0.24f, 1f)]
        [TestCase(0.24f, 0.7f)]
        [TestCase(0.5f, 0.4f)]
        public void TestHitPositionLandsOnTheLineAndScrollTopOnTheScrollLength(float ratio, float scrollLength)
        {
            var p = create(ratio);
            var grid = new TrackGrid(p, 0, 1, 0, scrollLength, texture);

            grid.GetCell(p, 0, 0, out var nearest, out var nearestCoords, out _);
            grid.GetCell(p, grid.Strips - 1, 0, out var farthest, out var farthestCoords, out _);

            // The texture's bottom row (the hit position) is drawn on the judgement line...
            Assert.That(nearest.BottomLeft.Y, Is.EqualTo(p.YAt(0)).Within(tolerance));
            Assert.That(nearestCoords.Bottom, Is.EqualTo(texture.Bottom).Within(tolerance));

            // ...and its top row (where notes enter) at the scroll length.
            Assert.That(farthest.TopLeft.Y, Is.EqualTo(p.YAt(scrollLength)).Within(tolerance));
            Assert.That(farthestCoords.Top, Is.EqualTo(texture.Top).Within(tolerance));
        }

        [TestCase(1f)]
        [TestCase(0.24f)]
        public void TestCellsMeetWithoutGaps(float ratio)
        {
            var p = create(ratio);
            var grid = new TrackGrid(p, 0, 1, 0, 1, texture);

            Assert.That(grid.Strips, Is.GreaterThan(1));

            for (int s = 0; s < grid.Strips; s++)
            {
                for (int c = 0; c < grid.Columns; c++)
                {
                    grid.GetCell(p, s, c, out var quad, out var coords, out _);

                    if (s + 1 < grid.Strips)
                    {
                        grid.GetCell(p, s + 1, c, out var farther, out var fartherCoords, out _);
                        assertNear(quad.TopLeft, farther.BottomLeft);
                        assertNear(quad.TopRight, farther.BottomRight);
                        Assert.That(coords.Top, Is.EqualTo(fartherCoords.Bottom).Within(tolerance));
                    }

                    if (c + 1 < grid.Columns)
                    {
                        grid.GetCell(p, s, c + 1, out var right, out var rightCoords, out _);
                        assertNear(quad.TopRight, right.TopLeft);
                        assertNear(quad.BottomRight, right.BottomLeft);
                        Assert.That(coords.Right, Is.EqualTo(rightCoords.Left).Within(tolerance));
                    }
                }
            }
        }

        [Test]
        public void TestPerspectiveIsSubdividedFinelyEnough()
        {
            var p = create(0.24f);
            var grid = new TrackGrid(p, 0, 1, 0, 1, texture);

            Assert.That(grid.Columns, Is.GreaterThan(1), "a strongly converging track needs columns, or textures bend");

            float stripHeight = (p.YAt(0) - p.YAt(1)) / grid.Strips;
            Assert.That(stripHeight, Is.LessThanOrEqualTo(TrackDrawing.STRIP_HEIGHT + tolerance));
        }

        [Test]
        public void TestClampedNearEdgeKeepsTheTextureMapping()
        {
            var p = create(0.24f);

            // Starts behind the camera: the drawn part begins at the near plane, showing the same texels it would unclamped.
            const float full_depth0 = -10;
            var grid = new TrackGrid(p, 0, 1, full_depth0, 1, texture);

            Assert.That(grid.Visible, Is.True);

            grid.GetCell(p, 0, 0, out var nearest, out var coords, out _);

            float expectedRow = texture.Bottom - texture.Height * (p.MinDepth - full_depth0) / (1 - full_depth0);

            Assert.That(nearest.BottomLeft.Y, Is.EqualTo(p.YAt(p.MinDepth)).Within(tolerance));
            Assert.That(coords.Bottom, Is.EqualTo(expectedRow).Within(tolerance));
        }

        [Test]
        public void TestNothingBehindTheCameraIsDrawn()
        {
            var p = create(0.24f);

            Assert.That(new TrackGrid(p, 0, 1, -10, p.MinDepth - 0.01f, texture).Visible, Is.False);
            Assert.That(new TrackGrid(p, 0, 1, 0.5f, 0.5f, texture).Visible, Is.False);
            Assert.That(new TrackGrid(p, 0, 1, 0.5f, 0.5f, texture).Strips, Is.Zero);
        }

        private static void assertNear(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance));
        }
    }
}
