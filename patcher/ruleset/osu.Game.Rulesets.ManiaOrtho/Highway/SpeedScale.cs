// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;
using System.Globalization;
using osu.Framework.Extensions;
using osu.Game.Rulesets.Mania.UI;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// The numbers the note speed is set, shown and stepped in (Alpha 2026-11-07): osu!mania's scroll speed, or another game's
    /// own speed number, each mapped to the time notes take as that game defines it (research/topic-speed.md,
    /// research/topic-speed-scales.md). The speed itself stays osu!mania's scroll speed setting, so nothing else changes.
    /// </summary>
    public enum SpeedScale
    {
        [Description("osu!mania")]
        Mania,

        [Description("Chunithm")]
        Chunithm,

        [Description("SDVX")]
        Sdvx,

        [Description("IIDX (green number)")]
        Iidx,

        [Description("Project Sekai")]
        Sekai,

        [Description("BanG Dream!")]
        BanGDream,

        [Description("Arcaea")]
        Arcaea,

        [Description("DJMAX")]
        Djmax,

        [Description("Quaver")]
        Quaver,

        [Description("StepMania / Etterna (CMod)")]
        StepMania,
    }

    /// <summary>
    /// The range of a speed scale's numbers and how they move.
    /// </summary>
    /// <param name="Min">The smallest number (the slowest, except for <see cref="SpeedScales.HigherIsSlower"/> scales).</param>
    /// <param name="Max">The largest number.</param>
    /// <param name="Precision">The finest step the game offers.</param>
    /// <param name="KeyStep">How far one press of the speed keys moves.</param>
    /// <param name="Decimals">Decimals the game shows.</param>
    public readonly record struct SpeedRange(double Min, double Max, double Precision, double KeyStep, int Decimals);

    public static class SpeedScales
    {
        /// <summary>
        /// The fastest osu!mania scroll speed maniaortho stores: above osu!mania's 40 so Chunithm's 16 to 19 fit (19 is 52 in
        /// osu!mania's units).
        /// </summary>
        public const double MAX_MANIA_SPEED = 60;

        /// <summary>
        /// The precision maniaortho stores osu!mania's scroll speed at: finer than osu!mania's 0.1, so every step of every
        /// scale lands on its own stored speed and reads back as itself.
        /// </summary>
        public const double MANIA_SPEED_PRECISION = 0.0001;

        /// <summary>
        /// Chunithm's visible length (395 arcade units) over its z speed per speed table unit (1.5 units per 60 Hz frame):
        /// T = 4389 / table ms (research/game-chunithm.md "Speed number to visible time").
        /// </summary>
        private const double chunithm_time_per_table = 395 / (1.5 * 60) * 1000;

        public static SpeedRange RangeOf(SpeedScale scale) => scale switch
        {
            SpeedScale.Mania => new SpeedRange(1, MAX_MANIA_SPEED, 0.1, 1, 1),
            // 1.00 to 15.00 in 0.25 steps, then 16, 17, 18 and 19 (SONIC, far beyond, is left out).
            SpeedScale.Chunithm => new SpeedRange(1, 19, 0.25, 0.25, 2),
            // EG's lane speed: the older games' BPM x HS over 100 (670 is 6.70).
            SpeedScale.Sdvx => new SpeedRange(1, 10, 0.01, 0.1, 2),
            // Larger is slower: from faster than anyone plays up to NORMAL HI-SPEED level 1.
            SpeedScale.Iidx => new SpeedRange(150, 1200, 1, 10, 0),
            SpeedScale.Sekai => new SpeedRange(1, 12, 0.01, 0.1, 2),
            SpeedScale.BanGDream => new SpeedRange(1, 12, 0.1, 0.1, 1),
            SpeedScale.Arcaea => new SpeedRange(1, 6.5, 0.1, 0.1, 1),
            SpeedScale.Djmax => new SpeedRange(1, 9, 0.1, 0.1, 1),
            // Up to about as fast as maniaortho's speed goes (Quaver's own goes to 100, CMods have no top).
            SpeedScale.Quaver => new SpeedRange(5, 60, 0.1, 1, 1),
            SpeedScale.StepMania => new SpeedRange(100, 1500, 10, 10, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, null),
        };

        /// <summary>
        /// Whether the scale counts the time notes are visible, below the wall (IIDX's green number: Floating Hi-Speed keeps
        /// it as the cover moves), rather than their speed (every other game: the time to cross the whole lane, which a cover
        /// does not change).
        /// </summary>
        public static bool MeasuresVisibleTime(SpeedScale scale) => scale == SpeedScale.Iidx;

        /// <summary>
        /// Whether a larger number is slower (the green number is a time).
        /// </summary>
        public static bool HigherIsSlower(SpeedScale scale) => scale == SpeedScale.Iidx;

        /// <summary>
        /// The time, in real milliseconds, a game's speed number gives: notes crossing the whole lane, or visible for that
        /// long for <see cref="MeasuresVisibleTime"/> scales.
        /// </summary>
        public static double ToMilliseconds(SpeedScale scale, double units)
        {
            switch (scale)
            {
                case SpeedScale.Chunithm:
                    return chunithm_time_per_table / chunithmTable(units);

                case SpeedScale.Sdvx:
                    // USC's 480,000 / (BPM x HS) with BPM x HS = 100 x lane speed (derived from the clones; the arcade is
                    // unmeasured, within about 7%: research/topic-speed-scales.md).
                    return 4800 / units;

                case SpeedScale.Iidx:
                    // 1/600 s per green number (GN 300 = 500 ms).
                    return units * 1000 / 600;

                case SpeedScale.Sekai:
                    // The Sonolus engine's duration curve, which fits the official game's captures (research/game-sekai.md).
                    return 350 + (4000 - 350) * Math.Pow(Math.Clamp((12 - units) / 11, 0, 1), 1.31);

                case SpeedScale.BanGDream:
                    // The MIT Sonolus recreation's durations (the official game's are unverified).
                    return units <= 11 ? (12 - units) * 500 : (16 - units) * 100;

                case SpeedScale.Arcaea:
                    // Confirmed by a frame-stepped capture of the official game: 200 / speed frames at 60 Hz.
                    return 10_000 / 3.0 / units;

                case SpeedScale.Djmax:
                    // RESPECT V's speed has not depended on the BPM since 2020 (the default gear at 1080p, from a fan measurement).
                    return 2693 / units;

                case SpeedScale.Quaver:
                    // The default 4K skin's line (research/game-quaver.md).
                    return 13528 / units;

                case SpeedScale.StepMania:
                    // 384 field px at 64 px per beat and C beats per minute: 360 / C s with the receptors at their default.
                    return 360_000 / units;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scale), scale, null);
            }
        }

        /// <summary>
        /// The inverse of <see cref="ToMilliseconds"/>, not rounded to the game's steps.
        /// </summary>
        public static double FromMilliseconds(SpeedScale scale, double milliseconds)
        {
            milliseconds = Math.Max(milliseconds, 1);

            switch (scale)
            {
                case SpeedScale.Chunithm:
                    double table = chunithm_time_per_table / milliseconds;
                    return table <= 12 ? table / 0.8 : 15 + (table - 12) / 2;

                case SpeedScale.Sdvx:
                    return 4800 / milliseconds;

                case SpeedScale.Iidx:
                    return milliseconds * 600 / 1000;

                case SpeedScale.Sekai:
                    double share = Math.Clamp((milliseconds - 350) / (4000 - 350), 0, 1);
                    return 12 - 11 * Math.Pow(share, 1 / 1.31);

                case SpeedScale.BanGDream:
                    return milliseconds >= 500 ? 12 - milliseconds / 500 : 16 - milliseconds / 100;

                case SpeedScale.Arcaea:
                    return 10_000 / 3.0 / milliseconds;

                case SpeedScale.Djmax:
                    return 2693 / milliseconds;

                case SpeedScale.Quaver:
                    return 13528 / milliseconds;

                case SpeedScale.StepMania:
                    return 360_000 / milliseconds;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scale), scale, null);
            }
        }

        /// <summary>
        /// The nearest number the game offers, within its range.
        /// </summary>
        public static double Snap(SpeedScale scale, double units)
        {
            var range = RangeOf(scale);
            units = Math.Clamp(units, range.Min, range.Max);

            // Chunithm moves in whole numbers past 15.
            if (scale == SpeedScale.Chunithm && units > 15)
                return Math.Round(units);

            return Math.Round(units / range.Precision) * range.Precision;
        }

        /// <summary>
        /// The next number one press of the speed keys reaches from <paramref name="units"/>, faster or slower.
        /// </summary>
        public static double Neighbour(SpeedScale scale, double units, bool faster)
        {
            var range = RangeOf(scale);
            double step = range.KeyStep;

            if (scale == SpeedScale.Chunithm && (faster ? units >= 15 : units > 15))
                step = 1;

            bool up = faster != HigherIsSlower(scale);
            return Snap(scale, units + (up ? step : -step));
        }

        /// <summary>
        /// "Chunithm 9.25", "SDVX 6.50", "GN 300".
        /// </summary>
        public static string Format(SpeedScale scale, double units)
        {
            string number = units.ToString("F" + RangeOf(scale).Decimals, CultureInfo.InvariantCulture);

            return scale switch
            {
                SpeedScale.Mania => $"speed {number}",
                SpeedScale.Iidx => $"GN {number}",
                SpeedScale.StepMania => $"C{number}",
                SpeedScale.Sekai => $"Sekai {number}",
                _ => $"{scale.GetDescription()} {number}",
            };
        }

        /// <summary>
        /// The speed setting's name in the settings and its toast, as the game calls it.
        /// </summary>
        public static string Caption(SpeedScale scale) => scale switch
        {
            SpeedScale.Mania => "Scroll speed",
            SpeedScale.Iidx => "Green number (IIDX)",
            SpeedScale.Sekai => "Note speed (Project Sekai)",
            SpeedScale.StepMania => "CMod (StepMania / Etterna)",
            _ => $"Speed ({scale.GetDescription()})",
        };

        /// <summary>
        /// What a scale's numbers mean, for its slider's hint.
        /// </summary>
        public static string Explain(SpeedScale scale) => scale switch
        {
            SpeedScale.Chunithm => "Chunithm's speed: 1.00 to 15.00 in 0.25 steps, then 16 to 19. Notes take 5486 / speed ms to cross the "
                                   + "whole track up to 15 (16 to 19 are faster still: 313 to 219 ms), as in the arcade; a wall that keeps "
                                   + "the speed hides part of that.",
            SpeedScale.Sdvx => "SDVX's lane speed, 1.00 to 10.00 (the older games' BPM x hi-speed over 100: 670 is 6.70; players use "
                               + "about 6 to 8.5). Notes take 4800 / speed ms to cross the whole track, as in the SDVX clones; the arcade "
                               + "may differ by a few percent.",
            SpeedScale.Iidx => "IIDX's green number: how long notes are visible below the wall, in 1/600 s (300 is half a second). "
                               + "Smaller is faster.",
            SpeedScale.Sekai => "Project Sekai's note speed, 1 to 12: the time notes take to cross the track, from the Sonolus engine's "
                                + "curve (10.0 is 741 ms, 11.0 is 508 ms). The game's notes also grow as they come closer; here they "
                                + "come at a steady speed.",
            SpeedScale.BanGDream => "BanG Dream!'s rhythm icon speed, 1.0 to 12.0: notes take (12 - speed) x 500 ms to cross the "
                                    + "track up to 11, then 100 ms less per step (11.0 is 500 ms, 12.0 is 400 ms), as its fan recreation "
                                    + "plays. The game's notes also grow as they come closer; here they come at a steady speed.",
            SpeedScale.Arcaea => "Arcaea's note speed, 1.0 to 6.5: notes take 3333 / speed ms to cross the track (4.0 is 833 ms).",
            SpeedScale.Djmax => "DJMAX RESPECT V's speed, 1.0 to 9.0: notes take 2693 / speed ms to cross the track, at every BPM "
                                + "(5.0 is 539 ms).",
            SpeedScale.Quaver => "Quaver's scroll speed: notes take 13528 / speed ms to cross the track, as with Quaver's default 4K "
                                 + "skin (15.0 is 902 ms).",
            SpeedScale.StepMania => "StepMania's and Etterna's constant speed (CMod): notes take 360 / C seconds to cross the track, "
                                    + "as on StepMania's default field (C600 is 600 ms).",
            _ => string.Empty,
        };

        private static double chunithmTable(double units) => units <= 15 ? units * 0.8 : 12 + (units - 15) * 2;
    }

    /// <summary>
    /// What turns osu!mania's scroll speed into the times a game's speed number describes: the skin's hit position (mania's
    /// time range scales with it), the rate, the wall and whether the camera is flat. The settings use the selected beatmap's,
    /// gameplay the play's.
    /// </summary>
    public readonly record struct SpeedContext(float HitPosition, double Rate, HighwayScroll Scroll, bool Flat)
    {
        /// <summary>
        /// Real milliseconds at <paramref name="maniaSpeed"/>: how long notes are visible, or how long they take to cross the
        /// whole track at their speed (longer than they are visible behind a wall).
        /// </summary>
        public double Milliseconds(double maniaSpeed, bool visibleTime)
        {
            double timeRange = VisibleTime.Of(DrawableManiaRuleset.ComputeScrollTime(maniaSpeed), HitPosition, Rate, new HighwayScroll(0, true), Flat);

            if (Flat)
                return timeRange;

            return visibleTime ? Scroll.VisibleTime(timeRange) : timeRange / Scroll.ScrollLength;
        }

        /// <summary>
        /// The osu!mania scroll speed giving <paramref name="milliseconds"/>, at the precision and within the range maniaortho
        /// stores it.
        /// </summary>
        public double ManiaSpeed(double milliseconds, bool visibleTime)
        {
            double timeRange = milliseconds;

            if (!Flat)
            {
                if (visibleTime)
                    timeRange = Scroll.WallKeepsVisibleTime ? milliseconds : milliseconds / Scroll.AppearDepth;
                else
                    timeRange = milliseconds * Scroll.ScrollLength;
            }

            double speed = DrawableManiaRuleset.MAX_TIME_RANGE * VisibleTime.HitPositionScale(HitPosition) / Math.Max(timeRange, 1);
            speed = Math.Round(speed / SpeedScales.MANIA_SPEED_PRECISION) * SpeedScales.MANIA_SPEED_PRECISION;

            return Math.Clamp(speed, 1, SpeedScales.MAX_MANIA_SPEED);
        }

        /// <summary>
        /// A scroll speed in a scale's numbers, rounded to the nearest one the game offers.
        /// </summary>
        public double ToUnits(SpeedScale scale, double maniaSpeed)
        {
            if (scale == SpeedScale.Mania)
                return SpeedScales.Snap(scale, maniaSpeed);

            return SpeedScales.Snap(scale, SpeedScales.FromMilliseconds(scale, Milliseconds(maniaSpeed, SpeedScales.MeasuresVisibleTime(scale))));
        }

        /// <summary>
        /// The scroll speed a scale's number gives.
        /// </summary>
        public double FromUnits(SpeedScale scale, double units)
        {
            if (scale == SpeedScale.Mania)
                return Math.Clamp(units, 1, SpeedScales.MAX_MANIA_SPEED);

            return ManiaSpeed(SpeedScales.ToMilliseconds(scale, units), SpeedScales.MeasuresVisibleTime(scale));
        }

        /// <summary>
        /// The scroll speed after <paramref name="steps"/> presses of the speed keys (positive faster) in a game's numbers:
        /// from a speed between two of them, the first press lands on the next one that way. Never moves against the key.
        /// </summary>
        public double Step(SpeedScale scale, double maniaSpeed, int steps)
        {
            if (steps == 0)
                return maniaSpeed;

            if (scale == SpeedScale.Mania)
                return Math.Clamp(maniaSpeed + steps, 1, SpeedScales.MAX_MANIA_SPEED);

            bool faster = steps > 0;
            double units = ToUnits(scale, maniaSpeed);
            double atUnits = FromUnits(scale, units);
            int presses = Math.Abs(steps);

            // The nearest number already lies the way the key goes: the first press only reaches it.
            if (faster ? atUnits > maniaSpeed + SpeedScales.MANIA_SPEED_PRECISION / 2 : atUnits < maniaSpeed - SpeedScales.MANIA_SPEED_PRECISION / 2)
                presses--;

            for (int i = 0; i < presses; i++)
                units = SpeedScales.Neighbour(scale, units, faster);

            double next = FromUnits(scale, units);
            return faster ? Math.Max(next, maniaSpeed) : Math.Min(next, maniaSpeed);
        }
    }
}
