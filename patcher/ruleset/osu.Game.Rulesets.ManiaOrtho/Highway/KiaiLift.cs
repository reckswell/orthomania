// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Beatmaps.ControlPoints;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// The optional cosmetic lift in kiai sections (<see cref="HighwaySettings.KiaiLift"/>): the lane pitches up about the
    /// judgement line, its far end rising towards the top of the screen, as SDVX's charts tilt the lane up at a chorus
    /// (research/topic-camera-motion.md C1). It ramps in over three beats from a section's start, holds, and ramps out over
    /// three beats after its end: SDVX's median ramp, clamped so very slow or fast songs neither drift nor jolt.
    /// </summary>
    public class KiaiLift
    {
        public const int RAMP_BEATS = 3;

        public const double MIN_RAMP = 500;
        public const double MAX_RAMP = 1500;

        private readonly KiaiSections sections;

        public KiaiLift(ControlPointInfo controlPoints)
        {
            sections = new KiaiSections(controlPoints, time => RampLength(controlPoints.TimingPointAt(time).BeatLength));
        }

        /// <summary>
        /// How many kiai sections lift the lane; sections closer together than a ramp count as one.
        /// </summary>
        public int SectionCount => sections.Count;

        /// <summary>
        /// One ramp for a beat of <paramref name="beatLength"/>: <see cref="RAMP_BEATS"/> beats, within
        /// <see cref="MIN_RAMP"/>..<see cref="MAX_RAMP"/> milliseconds.
        /// </summary>
        public static double RampLength(double beatLength)
        {
            double beat = beatLength > 0 && double.IsFinite(beatLength) ? beatLength : TimingControlPoint.DEFAULT_BEAT_LENGTH;
            return Math.Clamp(RAMP_BEATS * beat, MIN_RAMP, MAX_RAMP);
        }

        /// <summary>
        /// How far the lane is lifted at <paramref name="time"/>, from 0 (at rest) to 1 (the full <see cref="HighwaySettings.KiaiLift"/>).
        /// </summary>
        public float At(double time) => (float)sections.At(time);
    }
}
