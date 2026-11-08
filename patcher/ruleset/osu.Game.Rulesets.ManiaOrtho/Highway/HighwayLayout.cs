// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// Where the highway sits on screen, in fractions of the playfield's draw size (y measured down from the top).
    /// This is everything <see cref="HighwayProjection"/> needs; it holds no game state.
    /// </summary>
    public readonly record struct HighwayLayout
    {
        /// <summary>
        /// The judgement line's height, as a fraction of the playfield height from the top (0.88 = 12% above the bottom).
        /// </summary>
        public float JudgementY { get; init; }

        /// <summary>
        /// The far edge of the track (where notes first appear when no wall is set), as a fraction of the playfield height
        /// from the top. Must be above <see cref="JudgementY"/>.
        /// </summary>
        public float FarY { get; init; }

        /// <summary>
        /// The track's width at the judgement line, as a fraction of the playfield width.
        /// </summary>
        public float NearWidth { get; init; }

        /// <summary>
        /// The track's width at the far edge divided by its width at the judgement line, in (0, 1].
        /// 1 means lanes stay parallel (no perspective); smaller values mean a stronger perspective.
        /// </summary>
        public float FarWidthRatio { get; init; }

        /// <summary>
        /// The horizontal centre of the track, as a fraction of the playfield width.
        /// </summary>
        public float CentreX { get; init; }

        /// <summary>
        /// How far the track above the judgement line leans sideways: the far edge's shift as a fraction of the playfield
        /// width, positive to the right. A shear in screen space about the judgement line, so the line, everything on it and
        /// everything below it stay where they are, and lanes stay straight. 0 (the default) is the camera at rest.
        /// </summary>
        public float Sway { get; init; }

        /// <summary>
        /// The largest <see cref="Sway"/> either way.
        /// </summary>
        public const float MAX_SWAY = 0.2f;

        /// <summary>
        /// A lasting lean of the track above the judgement line, added to <see cref="Sway"/>: the far edge's shift as a
        /// fraction of the playfield width, positive to the right. It moves where the lanes converge sideways (see
        /// <see cref="LeanTowards"/>) and, like the sway, leaves the judgement line and everything below it in place.
        /// </summary>
        public float Lean { get; init; }

        /// <summary>
        /// How notes travel down the track (<see cref="ApproachCurve"/>): from <see cref="ApproachCurve.PERSPECTIVE"/> (-1, the
        /// default: true perspective, linear in depth) through <see cref="ApproachCurve.SEKAI"/> (0, Project Sekai's even growth)
        /// to <see cref="ApproachCurve.EVEN"/> (1, a steady speed on screen). The track's shape does not change, only where along
        /// it each depth is drawn.
        /// </summary>
        public float Approach { get; init; }

        /// <summary>
        /// A copy leaning so that the lanes converge above <paramref name="x"/> (a fraction of the playfield width) instead of
        /// above <see cref="CentreX"/>. Lanes that stay parallel have nowhere to converge, so they do not lean.
        /// </summary>
        /// <remarks>
        /// The lanes meet at height <c>(1 + c) / c</c> times the track's height above the line (<c>c = 1/r - 1</c>), so a
        /// shift of <c>d</c> there is a shift of <c>d · c / (1 + c) = d · (1 - r)</c> at the far edge.
        /// </remarks>
        public HighwayLayout LeanTowards(float x) => this with { Lean = (x - CentreX) * (1 - Math.Clamp(FarWidthRatio, MIN_FAR_WIDTH_RATIO, 1f)) };

        /// <summary>
        /// A copy with the far edge raised by <paramref name="lift"/> (0 to 1) of its distance below the top edge, as SDVX's
        /// charts pitch the lane up at a chorus (research/topic-camera-motion.md C1). The far end widens with the lane's height
        /// on screen, so notes reach the judgement line at the same speed under the Perspective approach (the near end's look
        /// is kept; an eased <see cref="Approach"/> changes that speed a little, Even by the lane's change in height), and the lanes still
        /// converge above the same point. The judgement line and the near width stay put. A lane already reaching the top does
        /// not move.
        /// </summary>
        public HighwayLayout Lifted(float lift)
        {
            float span = JudgementY - FarY;

            if (!(lift > 0) || FarY <= 0 || span <= 0)
                return this;

            float farY = FarY * (1 - Math.Min(lift, 1));
            float ratio = Math.Clamp(FarWidthRatio, MIN_FAR_WIDTH_RATIO, 1f);
            float liftedRatio = Math.Min(1, ratio * (JudgementY - farY) / span);

            // A lean converges above x = CentreX + Lean / (1 - r) (LeanTowards); keep it there.
            float lean = ratio < 1 ? Lean * (1 - liftedRatio) / (1 - ratio) : Lean;

            return this with { FarY = farY, FarWidthRatio = liftedRatio, Lean = lean };
        }

        public HighwayLayout()
        {
            JudgementY = 0.88f;
            FarY = 0;
            NearWidth = 0.5f;
            FarWidthRatio = 0.5f;
            CentreX = 0.5f;
            Approach = ApproachCurve.PERSPECTIVE;
        }

        /// <summary>
        /// The narrowest supported <see cref="NearWidth"/>.
        /// </summary>
        public const float MIN_NEAR_WIDTH = 0.05f;

        /// <summary>
        /// The smallest supported <see cref="FarWidthRatio"/>. Below this the far edge is close to a point and far notes
        /// become unreadable.
        /// </summary>
        public const float MIN_FAR_WIDTH_RATIO = 0.05f;

        /// <summary>
        /// Returns a copy with every value clamped to a range the projection can handle.
        /// </summary>
        public HighwayLayout Sanitised()
        {
            float judgementY = Math.Clamp(JudgementY, 0.05f, 1f);

            return this with
            {
                JudgementY = judgementY,
                FarY = Math.Clamp(FarY, 0f, judgementY - 0.05f),
                NearWidth = Math.Clamp(NearWidth, MIN_NEAR_WIDTH, 1f),
                FarWidthRatio = Math.Clamp(FarWidthRatio, MIN_FAR_WIDTH_RATIO, 1f),
                CentreX = Math.Clamp(CentreX, 0f, 1f),
                Sway = float.IsFinite(Sway) ? Math.Clamp(Sway, -MAX_SWAY, MAX_SWAY) : 0,
                Lean = float.IsFinite(Lean) ? Math.Clamp(Lean, -1f, 1f) : 0,
                Approach = float.IsFinite(Approach) ? Math.Clamp(Approach, ApproachCurve.PERSPECTIVE, ApproachCurve.EVEN) : ApproachCurve.PERSPECTIVE,
            };
        }
    }
}
