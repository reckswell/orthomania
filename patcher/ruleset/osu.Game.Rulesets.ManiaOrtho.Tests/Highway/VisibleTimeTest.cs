// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for the visible time the speed and wall settings show (<see cref="VisibleTime"/>). In gameplay, against
    /// drawn notes: <see cref="TestSceneVisibleTime"/>.
    /// </summary>
    [TestFixture]
    public class VisibleTimeTest
    {
        private static readonly float default_line = LegacyManiaSkinConfiguration.DEFAULT_HIT_POSITION;

        private static double at(double speed, float hitPosition = 124.8f, double rate = 1, float wall = 0, bool keepVisibleTime = true, bool flat = false)
            => VisibleTime.Of(DrawableManiaRuleset.ComputeScrollTime(speed), hitPosition, rate, new HighwayScroll(wall, keepVisibleTime), flat);

        [Test]
        public void TestDefaultLineIsManiasLabel() => Assert.That(at(29, default_line), Is.EqualTo(11485 / 29.0).Within(1e-9));

        [Test]
        public void TestLowerLineShowsNotesLonger()
        {
            Assert.That(at(29, Stage.HIT_TARGET_POSITION), Is.EqualTo(11485 / 29.0 * 658 / 643.2).Within(1e-3), "lazer's skins: +2.3%");
            Assert.That(at(29, 0), Is.EqualTo(11485 / 29.0 * 768 / 643.2).Within(1e-3), "line at the bottom: +19.4%");
        }

        [Test]
        public void TestRateCancelsOutUntilManiasCap()
        {
            Assert.That(at(29, rate: 1.5), Is.EqualTo(at(29)).Within(1e-9));
            Assert.That(at(29, rate: 0.75), Is.EqualTo(at(29)).Within(1e-9));

            // Speed 1 is 11485 ms, mania's cap in game time: at 1.5x that is 7657 real ms, and a lower line adds nothing.
            Assert.That(at(1, rate: 1.5), Is.EqualTo(11485 / 1.5).Within(1e-9));
            Assert.That(at(1, Stage.HIT_TARGET_POSITION), Is.EqualTo(11485).Within(1e-9));
            Assert.That(at(1, rate: 0), Is.EqualTo(at(1)), "a rate of 0 counts as 1");
        }

        [Test]
        public void TestWall()
        {
            Assert.That(at(29, wall: 0.4f), Is.EqualTo(at(29)).Within(1e-9), "keeping the visible time");
            Assert.That(at(29, wall: 0.4f, keepVisibleTime: false), Is.EqualTo(at(29) * 0.6).Within(1e-4), "keeping the speed");
            Assert.That(at(29, wall: 0.4f, keepVisibleTime: false, flat: true), Is.EqualTo(at(29)).Within(1e-9), "no wall when flat");
        }
    }
}
