// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Mania.Skinning;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// How long notes are visible on the highway (SPEC 3 and 4), in real milliseconds, as the speed and wall settings show it.
    /// Mania's own label, <c>11485 / speed</c>, is not this: mania scales its time range by the skin's hit position, and the
    /// wall shortens it when it keeps the speed (research/game-osu-mania.md proposal 1, topic-covers.md C3).
    /// </summary>
    public static class VisibleTime
    {
        /// <summary>
        /// The real time a note is visible at constant scroll velocity, from coming into view (at the wall, or the far edge
        /// when there is none) to reaching the judgement line.
        /// </summary>
        /// <param name="targetTimeRange">Mania's time range for its scroll speed setting, before the skin and the rate:
        /// <see cref="DrawableManiaRuleset.ComputeScrollTime"/> of the speed, or a play's
        /// <see cref="DrawableManiaRuleset.TargetTimeRange"/>.</param>
        /// <param name="hitPosition">The skin's hit position as mania reads it for its time range (see <see cref="HitPosition(ISkin)"/>).</param>
        /// <param name="rate">The rate the mods play at (1.5 with Double Time).</param>
        /// <param name="scroll">The wall.</param>
        /// <param name="flat">Whether the camera is flat, which draws mania's own stage and no wall.</param>
        public static double Of(double targetTimeRange, float hitPosition, double rate, HighwayScroll scroll, bool flat)
        {
            if (rate <= 0)
                rate = 1;

            // As DrawableManiaRuleset sets its time range every frame, including the clamp of its bindable: in game time, so
            // the rate's factor cancels out unless the clamp is reached.
            double timeRange = Math.Clamp(targetTimeRange * rate * HitPositionScale(hitPosition), 1, DrawableManiaRuleset.MAX_TIME_RANGE);

            return (flat ? timeRange : scroll.VisibleTime(timeRange)) / rate;
        }

        /// <summary>
        /// Mania's factor on its time range for a hit position: notes keep their on-screen speed as the line moves, so a lower
        /// line shows them for longer.
        /// </summary>
        public static float HitPositionScale(float hitPosition)
        {
            const float length_to_default_hit_position = 768 - LegacyManiaSkinConfiguration.DEFAULT_HIT_POSITION;
            return (768 - hitPosition) / length_to_default_hit_position;
        }

        /// <summary>
        /// The hit position mania reads in gameplay from the ruleset's skin (which knows the beatmap's key count).
        /// </summary>
        public static float HitPosition(ISkin skin)
            => skin.GetConfig<ManiaSkinConfigurationLookup, float>(new ManiaSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HitPosition))?.Value
               ?? Stage.HIT_TARGET_POSITION;

        /// <summary>
        /// The hit position a skin gives for <paramref name="keys"/> columns, outside gameplay (no ruleset skin): the same
        /// value <see cref="HitPosition(ISkin)"/> finds in gameplay for a beatmap with that many columns.
        /// </summary>
        public static float HitPosition(ISkin skin, int keys)
            => skin.GetConfig<LegacyManiaSkinConfigurationLookup, float>(new LegacyManiaSkinConfigurationLookup(keys, LegacyManiaSkinConfigurationLookups.HitPosition))?.Value
               ?? Stage.HIT_TARGET_POSITION;
    }
}
