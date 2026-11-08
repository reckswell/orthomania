// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Primitives;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// Maps a point on the track to the screen. Pure maths, no game state (SPEC section 1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Track space: <c>lane</c> runs across the track (0 = left edge of the first lane, 1 = right edge of the last lane),
    /// <c>depth</c> runs along it (0 = judgement line, 1 = far edge). Notes move linearly in depth over time, exactly as
    /// mania moves them along a column.
    /// </para>
    /// <para>
    /// The track is a rectangle on a plane seen by a pinhole camera, so its image is an isosceles trapezoid and the mapping
    /// is the plane homography fixed by the trapezoid's corners. With <c>r</c> the far/near width ratio and
    /// <c>c = 1/r - 1</c>, a point at depth <c>d</c> is drawn at width scale <c>s = 1 / (1 + c·d)</c> and at
    /// <c>y = yJudgement + (yFar - yJudgement) · d(1 + c) / (1 + c·d)</c>. With <c>r = 1</c> (c = 0) this is a plain linear
    /// mapping: parallel lanes, constant on-screen speed.
    /// </para>
    /// <para>
    /// <see cref="HighwayLayout.Approach"/> moves where along the track each depth is drawn (<see cref="ApproachCurve"/>): the
    /// track keeps its shape, depth 0 stays on the line and depth 1 at the far edge, and every depth above is the eased one.
    /// Everything placed by depth (notes, holds, bar lines, the wall, the warped stage) follows it.
    /// </para>
    /// </remarks>
    public readonly struct HighwayProjection
    {
        /// <summary>
        /// Points closer to the camera plane than this (in units of 1/c) are treated as behind the camera.
        /// </summary>
        private const float near_plane_margin = 0.02f;

        public readonly HighwayLayout Layout;

        /// <summary>
        /// The size of the area being projected into, in pixels.
        /// </summary>
        public readonly Vector2 Size;

        /// <summary>
        /// Depth compression, <c>1/r - 1</c>. 0 for parallel lanes.
        /// </summary>
        public readonly float Compression;

        private readonly float judgementY;
        private readonly float farY;
        private readonly float nearWidth;
        private readonly float centreX;
        private readonly float sway;
        private readonly float approach;

        public HighwayProjection(HighwayLayout layout, Vector2 size)
        {
            Layout = layout.Sanitised();
            Size = size;

            Compression = 1 / Layout.FarWidthRatio - 1;
            judgementY = Layout.JudgementY * size.Y;
            farY = Layout.FarY * size.Y;
            nearWidth = Layout.NearWidth * size.X;
            centreX = Layout.CentreX * size.X;
            sway = (Layout.Sway + Layout.Lean) * size.X;
            approach = Layout.Approach;
        }

        /// <summary>
        /// The smallest depth that is still in front of the camera. Anything nearer must be culled or clamped.
        /// Only relevant below the judgement line (negative depth).
        /// </summary>
        public float MinDepth => Compression <= 0
            ? float.NegativeInfinity
            : DepthAtPerspectiveDepth(-(1 - near_plane_margin) / Compression);

        /// <summary>
        /// Where true perspective (linear in depth) puts what <see cref="HighwayLayout.Approach"/> draws at track depth
        /// <paramref name="depth"/>: the depth itself without an approach curve.
        /// </summary>
        public float PerspectiveDepthAt(float depth) => ApproachCurve.Ease(depth, approach, Compression);

        /// <summary>
        /// The inverse of <see cref="PerspectiveDepthAt"/>.
        /// </summary>
        public float DepthAtPerspectiveDepth(float perspectiveDepth) => ApproachCurve.Unease(perspectiveDepth, approach, Compression);

        /// <summary>
        /// The width scale at <paramref name="depth"/>, relative to the judgement line (1 at the line, <see cref="HighwayLayout.FarWidthRatio"/> at the far edge).
        /// </summary>
        public float ScaleAt(float depth) => 1 / (1 + Compression * ApproachCurve.Ease(depth, approach, Compression));

        /// <summary>
        /// The track's width in pixels at <paramref name="depth"/>.
        /// </summary>
        public float WidthAt(float depth) => nearWidth * ScaleAt(depth);

        /// <summary>
        /// The screen y (pixels, down from the top) of the track at <paramref name="depth"/>.
        /// </summary>
        public float YAt(float depth) => judgementY + (farY - judgementY) * ShareAt(depth);

        /// <summary>
        /// The inverse of <see cref="YAt"/>: the depth of the track drawn at screen y <paramref name="y"/>.
        /// </summary>
        public float DepthAtY(float y) => DepthAtShare((y - judgementY) / (farY - judgementY));

        /// <summary>
        /// How far up from the judgement line <paramref name="depth"/> is drawn, as a share of the height from the line to the
        /// far edge on screen (0 at the line, 1 at the far edge). Needs no screen size.
        /// </summary>
        public float ShareAt(float depth) => ShareAt(depth, Compression, approach);

        /// <summary>
        /// The inverse of <see cref="ShareAt(float)"/>: the depth drawn at <paramref name="share"/> of the height from the line to
        /// the far edge.
        /// </summary>
        public float DepthAtShare(float share) => DepthAtShare(share, Compression, approach);

        /// <summary>
        /// <see cref="ShareAt(float)"/> for a camera given by its <paramref name="compression"/> (<c>1/r - 1</c>) and
        /// <paramref name="approach"/>, without a projection.
        /// </summary>
        public static float ShareAt(float depth, float compression, float approach) => shareAt(ApproachCurve.Ease(depth, approach, compression), compression);

        /// <summary>
        /// The share of the line-to-far-edge height at which <paramref name="perspectiveDepth"/> (already eased) is drawn.
        /// </summary>
        private static float shareAt(float perspectiveDepth, float compression) => perspectiveDepth * (1 + compression) / (1 + compression * perspectiveDepth);

        /// <summary>
        /// <see cref="DepthAtShare(float)"/> for a camera given by its <paramref name="compression"/> and <paramref name="approach"/>.
        /// </summary>
        public static float DepthAtShare(float share, float compression, float approach)
            => ApproachCurve.Unease(share / (1 + compression - compression * share), approach, compression);

        /// <summary>
        /// The track's width in pixels at screen y <paramref name="y"/>. Lane edges are straight lines through the vanishing
        /// point, so this also holds below the judgement line, where the track continues on screen.
        /// </summary>
        public float WidthAtY(float y) => WidthAt(DepthAtY(y));

        /// <summary>
        /// The screen x (pixels) of <paramref name="lane"/> at screen y <paramref name="y"/>.
        /// </summary>
        public float XAt(float lane, float y) => centreX + SwayAtY(y) + (lane - 0.5f) * WidthAtY(y);

        /// <summary>
        /// How far <see cref="HighwayLayout.Sway"/> and <see cref="HighwayLayout.Lean"/> move the track sideways at screen y
        /// <paramref name="y"/>, in pixels: in proportion to the height above the judgement line (all of it at the far edge),
        /// nothing on or below it.
        /// </summary>
        public float SwayAtY(float y) => y >= judgementY ? 0 : sway * (judgementY - y) / (judgementY - farY);

        /// <summary>
        /// The screen position (pixels) of the track point (<paramref name="lane"/>, <paramref name="depth"/>).
        /// </summary>
        public Vector2 ToScreen(float lane, float depth)
        {
            // Eased once for both the height and the width, each worked out as YAt and WidthAt do.
            float perspectiveDepth = ApproachCurve.Ease(depth, approach, Compression);
            float y = judgementY + (farY - judgementY) * shareAt(perspectiveDepth, Compression);
            float width = nearWidth * (1 / (1 + Compression * perspectiveDepth));

            return new Vector2(centreX + SwayAtY(y) + (lane - 0.5f) * width, y);
        }

        /// <summary>
        /// The screen quad of the track rectangle spanning lanes <paramref name="lane0"/>..<paramref name="lane1"/> and
        /// depths <paramref name="depth0"/> (near) .. <paramref name="depth1"/> (far).
        /// Straight edges stay straight under a homography (and under the sway's shear, on or above the judgement line), so the
        /// four projected corners describe the whole rectangle.
        /// </summary>
        public Quad ToScreen(float lane0, float lane1, float depth0, float depth1)
            => new Quad(ToScreen(lane0, depth1), ToScreen(lane1, depth1), ToScreen(lane0, depth0), ToScreen(lane1, depth0));

        /// <summary>
        /// Where lanes converge on screen, or <c>null</c> when they are parallel.
        /// </summary>
        public Vector2? VanishingPoint
        {
            get
            {
                if (Compression <= 0)
                    return null;

                float y = judgementY + (farY - judgementY) * (1 + Compression) / Compression;
                return new Vector2(centreX + SwayAtY(y), y);
            }
        }
    }
}
