// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Beatmaps.ControlPoints;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// The optional cosmetic sway in kiai sections (SPEC 2): the far end of the track swings from side to side with the beat
    /// while the judgement line stays put. A pure function of time, so a seek or a rewind shows the same pose as playing
    /// through, and frames captured at the same time match.
    /// </summary>
    public class KiaiSway
    {
        /// <summary>
        /// The shortest swing (one side to the other), in milliseconds: one beat up to 200 BPM, the beat doubled until it is
        /// at least this long above it, so fast songs and gimmick timing never shake the track.
        /// </summary>
        public const double MIN_SWING = 300;

        // Each timing point's time, its swing length, and how many swings came before it (so the swing never jumps at a red line).
        private readonly double[] timingTimes;
        private readonly double[] swingLengths;
        private readonly double[] swingsBefore;

        // Kiai sections, each growing over one swing from its start and settling over one swing after its end.
        private readonly KiaiSections sections;

        public KiaiSway(ControlPointInfo controlPoints)
        {
            var timing = controlPoints.TimingPoints;
            int timingCount = Math.Max(1, timing.Count);

            timingTimes = new double[timingCount];
            swingLengths = new double[timingCount];
            swingsBefore = new double[timingCount];
            swingLengths[0] = SwingLength(timing.Count > 0 ? timing[0].BeatLength : TimingControlPoint.DEFAULT_BEAT_LENGTH);

            for (int i = 0; i < timing.Count; i++)
            {
                timingTimes[i] = timing[i].Time;
                swingLengths[i] = SwingLength(timing[i].BeatLength);

                if (i > 0)
                    swingsBefore[i] = swingsBefore[i - 1] + (timingTimes[i] - timingTimes[i - 1]) / swingLengths[i - 1];
            }

            sections = new KiaiSections(controlPoints, swingAt);
        }

        /// <summary>
        /// How many kiai sections the beatmap has: consecutive kiai effect points, and sections closer together than their
        /// fade, count as one.
        /// </summary>
        public int SectionCount => sections.Count;

        /// <summary>
        /// One swing (one side to the other) for a beat of <paramref name="beatLength"/>: the beat, doubled until it is at
        /// least <see cref="MIN_SWING"/>.
        /// </summary>
        public static double SwingLength(double beatLength)
        {
            double swing = beatLength > 0 && double.IsFinite(beatLength) ? beatLength : TimingControlPoint.DEFAULT_BEAT_LENGTH;

            while (swing < MIN_SWING)
                swing *= 2;

            return swing;
        }

        /// <summary>
        /// The sway at <paramref name="time"/>, from -1 (far end fully left) to 1 (fully right); 0 outside kiai sections.
        /// </summary>
        public float At(double time)
        {
            double envelope = sections.At(time);

            if (envelope <= 0)
                return 0;

            // At a side on each beat (each swing) and through the middle between them, counted on from the first timing point.
            int timingIndex = Math.Max(0, KiaiSections.FloorIndex(timingTimes, time));
            double swings = swingsBefore[timingIndex] + (time - timingTimes[timingIndex]) / swingLengths[timingIndex];

            return (float)(envelope * Math.Cos(Math.PI * swings));
        }

        private double swingAt(double time) => swingLengths[Math.Max(0, KiaiSections.FloorIndex(timingTimes, time))];
    }
}
