// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for "Width by" (Alpha 2026-11-07): one track width for every key count, or a width per lane.
    /// </summary>
    [TestFixture]
    public class LaneWidthTest
    {
        private static readonly HighwaySettings chunithm = HighwayPresets.Get(HighwayPreset.Chunithm);

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(10)]
        public void TestTrackModeIgnoresTheKeyCount(int columns)
            => Assert.That(chunithm.HighwayTrackWidth(columns), Is.EqualTo(chunithm.TrackWidth));

        [TestCase(4, 0.1f, 0.4f)]
        [TestCase(7, 0.1f, 0.7f)]
        [TestCase(10, 0.1f, 1f)]
        [TestCase(7, 0.05f, 0.35f)]
        [TestCase(18, 0.1f, 1f)] // 9K dual stages: no wider than the screen
        [TestCase(1, HighwaySettings.MIN_LANE_WIDTH, HighwayLayout.MIN_NEAR_WIDTH)] // 1K at the thinnest: the narrowest track
        public void TestLaneModeGrowsWithTheKeyCount(int columns, float laneWidth, float trackWidth)
        {
            var settings = chunithm with { WidthMode = HighwayWidthMode.Lane, LaneWidth = laneWidth };
            Assert.That(settings.HighwayTrackWidth(columns), Is.EqualTo(trackWidth).Within(1e-6f));
        }

        /// <summary>
        /// Argon's 7K centre lane is twice as wide (8 lane units): the other lanes are the lane width.
        /// </summary>
        [Test]
        public void TestWiderSpecialLaneKeepsItsProportion()
        {
            var settings = chunithm with { WidthMode = HighwayWidthMode.Lane, LaneWidth = 0.08f };
            Assert.That(settings.HighwayTrackWidth(7, 1 / 8f), Is.EqualTo(0.64f).Within(1e-6f));
        }

        [Test]
        public void TestDefaultsKeepOneWidth()
        {
            Assert.That(new HighwaySettings().WidthMode, Is.EqualTo(HighwayWidthMode.Track));
            Assert.That(new HighwaySettings().LaneWidth, Is.EqualTo(HighwaySettings.DEFAULT_LANE_WIDTH));
        }
    }
}
