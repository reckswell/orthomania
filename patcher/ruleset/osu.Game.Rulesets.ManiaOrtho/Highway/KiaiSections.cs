// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Beatmaps.ControlPoints;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// A beatmap's kiai sections as a smooth envelope for the cosmetic kiai effects (<see cref="KiaiSway"/>,
    /// <see cref="KiaiLift"/>): 0 outside them, rising to 1 over a fade from each section's start and falling back over a fade
    /// after its end. A pure function of time, so a seek or a rewind shows the same pose as playing through.
    /// </summary>
    public class KiaiSections
    {
        // Sections closer than their fade are merged into one.
        private readonly double[] starts;
        private readonly double[] ends;
        private readonly double[] fadeIns;
        private readonly double[] fadeOuts;

        /// <param name="controlPoints">The beatmap's control points.</param>
        /// <param name="fadeAt">How long the fade is at a time, in milliseconds (positive).</param>
        public KiaiSections(ControlPointInfo controlPoints, Func<double, double> fadeAt)
        {
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
                if (sectionEnds.Count > 0 && point.Time - sectionEnds[^1] < fadeAt(sectionEnds[^1]))
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
                fadeIns[i] = fadeAt(starts[i]);
                fadeOuts[i] = double.IsFinite(ends[i]) ? fadeAt(ends[i]) : 1;
            }
        }

        /// <summary>
        /// How many sections there are: consecutive kiai effect points, and sections closer together than their fade, count
        /// as one.
        /// </summary>
        public int Count => starts.Length;

        /// <summary>
        /// The envelope at <paramref name="time"/>, from 0 (outside kiai) to 1 (inside a section, past its fade), eased in and
        /// out (smoothstep, so the motion never jumps in speed).
        /// </summary>
        public double At(double time)
        {
            if (starts.Length == 0 || double.IsNaN(time))
                return 0;

            // The last section starting at or before this time.
            int index = FloorIndex(starts, time);

            if (index < 0)
                return 0;

            double grown = (time - starts[index]) / fadeIns[index];
            double settled = time < ends[index] ? 0 : (time - ends[index]) / fadeOuts[index];

            return smoothStep(Math.Clamp(Math.Min(grown, 1 - settled), 0, 1));
        }

        /// <summary>
        /// The index of the last of <paramref name="times"/> (sorted) at or before <paramref name="time"/>; -1 if none.
        /// </summary>
        public static int FloorIndex(double[] times, double time)
        {
            int index = Array.BinarySearch(times, time);
            return index >= 0 ? index : ~index - 1;
        }

        private static double smoothStep(double x) => x * x * (3 - 2 * x);
    }
}
