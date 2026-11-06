// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.SkinPort;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for the highway's track (research game-chunithm-feedback P5): how a lane beam fades in and out and how far
    /// it reaches, where the track continues below the judgement line, how track opacity reaches the lanes' background, and
    /// where a hit lights the line.
    /// </summary>
    [TestFixture]
    public class HighwayTrackTest
    {
        private const float tolerance = 1e-4f;

        [Test]
        public void TestBeamFadesInLinearly()
        {
            Assert.That(HighwayTrack.StepBeam(0, true, HighwayTrack.BEAM_FADE_IN / 2), Is.EqualTo(0.5f).Within(tolerance));
            Assert.That(HighwayTrack.StepBeam(0.5f, true, HighwayTrack.BEAM_FADE_IN / 2), Is.EqualTo(1).Within(tolerance));
            Assert.That(HighwayTrack.StepBeam(1, true, 1000), Is.EqualTo(1));
        }

        [Test]
        public void TestBeamFadesOutEasingOut()
        {
            // (1 - p)² at progress p, the shape of an OutQuad fade, whatever the frame steps.
            Assert.That(HighwayTrack.StepBeam(1, false, HighwayTrack.BEAM_FADE_OUT / 2), Is.EqualTo(0.25f).Within(tolerance));

            float level = 1;
            for (int i = 0; i < 15; i++)
                level = HighwayTrack.StepBeam(level, false, HighwayTrack.BEAM_FADE_OUT / 2 / 15);
            Assert.That(level, Is.EqualTo(0.25f).Within(tolerance));

            Assert.That(HighwayTrack.StepBeam(0.25f, false, HighwayTrack.BEAM_FADE_OUT / 2), Is.EqualTo(0).Within(tolerance));
            Assert.That(HighwayTrack.StepBeam(1, false, HighwayTrack.BEAM_FADE_OUT), Is.Zero);
        }

        [Test]
        public void TestBeamHoldsWhileTimeStands()
        {
            Assert.That(HighwayTrack.StepBeam(0.4f, true, 0), Is.EqualTo(0.4f));
            Assert.That(HighwayTrack.StepBeam(0.4f, false, 0), Is.EqualTo(0.4f).Within(tolerance));
        }

        [TestCase(-16.0)]
        [TestCase(double.NaN)]
        public void TestRewindSettlesTheBeam(double elapsed)
        {
            Assert.That(HighwayTrack.StepBeam(0.3f, true, elapsed), Is.EqualTo(1));
            Assert.That(HighwayTrack.StepBeam(0.3f, false, elapsed), Is.Zero);
        }

        [Test]
        public void TestRailsAreAShareOfTheirStage()
        {
            var single = new[] { new Vector2(0, 1) };
            HighwayTrack.GetRailWidths(single, 0, out float left, out float right);
            Assert.That(left, Is.EqualTo(HighwayTrack.RAIL_WIDTH).Within(tolerance));
            Assert.That(right, Is.EqualTo(HighwayTrack.RAIL_WIDTH).Within(tolerance));

            // Dual stages far apart: each rail is a sixteenth of its own stage.
            var apart = new[] { new Vector2(0, 0.4f), new Vector2(0.6f, 1) };
            HighwayTrack.GetRailWidths(apart, 1, out left, out right);
            Assert.That(left, Is.EqualTo(0.4f * HighwayTrack.RAIL_WIDTH).Within(tolerance));
            Assert.That(right, Is.EqualTo(0.4f * HighwayTrack.RAIL_WIDTH).Within(tolerance));
        }

        [Test]
        public void TestRailsNeverCoverTheNextStage()
        {
            var close = new[] { new Vector2(0, 0.495f), new Vector2(0.505f, 1) };

            HighwayTrack.GetRailWidths(close, 0, out float outer, out float inner0);
            HighwayTrack.GetRailWidths(close, 1, out float inner1, out _);

            Assert.That(outer, Is.EqualTo(0.495f * HighwayTrack.RAIL_WIDTH).Within(tolerance));
            Assert.That(inner0, Is.EqualTo(0.005f).Within(tolerance));
            Assert.That(close[0].Y + inner0, Is.LessThanOrEqualTo(close[1].X - inner1 + tolerance));

            var touching = new[] { new Vector2(0, 0.5f), new Vector2(0.5f, 1) };
            HighwayTrack.GetRailWidths(touching, 0, out _, out inner0);
            Assert.That(inner0, Is.Zero);
        }

        [TestCase(0f, 1f)]
        [TestCase(0.25f, 0.5f)]
        [TestCase(-1 / 16f, 0f)]
        public void TestBelowTheLineFollowsTheLaneEdges(float lane0, float lane1)
        {
            var p = new HighwayProjection(new HighwayLayout
            {
                JudgementY = 0.926f,
                FarY = 0,
                NearWidth = 0.61f,
                FarWidthRatio = 0.24f,
                CentreX = 0.5f,
            }, new Vector2(1920, 1080));

            float height = 1080 - p.YAt(0);
            var quad = TrackDrawing.GetBelowLine(p, lane0, lane1, height);

            Assert.That(Vector2.Distance(quad.TopLeft, p.ToScreen(lane0, 0)), Is.LessThan(1e-2f));
            Assert.That(Vector2.Distance(quad.TopRight, p.ToScreen(lane1, 0)), Is.LessThan(1e-2f));
            Assert.That(quad.BottomLeft.Y, Is.EqualTo(1080).Within(1e-2f));
            Assert.That(quad.BottomRight.Y, Is.EqualTo(1080).Within(1e-2f));

            // Each side continues the lane edge's straight line on the track.
            Assert.That(cross(p.ToScreen(lane0, 0.5f), p.ToScreen(lane0, 0), quad.BottomLeft), Is.LessThan(1e-3f));
            Assert.That(cross(p.ToScreen(lane1, 0.5f), p.ToScreen(lane1, 0), quad.BottomRight), Is.LessThan(1e-3f));
        }

        /// <summary>
        /// The band a lane lights at the judgement line (above it and below it) keeps to the lane's edges.
        /// </summary>
        [TestCase(0f, 1f)]
        [TestCase(3 / 7f, 4 / 7f)]
        public void TestBandAcrossLanesFollowsTheLaneEdges(float lane0, float lane1)
        {
            var p = new HighwayProjection(new HighwayLayout
            {
                JudgementY = 0.926f,
                FarY = 0,
                NearWidth = 0.61f,
                FarWidthRatio = 0.24f,
                CentreX = 0.5f,
            }, new Vector2(1920, 1080));

            float line = p.YAt(0);
            var quad = TrackDrawing.GetAcrossLanes(p, lane0, lane1, line - 30, line + 30);

            Assert.That(quad.TopLeft.Y, Is.EqualTo(line - 30).Within(1e-3f));
            Assert.That(quad.BottomRight.Y, Is.EqualTo(line + 30).Within(1e-3f));

            // Above the line its sides are the lane edges on the track; below it they continue them.
            float above = p.DepthAtY(line - 30);
            Assert.That(Vector2.Distance(quad.TopLeft, p.ToScreen(lane0, above)), Is.LessThan(1e-2f));
            Assert.That(Vector2.Distance(quad.TopRight, p.ToScreen(lane1, above)), Is.LessThan(1e-2f));
            Assert.That(cross(p.ToScreen(lane0, 0.5f), p.ToScreen(lane0, 0), quad.BottomLeft), Is.LessThan(1e-3f));
            Assert.That(cross(p.ToScreen(lane1, 0.5f), p.ToScreen(lane1, 0), quad.BottomRight), Is.LessThan(1e-3f));

            Assert.That(TrackDrawing.GetAcrossTrack(p, line - 30, line + 30), Is.EqualTo(TrackDrawing.GetAcrossLanes(p, 0, 1, line - 30, line + 30)));
        }

        /// <summary>
        /// Track opacity (research game-sekai.md K9) scales the highway floor from its designed 90%.
        /// </summary>
        [TestCase(1f, 0.9f)]
        [TestCase(0.5f, 0.45f)]
        [TestCase(0f, 0f)]
        [TestCase(2f, 0.9f)]
        [TestCase(-1f, 0f)]
        public void TestFloorOpacity(float opacity, float alpha)
        {
            var track = new HighwayTrack { FloorOpacity = opacity };

            Assert.That(track.FloorAlpha, Is.EqualTo(alpha).Within(tolerance));
            Assert.That(HighwayTrack.FloorColour(track.FloorAlpha, 0.5f).A, Is.EqualTo(alpha / 2).Within(tolerance), "times the drawable's alpha");
            Assert.That(HighwayTrack.FloorColour(track.FloorAlpha, 1).R, Is.EqualTo(HighwayTrack.FLOOR_COLOUR.R));
        }

        /// <summary>
        /// ... and a ported skin's lane colours from the alpha stable gives them (the skin's alpha, squared).
        /// </summary>
        [Test]
        public void TestPortedLaneBackgroundOpacity()
        {
            var skinColour = new Color4(0.1f, 0.2f, 0.3f, 0.8f);

            Assert.That(PortedTrack.LaneBackground(skinColour, 1, 1).A, Is.EqualTo(0.64f).Within(tolerance));
            Assert.That(PortedTrack.LaneBackground(skinColour, 0.5f, 1).A, Is.EqualTo(0.32f).Within(tolerance));
            Assert.That(PortedTrack.LaneBackground(skinColour, 0.5f, 0.5f).A, Is.EqualTo(0.16f).Within(tolerance));
            Assert.That(PortedTrack.LaneBackground(skinColour, 0, 1).A, Is.Zero);
            Assert.That(PortedTrack.LaneBackground(skinColour, 0.5f, 1).B, Is.EqualTo(0.3f));
        }

        /// <summary>
        /// A hit lights its lane's piece of the judgement line (research game-sekai.md K7): a band centred on the line, at the
        /// camera's height or a ported skin's lifted one.
        /// </summary>
        [TestCase(0.926f)]
        [TestCase(0.65f)]
        public void TestSlotBandsSitOnTheLine(float judgementY)
        {
            var p = new HighwayProjection(new HighwayLayout
            {
                JudgementY = judgementY,
                FarY = 0,
                NearWidth = 0.61f,
                FarWidthRatio = 0.24f,
                CentreX = 0.5f,
            }, new Vector2(1920, 1080));

            var lane = new Vector2(3 / 7f, 4 / 7f);
            HighwaySlotLights.GetBands(p, lane, out var above, out var solid, out var below);

            float line = p.YAt(0);
            float half = HighwaySlotLights.SOLID_HEIGHT * 1080 / 2;
            float fade = HighwaySlotLights.FADE_HEIGHT * 1080;

            Assert.That(solid.TopLeft.Y, Is.EqualTo(line - half).Within(1e-3f));
            Assert.That(solid.BottomLeft.Y, Is.EqualTo(line + half).Within(1e-3f));
            Assert.That(above.TopLeft.Y, Is.EqualTo(line - half - fade).Within(1e-3f));
            Assert.That(above.BottomRight, Is.EqualTo(solid.TopRight));
            Assert.That(below.TopLeft, Is.EqualTo(solid.BottomLeft));
            Assert.That(below.BottomLeft.Y, Is.EqualTo(line + half + fade).Within(1e-3f));

            // Its sides lie on the lane's edges, above the line and below it.
            Assert.That(cross(p.ToScreen(lane.X, 0.5f), p.ToScreen(lane.X, 0), solid.BottomLeft), Is.LessThan(1e-3f));
            Assert.That(cross(p.ToScreen(lane.Y, 0.5f), p.ToScreen(lane.Y, 0), solid.TopRight), Is.LessThan(1e-3f));
        }

        /// <summary>
        /// DJMAX's Half and Quarter beams (research game-djmax.md R2, whose table these depths come from): a share of the
        /// track's height on screen under every camera, where Long is half the track's depth.
        /// </summary>
        [TestCase(0.24f, KeyBeamLength.Half, 0.194f)] // Chunithm
        [TestCase(0.24f, KeyBeamLength.Quarter, 0.074f)]
        [TestCase(0.106f, KeyBeamLength.Half, 0.096f)] // Voltex
        [TestCase(0.106f, KeyBeamLength.Quarter, 0.034f)]
        [TestCase(1f, KeyBeamLength.Half, 0.5f)] // Parallel: depth is screen height, so Half is Long.
        [TestCase(1f, KeyBeamLength.Quarter, 0.25f)]
        [TestCase(0.24f, KeyBeamLength.Long, HighwayTrack.BEAM_LENGTH)]
        [TestCase(1f, KeyBeamLength.Long, HighwayTrack.BEAM_LENGTH)]
        public void TestBeamDepth(float farWidthRatio, KeyBeamLength length, float depth)
        {
            var p = new HighwayProjection(new HighwayLayout
            {
                JudgementY = 0.926f,
                FarY = 0,
                NearWidth = 0.61f,
                FarWidthRatio = farWidthRatio,
                CentreX = 0.5f,
            }, new Vector2(1920, 1080));

            Assert.That(HighwayTrack.GetBeamDepth(p, 1, length), Is.EqualTo(depth).Within(1e-3f));
        }

        /// <summary>
        /// ... measured on screen up to where the track ends, the wall's edge when there is one.
        /// </summary>
        [TestCase(1f, KeyBeamLength.Half, 0.5f)]
        [TestCase(1f, KeyBeamLength.Quarter, 0.25f)]
        [TestCase(0.6f, KeyBeamLength.Half, 0.5f)]
        [TestCase(0.6f, KeyBeamLength.Quarter, 0.25f)]
        public void TestBeamReachesItsShareOfTheTrackOnScreen(float farDepth, KeyBeamLength length, float share)
        {
            var p = new HighwayProjection(new HighwayLayout
            {
                JudgementY = 0.926f,
                FarY = 0.05f,
                NearWidth = 0.61f,
                FarWidthRatio = 0.24f,
                CentreX = 0.5f,
            }, new Vector2(1920, 1080));

            float line = p.YAt(0);
            float depth = HighwayTrack.GetBeamDepth(p, farDepth, length);

            Assert.That(line - p.YAt(depth), Is.EqualTo(share * (line - p.YAt(farDepth))).Within(1e-2f));
            Assert.That(depth, Is.LessThan(HighwayTrack.BEAM_LENGTH));
        }

        /// <summary>
        /// How far <paramref name="c"/> is from the line through <paramref name="a"/> and <paramref name="b"/>, in pixels.
        /// </summary>
        private static float cross(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            return System.Math.Abs(ab.X * ac.Y - ab.Y * ac.X) / ab.Length;
        }
    }
}
