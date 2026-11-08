// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// How notes travel along the lane between appearing and reaching the judgement line (research/topic-approach-angles.md,
    /// research/game-sekai.md). A note's track depth (linear in time, from osu!mania's scroll algorithm) is turned into a
    /// position on the perspective lane; the ends never move, so a note still appears when it did and crosses the line
    /// exactly at its hit time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The family is set by how a note's on-screen size <c>s</c> (1 at the line, the far/near width ratio <c>r</c> at the far
    /// end) grows with the time still to go <c>t</c> (1 at the far end, 0 at the line): <c>s^p = 1 - t (1 - r^p)</c>.
    /// </para>
    /// <list type="bullet">
    /// <item><c>p = -1</c> (<see cref="PERSPECTIVE"/>): true perspective, linear in depth. Notes crawl far away and rush in near
    /// the line (Chunithm, SDVX, Arcaea).</item>
    /// <item><c>p = 0</c> (<see cref="SEKAI"/>): <c>s = r^t</c>, the size grows by the same factor every moment: Project Sekai's
    /// approach, <c>1.06^(45 (u - 1))</c> with <c>1.06^45 = 1 / r</c>.</item>
    /// <item><c>p = 1</c> (<see cref="EVEN"/>): the size, and so the screen height, change linearly: a steady on-screen speed.</item>
    /// </list>
    /// </remarks>
    public static class ApproachCurve
    {
        public const float PERSPECTIVE = -1;
        public const float SEKAI = 0;
        public const float EVEN = 1;

        private const double near_zero = 1e-4;

        /// <summary>
        /// The smallest size a note is drawn at, next to its size at the line. Far past the far edge (a row of mania's stage far
        /// above the visible track) the eased curves would otherwise run off to infinity; they stop at the vanishing point, as
        /// true perspective does.
        /// </summary>
        private const double min_scale = 1e-6;

        /// <summary>
        /// The perspective depth (as <see cref="HighwayProjection"/> places it) of a note at track depth
        /// <paramref name="depth"/> (0 at the line, 1 at the far edge, negative once passed).
        /// </summary>
        /// <param name="depth">Track depth, linear in time.</param>
        /// <param name="approach">From <see cref="PERSPECTIVE"/> through <see cref="SEKAI"/> to <see cref="EVEN"/>.</param>
        /// <param name="compression">The projection's depth compression, <c>1/r - 1</c>.</param>
        public static float Ease(float depth, float approach, float compression)
        {
            if (approach <= PERSPECTIVE || compression <= near_zero || !float.IsFinite(depth))
                return depth;

            double p = Math.Min(approach, EVEN);
            double c = compression;
            double r = 1 / (1 + c);
            double scale;

            if (Math.Abs(p) < near_zero)
                scale = Math.Pow(r, depth);
            else
            {
                // Past the line the base can reach 0 (the camera plane): keep it in front of the camera.
                double basis = Math.Max(1e-6, 1 - depth * (1 - Math.Pow(r, p)));
                scale = Math.Pow(basis, 1 / p);
            }

            return (float)((1 / Math.Max(min_scale, scale) - 1) / c);
        }

        /// <summary>
        /// The inverse of <see cref="Ease"/>: the track depth drawn at perspective depth <paramref name="perspectiveDepth"/>.
        /// </summary>
        public static float Unease(float perspectiveDepth, float approach, float compression)
        {
            if (approach <= PERSPECTIVE || compression <= near_zero || !float.IsFinite(perspectiveDepth))
                return perspectiveDepth;

            double p = Math.Min(approach, EVEN);
            double c = compression;
            double r = 1 / (1 + c);
            double scale = 1 / Math.Max(1e-6, 1 + c * perspectiveDepth);

            if (Math.Abs(p) < near_zero)
                return (float)(Math.Log(scale) / Math.Log(r));

            return (float)((1 - Math.Pow(scale, p)) / (1 - Math.Pow(r, p)));
        }
    }
}
