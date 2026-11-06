// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// A cover over part of the track, in track depth: what mania's Hidden, Fade In and Cover mods put over a column, for
    /// notes drawn on the track. Pure maths, no game state.
    /// </summary>
    /// <remarks>
    /// Fully covered from <see cref="CoveredFrom"/> to <see cref="CoveredTo"/>; the fade runs from <see cref="FadeFrom"/>
    /// (the end next to the covered part, covered by <see cref="FadeAlpha"/>) to <see cref="FadeTo"/> (not covered).
    /// Everything else is not covered. Visibility is 1 minus the cover, as mania's cover multiplies what is under it.
    /// </remarks>
    public readonly record struct DepthCover(float CoveredFrom, float CoveredTo, float FadeFrom, float FadeTo, float FadeAlpha)
    {
        /// <summary>
        /// The longest piece of the fade drawn with a straight colour ramp. Colours are interpolated linearly on screen, which
        /// is not linear in depth under perspective; short pieces keep the fade close to mania's.
        /// </summary>
        public const float MAX_FADE_STEP = 0.02f;

        private const float min_step = 1e-5f;

        /// <summary>
        /// No cover.
        /// </summary>
        public static DepthCover None => default;

        /// <summary>
        /// The far fade (<see cref="HighwaySettings.FarFade"/>, research/game-yarg-clonehero.md Y3): notes fade in linearly
        /// over the far <paramref name="share"/> of the visible track, from nothing where they appear (<paramref name="appearDepth"/>:
        /// the far edge, or the wall's edge) to fully shown. Beyond where they appear they stay hidden. None when the share
        /// is 0.
        /// </summary>
        public static DepthCover FarFade(float appearDepth, float share)
        {
            if (!(share > 0) || !(appearDepth > 0))
                return None;

            return new DepthCover(appearDepth, float.MaxValue, appearDepth, appearDepth * (1 - Math.Min(share, 1)), 1);
        }

        private bool hasCovered => CoveredTo > CoveredFrom;

        private bool hasFade => FadeAlpha > 0 && FadeFrom != FadeTo;

        public bool IsActive => hasCovered || hasFade;

        /// <summary>
        /// How much of a note at <paramref name="depth"/> shows, from 0 (covered) to 1.
        /// </summary>
        public float VisibilityAt(float depth) => VisibilityAt(depth, depth);

        /// <summary>
        /// The visibility at <paramref name="depth"/> of the part of the cover <paramref name="inside"/> is in. Used for the
        /// ends of a piece that starts or stops on an edge between parts: the fully covered part and a fade that starts below
        /// full cover (while mania's cover fades in) meet at a step.
        /// </summary>
        public float VisibilityAt(float depth, float inside)
        {
            if (hasCovered && inside >= CoveredFrom && inside <= CoveredTo)
                return 0;

            if (hasFade)
            {
                float t = (inside - FadeFrom) / (FadeTo - FadeFrom);

                if (t >= 0 && t <= 1)
                {
                    float at = Math.Clamp((depth - FadeFrom) / (FadeTo - FadeFrom), 0, 1);
                    return 1 - FadeAlpha * (1 - at);
                }
            }

            return 1;
        }

        /// <summary>
        /// The end of the piece starting at <paramref name="from"/>, at most <paramref name="to"/>, over which the visibility
        /// is a single straight ramp no longer than <see cref="MAX_FADE_STEP"/>: the next edge of a part, or the next step of
        /// the fade.
        /// </summary>
        public float NextCut(float from, float to)
        {
            float next = to;

            if (hasCovered)
            {
                next = earlier(CoveredFrom, from, next);
                next = earlier(CoveredTo, from, next);
            }

            if (hasFade)
            {
                float low = Math.Min(FadeFrom, FadeTo);
                float high = Math.Max(FadeFrom, FadeTo);

                next = earlier(low, from, next);
                next = earlier(high, from, next);

                // Also from just below the fade's near edge, which is too close to cut at.
                if (from >= low - min_step && from < high)
                {
                    int pieces = Math.Max(1, (int)MathF.Ceiling((high - low) / MAX_FADE_STEP));
                    float step = (high - low) / pieces;
                    int nextStep = (int)MathF.Floor(Math.Max(0, from - low) / step + 1e-3f) + 1;

                    // The last step ends exactly on the edge, so no sliver is left over from rounding.
                    float grid = nextStep >= pieces ? high : low + nextStep * step;

                    next = earlier(grid, from, next);
                }
            }

            return next;
        }

        private static float earlier(float candidate, float from, float current) => candidate > from + min_step && candidate < current ? candidate : current;
    }
}
