// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.UI.Scrolling.Algorithms;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// Maps time to track depth, using osu!mania's own scroll algorithm (so scroll velocity behaves exactly as in mania)
    /// and the wall setting (SPEC sections 3 and 4).
    /// </summary>
    public readonly struct HighwayScroll
    {
        /// <summary>
        /// The highest wall. Arcade players use up to about 0.885 (Chunithm wall 14; research/game-chunithm.md).
        /// </summary>
        public const float MAX_WALL = 0.9f;

        /// <summary>
        /// The fraction of the track, from the far edge, behind the wall.
        /// </summary>
        public readonly float Wall;

        /// <summary>
        /// Whether moving the wall keeps the visible time (true) or the note speed (false).
        /// </summary>
        public readonly bool WallKeepsVisibleTime;

        public HighwayScroll(float wall, bool wallKeepsVisibleTime)
        {
            Wall = Math.Clamp(wall, 0, MAX_WALL);
            WallKeepsVisibleTime = wallKeepsVisibleTime;
        }

        /// <summary>
        /// The depth at which notes come into view: the wall's near edge, or the far edge when there is no wall.
        /// </summary>
        public float AppearDepth => 1 - Wall;

        /// <summary>
        /// The track length, in depth units, that one time range covers.
        /// </summary>
        public float ScrollLength => WallKeepsVisibleTime ? AppearDepth : 1;

        /// <summary>
        /// How long a note is visible, from appearing to reaching the judgement line, at constant scroll velocity.
        /// </summary>
        public double VisibleTime(double timeRange) => WallKeepsVisibleTime ? timeRange : timeRange * AppearDepth;

        /// <summary>
        /// The track depth of something at <paramref name="time"/>: 0 at the judgement line when <paramref name="time"/>
        /// equals <paramref name="currentTime"/>, negative once it has passed.
        /// </summary>
        public float DepthAt(IScrollAlgorithm algorithm, double time, double currentTime, double timeRange)
            => algorithm.PositionAt(time, currentTime, timeRange, ScrollLength);

        /// <summary>
        /// The depth covered between two times (for example a hold's head and tail).
        /// </summary>
        public float LengthBetween(IScrollAlgorithm algorithm, double startTime, double endTime, double timeRange)
            => algorithm.GetLength(startTime, endTime, timeRange, ScrollLength);
    }
}
