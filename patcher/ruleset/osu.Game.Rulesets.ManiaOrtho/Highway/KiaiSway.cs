// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
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

        // Kiai sections, with sections closer than their fade merged into one; each grows over one swing from its start
        // and settles over one swing after its end.
        private readonly double[] starts;
        private readonly double[] ends;
        private readonly double[] fadeIns;
        private readonly double[] fadeOuts;

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

            var sectionStarts = new List<double>();
            var sectionEnds = new List<double>();
            bool inKiai = false;

            // Consecutive kiai effect points (a scroll speed change inside a kiai section) make one section.
            foreach (var point in controlPoints.EffectPoints)
            {
                if (point.KiaiMode == inKiai)
                    continue;

                inKiai = point.KiaiMode;

                if (!inKiai)
                {
                    sectionEnds.Add(point.Time);
                    continue;
                }

                // A section starting before the last one has settled carries on from it.
                if (sectionEnds.Count > 0 && point.Time - sectionEnds[^1] < swingAt(sectionEnds[^1]))
                    sectionEnds.RemoveAt(sectionEnds.Count - 1);
                else
                    sectionStarts.Add(point.Time);
            }

            if (inKiai)
                sectionEnds.Add(double.PositiveInfinity);

            starts = sectionStarts.ToArray();
            ends = sectionEnds.ToArray();
            fadeIns = new double[starts.Length];
            fadeOuts = new double[starts.Length];

            for (int i = 0; i < starts.Length; i++)
            {
                fadeIns[i] = swingAt(starts[i]);
                fadeOuts[i] = double.IsFinite(ends[i]) ? swingAt(ends[i]) : 1;
            }
        }

        /// <summary>
        /// How many kiai sections the beatmap has: consecutive kiai effect points, and sections closer together than their
        /// fade, count as one.
        /// </summary>
        public int SectionCount => starts.Length;

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
            if (starts.Length == 0 || double.IsNaN(time))
                return 0;

            // The last section starting at or before this time.
            int index = floorIndex(starts, time);

            if (index < 0)
                return 0;

            double grown = (time - starts[index]) / fadeIns[index];
            double settled = time < ends[index] ? 0 : (time - ends[index]) / fadeOuts[index];
            double envelope = smoothStep(Math.Clamp(Math.Min(grown, 1 - settled), 0, 1));

            if (envelope <= 0)
                return 0;

            // At a side on each beat (each swing) and through the middle between them, counted on from the first timing point.
            int timingIndex = Math.Max(0, floorIndex(timingTimes, time));
            double swings = swingsBefore[timingIndex] + (time - timingTimes[timingIndex]) / swingLengths[timingIndex];

            return (float)(envelope * Math.Cos(Math.PI * swings));
        }

        private double swingAt(double time) => swingLengths[Math.Max(0, floorIndex(timingTimes, time))];

        /// <summary>
        /// The index of the last of <paramref name="times"/> (sorted) at or before <paramref name="time"/>; -1 if none.
        /// </summary>
        private static int floorIndex(double[] times, double time)
        {
            int index = Array.BinarySearch(times, time);
            return index >= 0 ? index : ~index - 1;
        }

        private static double smoothStep(double x) => x * x * (3 - 2 * x);
    }
}
