// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// The hand groups of a stage, marked by stronger guide lines from 6K up (research/topic-wide-keys.md W1): 3|3, 3|1|3,
    /// 4|4, 4|1|4 and 5|5, mania's default split between the hands, with the odd centre key on its own. Up to 5K a stage
    /// is one group. From the key count alone: key bindings are per variant and need not follow the default.
    /// </summary>
    /// <remarks>
    /// A map made for stable's N+1 layout (its <c>SpecialStyle</c> flag: 8K played as 7 keys and a special key) is split
    /// 1 | N−1 instead, its special column on the skin's side (research/game-djmax.md R3).
    /// </remarks>
    public static class LaneGroups
    {
        /// <summary>
        /// Whether column <paramref name="column"/> (from 1 to <paramref name="keys"/> − 1) of a stage with
        /// <paramref name="keys"/> columns starts a new group, so the boundary to its left divides two groups.
        /// </summary>
        /// <param name="keys">The stage's columns.</param>
        /// <param name="column">The column right of the boundary.</param>
        /// <param name="special">The stage's special column (<see cref="SpecialSide"/>).</param>
        public static bool StartsGroup(int keys, int column, SpecialColumnSide special = SpecialColumnSide.None) => special switch
        {
            SpecialColumnSide.Left => column == 1,
            SpecialColumnSide.Right => column == keys - 1,
            _ => keys switch
            {
                6 => column == 3,
                7 => column == 3 || column == 4,
                8 => column == 4,
                9 => column == 4 || column == 5,
                10 => column == 5,
                _ => false,
            },
        };

        /// <summary>
        /// Where a stage's special column is, as stable lays out N+1 maps: none unless the map is one (its
        /// <c>SpecialStyle</c> flag) with an even total key count from 6 up (a single stage of an odd count has its centre
        /// key); then on the side the skin's <c>SpecialStyle</c> names for that key count, the left when it names none
        /// (stable's default). With two stages (14K as 7 + 7, 18K as 9 + 9) the skin's left (1) means the outer columns and
        /// right (2) the inner ones, as in stable.
        /// </summary>
        /// <param name="specialStyle">The map's <c>SpecialStyle</c> flag.</param>
        /// <param name="skinStyle">The skin's <c>SpecialStyle</c> for the play's total key count, if it sets one.</param>
        /// <param name="totalColumns">The play's columns, all stages together.</param>
        /// <param name="stage">The stage, from 0.</param>
        /// <param name="stageCount">The play's stages.</param>
        public static SpecialColumnSide SpecialSide(bool specialStyle, LegacyManiaSkinConfiguration.LegacySpecialStyle? skinStyle, int totalColumns, int stage, int stageCount)
        {
            if (!specialStyle || totalColumns < 6 || totalColumns % 2 != 0)
                return SpecialColumnSide.None;

            bool right = skinStyle == LegacyManiaSkinConfiguration.LegacySpecialStyle.Right;

            // Measured from the centre between the stages: the left stage's outer side is its left.
            if (stageCount == 2)
                return (stage == 0) != right ? SpecialColumnSide.Left : SpecialColumnSide.Right;

            return right ? SpecialColumnSide.Right : SpecialColumnSide.Left;
        }
    }

    /// <summary>
    /// The side of a stage that holds the special column of an N+1 map, if any (<see cref="LaneGroups.SpecialSide"/>).
    /// </summary>
    public enum SpecialColumnSide
    {
        None,
        Left,
        Right,
    }
}
