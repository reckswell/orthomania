// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// The lane as a physical plane seen by a camera: how far it leans back (<see cref="Tilt"/>) and how long it is
    /// (<see cref="LaneLength"/>). Turns those into where the lane ends on screen and how narrow its far end is, the two
    /// values <see cref="HighwayLayout"/> needs besides the judgement line and the track width (research/topic-approach-angles.md).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The camera is a pinhole with a fixed vertical field of view (<see cref="FIELD_OF_VIEW"/>), aimed through the centre of
    /// the screen, so a degree of tilt means the same in every preset. The lane lies on a plane through the judgement line,
    /// <see cref="Tilt"/> degrees from the screen: 0 stands it upright facing the player (parallel lanes, steady speed), 90
    /// would lay it flat along the view. Tilting pivots about the judgement line, so the line never moves (SDVX's
    /// <c>zoom_top</c>, research/topic-camera-motion.md).
    /// </para>
    /// <para>
    /// Lengths are in screen heights at the judgement line's distance, so the lane's shape does not depend on its width: the
    /// track width and the key count only change how wide it is. A lane whose far end would rise above the top of the screen
    /// is cut at the top edge, so notes always enter at the edge or below and the visible time stays the speed setting.
    /// </para>
    /// </remarks>
    public readonly record struct HighwayCamera
    {
        /// <summary>
        /// The camera's vertical field of view, in degrees: USC's landscape camera (research/game-usc.md). Every preset shares
        /// it, so tilts compare across games.
        /// </summary>
        public const float FIELD_OF_VIEW = 60;

        public const float MIN_TILT = 0;
        public const float MAX_TILT = 85;

        /// <summary>
        /// The shortest lane: short enough for a weak perspective (far end 3/4 as wide as the line at the steepest tilt).
        /// </summary>
        public const float MIN_LANE_LENGTH = 0.25f;

        public const float MAX_LANE_LENGTH = 16;

        /// <summary>
        /// The focal length in screen heights.
        /// </summary>
        private static readonly double focal_length = 0.5 / Math.Tan(FIELD_OF_VIEW / 2 * Math.PI / 180);

        /// <summary>
        /// How far the lane leans back from the screen, in degrees, pivoting about the judgement line.
        /// </summary>
        public float Tilt { get; init; }

        /// <summary>
        /// How long the lane is, in screen heights at the judgement line's distance (the length it would have on screen
        /// standing upright).
        /// </summary>
        public float LaneLength { get; init; }

        public HighwayCamera(float tilt, float laneLength)
        {
            Tilt = tilt;
            LaneLength = laneLength;
        }

        /// <summary>
        /// Where the lane ends on screen (<see cref="HighwayLayout.FarY"/>, a fraction of the height from the top) and how
        /// wide its far end is next to the judgement line (<see cref="HighwayLayout.FarWidthRatio"/>).
        /// </summary>
        /// <param name="judgementY">The judgement line's height, as a fraction of the screen height from the top.</param>
        /// <param name="perspective">False for a parallel (orthographic) camera: lanes stay parallel, nothing shows how long
        /// the lane is, and only the tilt counts. A lane one screen height long (<see cref="LaneLength"/> is ignored) is
        /// <c>cos(tilt)</c> of the screen tall above the line, so it reaches the top edge upright and shortens as it leans
        /// back.</param>
        public (float FarY, float FarWidthRatio) Project(float judgementY, bool perspective = true)
        {
            double tilt = Math.Clamp(float.IsFinite(Tilt) ? Tilt : 0, MIN_TILT, MAX_TILT) * Math.PI / 180;
            double length = Math.Clamp(float.IsFinite(LaneLength) ? LaneLength : MIN_LANE_LENGTH, MIN_LANE_LENGTH, MAX_LANE_LENGTH);
            double cos = Math.Cos(tilt);
            double sin = Math.Sin(tilt);

            if (!perspective)
                return ((float)Math.Max(0, judgementY - cos), 1);

            double f = focal_length;
            double below = judgementY - 0.5;

            // Screen y of the point `s` along the lane: 0.5 + f (below - s cos) / (f + s sin).
            double yAt(double s) => 0.5 + f * (below - s * cos) / (f + s * sin);

            double end = length;

            // Cut the lane at the top edge. It can only cross it when the lanes converge above the top of the screen.
            if (yAt(end) < 0)
                end = f * (below + 0.5) / (f * cos - 0.5 * sin);

            return ((float)Math.Max(0, yAt(end)), (float)(f / (f + end * sin)));
        }

        /// <summary>
        /// The camera that draws a lane ending at <paramref name="farY"/> with a far end <paramref name="farWidthRatio"/> as
        /// wide as the judgement line. Used to turn measured screen geometry (and settings saved before tilt existed) into a
        /// tilt and a length.
        /// </summary>
        /// <param name="judgementY">The judgement line's height, as a fraction of the screen height from the top.</param>
        /// <param name="farY">Where the lane ends, as a fraction of the screen height from the top.</param>
        /// <param name="farWidthRatio">The far end's width next to the judgement line's.</param>
        /// <param name="perspective">Whether the camera is a perspective one (<see cref="Project"/>).</param>
        public static HighwayCamera FromLayout(float judgementY, float farY, float farWidthRatio, bool perspective = true)
        {
            float span = Math.Max(0.01f, judgementY - farY);

            // Parallel lanes under a parallel camera: the tilt that makes its lane as tall as the span; upright if it reaches
            // the top.
            if (!perspective)
                return new HighwayCamera(farY <= 0 ? 0 : (float)(Math.Acos(Math.Min(1, span)) * 180 / Math.PI), 1);

            // Parallel lanes under a perspective camera: an upright lane as long as the span.
            if (farWidthRatio >= 1 - 1e-4f)
                return new HighwayCamera(0, Math.Clamp(span, MIN_LANE_LENGTH, MAX_LANE_LENGTH));

            double c = 1 / Math.Clamp(farWidthRatio, HighwayLayout.MIN_FAR_WIDTH_RATIO, 1) - 1;

            // Where the lanes converge, then the tilt that puts the horizon there and the length that narrows them to the ratio.
            double vanishingY = judgementY - span * (1 + c) / c;
            double tilt = Math.Atan2(focal_length, 0.5 - vanishingY);
            double length = focal_length * c / Math.Sin(tilt);

            return new HighwayCamera((float)(tilt * 180 / Math.PI), (float)length);
        }
    }
}
