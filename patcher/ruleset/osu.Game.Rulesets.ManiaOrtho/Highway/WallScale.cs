// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// The units the wall is set and shown in, after the lane covers of other games (research/topic-covers.md). The wall is
    /// always stored as a share of the track's depth (<see cref="HighwaySettings.Wall"/>); a scale only changes how it is
    /// set, stepped by the gameplay keys and shown.
    /// </summary>
    public enum WallScale
    {
        /// <summary>
        /// A share of the track's depth, 0-90%, in 5% steps from the gameplay keys.
        /// </summary>
        [Description("Percent of the track")]
        Percent,

        /// <summary>
        /// Chunithm's field wall steps, 0-14, each step the arcade's own wall position (research/game-chunithm.md). Notes
        /// keep their speed, as in the arcade.
        /// </summary>
        [Description("Chunithm (0-14)")]
        Chunithm,

        /// <summary>
        /// beatmania IIDX's SUDDEN+ white number, 0-1000: thousandths of the lane's height on screen, covered from the far end.
        /// Notes keep their visible time, as with IIDX's Floating Hi-Speed.
        /// </summary>
        [Description("IIDX white number (0-1000)")]
        Iidx,

        /// <summary>
        /// osu!mania's Cover and Quaver's lane cover: a share of the lane's height on screen, covered from the far end. Notes
        /// keep their speed, as in those games.
        /// </summary>
        [Description("osu!mania / Quaver (% of the lane)")]
        Lane,
    }

    /// <summary>
    /// Conversions between the stored wall (a share of the track's depth hidden from the far end) and each
    /// <see cref="WallScale"/>'s units.
    /// </summary>
    public static class WallScales
    {
        /// <summary>
        /// Chunithm's wall plane per step (z in arcade units, research/game-chunithm.md "Positions"); its hidden share of the
        /// visible track is (z + 460) / 395. Steps 15 and 16 reach the judgement line and beyond, past
        /// <see cref="HighwayScroll.MAX_WALL"/>.
        /// </summary>
        private static readonly double[] chunithm_z = { -460, -437, -414, -391, -368, -345, -322, -299, -276, -253, -230, -207, -179.4, -151.8, -110.4 };

        public const int MAX_CHUNITHM_STEP = 14;

        /// <summary>
        /// The wall at Chunithm's wall <paramref name="step"/> (clamped to 0-14).
        /// </summary>
        public static float ChunithmWall(int step) => (float)((chunithm_z[Math.Clamp(step, 0, MAX_CHUNITHM_STEP)] + 460) / 395);

        /// <summary>
        /// The Chunithm step nearest to <paramref name="wall"/>.
        /// </summary>
        public static int ChunithmStep(float wall)
        {
            int nearest = 0;

            for (int step = 1; step <= MAX_CHUNITHM_STEP; step++)
            {
                if (Math.Abs(ChunithmWall(step) - wall) < Math.Abs(ChunithmWall(nearest) - wall))
                    nearest = step;
            }

            return nearest;
        }

        /// <summary>
        /// The share of the lane's height on screen (from the far edge down to the judgement line) that <paramref name="wall"/>
        /// covers, under a camera whose far edge is <paramref name="farWidthRatio"/> of the near width (1 for parallel lanes).
        /// The inverse of <see cref="WallForScreenShare"/>; see <see cref="HighwayProjection.YAt"/>.
        /// </summary>
        public static float ScreenShare(float wall, float farWidthRatio)
        {
            float c = compression(farWidthRatio);
            float depth = 1 - wall;
            return 1 - depth * (1 + c) / (1 + c * depth);
        }

        /// <summary>
        /// The wall that covers <paramref name="share"/> of the lane's height on screen.
        /// </summary>
        public static float WallForScreenShare(float share, float farWidthRatio)
        {
            float c = compression(farWidthRatio);
            float t = 1 - share;
            return 1 - t / (1 + c - c * t);
        }

        /// <summary>
        /// Whether choosing <paramref name="scale"/> switches the wall to keep visible time (true) or speed (false), as that
        /// game does; null leaves it alone.
        /// </summary>
        public static bool? KeepsVisibleTime(WallScale scale)
        {
            switch (scale)
            {
                case WallScale.Chunithm:
                case WallScale.Lane:
                    return false;

                case WallScale.Iidx:
                    return true;

                default:
                    return null;
            }
        }

        /// <summary>
        /// <paramref name="wall"/> in <paramref name="scale"/>'s units: percent, a Chunithm step, a white number or a percent
        /// of the lane.
        /// </summary>
        public static int ToUnits(WallScale scale, float wall, float farWidthRatio)
        {
            switch (scale)
            {
                case WallScale.Chunithm:
                    return ChunithmStep(wall);

                case WallScale.Iidx:
                    return (int)Math.Round(ScreenShare(wall, farWidthRatio) * 1000);

                case WallScale.Lane:
                    return (int)Math.Round(ScreenShare(wall, farWidthRatio) * 100);

                default:
                    return (int)Math.Round(wall * 100);
            }
        }

        /// <summary>
        /// The wall at <paramref name="units"/> of <paramref name="scale"/>, within the wall's range.
        /// </summary>
        public static float FromUnits(WallScale scale, int units, float farWidthRatio)
        {
            float wall;

            switch (scale)
            {
                case WallScale.Chunithm:
                    wall = ChunithmWall(units);
                    break;

                case WallScale.Iidx:
                    wall = WallForScreenShare(units / 1000f, farWidthRatio);
                    break;

                case WallScale.Lane:
                    wall = WallForScreenShare(units / 100f, farWidthRatio);
                    break;

                default:
                    wall = units / 100f;
                    break;
            }

            return Math.Clamp(wall, 0, HighwayScroll.MAX_WALL);
        }

        /// <summary>
        /// The most <paramref name="scale"/>'s units can reach within the wall's range: the wall's most, as shown in them
        /// (<see cref="FromUnits"/> clamps the last unit to the range).
        /// </summary>
        public static int MaxUnits(WallScale scale, float farWidthRatio) => ToUnits(scale, HighwayScroll.MAX_WALL, farWidthRatio);

        /// <summary>
        /// How far one press of the gameplay keys (Shift with the scroll speed keys) moves the wall, in <paramref name="scale"/>'s units.
        /// </summary>
        public static int KeyStep(WallScale scale)
        {
            switch (scale)
            {
                case WallScale.Chunithm:
                    return 1;

                case WallScale.Iidx:
                    return 50;

                default:
                    return 5;
            }
        }

        /// <summary>
        /// How far from a unit the stored wall may be and still be on it: its precision, with float error.
        /// </summary>
        private const float on_unit_tolerance = 0.0001f;

        /// <summary>
        /// <paramref name="wall"/> moved <paramref name="steps"/> key steps (negative to lower it), in <paramref name="scale"/>'s units.
        /// A wall between two units counts from the one behind it, so one press never moves more than one step, and the wall
        /// never moves against the key (at the top of a scale whose last unit is below the wall's most, it stays).
        /// </summary>
        public static float Step(WallScale scale, float wall, int steps, float farWidthRatio)
        {
            if (steps == 0)
                return wall;

            int nearest = ToUnits(scale, wall, farWidthRatio);
            float atNearest = FromUnits(scale, nearest, farWidthRatio);
            int from = nearest;

            if (Math.Abs(atNearest - wall) > on_unit_tolerance)
            {
                // Between units: start from the one below the wall when raising it, above when lowering it.
                if (steps > 0 && atNearest > wall)
                    from = nearest - 1;
                else if (steps < 0 && atNearest < wall)
                    from = nearest + 1;
            }

            int units = Math.Clamp(from + steps * KeyStep(scale), 0, MaxUnits(scale, farWidthRatio));
            float next = FromUnits(scale, units, farWidthRatio);

            return steps > 0 ? Math.Max(next, wall) : Math.Min(next, wall);
        }

        /// <summary>
        /// <paramref name="wall"/> in <paramref name="scale"/>'s units: "40%", "wall 5", "WN 350" or "25% of the lane".
        /// </summary>
        public static string Format(WallScale scale, float wall, float farWidthRatio)
        {
            int units = ToUnits(scale, wall, farWidthRatio);

            switch (scale)
            {
                case WallScale.Chunithm:
                    return $"wall {units}";

                case WallScale.Iidx:
                    return $"WN {units}";

                case WallScale.Lane:
                    return $"{units}% of the lane";

                default:
                    return $"{units}%";
            }
        }

        /// <summary>
        /// <paramref name="wall"/> as the wall toast shows it: "40% covered", "wall 5", "WN 350" or "25% of the lane covered".
        /// </summary>
        public static string Describe(WallScale scale, float wall, float farWidthRatio)
        {
            string units = Format(scale, wall, farWidthRatio);
            return scale == WallScale.Percent || scale == WallScale.Lane ? units + " covered" : units;
        }

        private static float compression(float farWidthRatio) => 1 / Math.Clamp(farWidthRatio, HighwayLayout.MIN_FAR_WIDTH_RATIO, 1) - 1;
    }
}
