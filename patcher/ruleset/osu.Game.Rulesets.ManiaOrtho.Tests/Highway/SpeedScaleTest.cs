// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for the speed scales (Alpha 2026-11-07; research/topic-speed.md, research/topic-speed-scales.md): each game's
    /// numbers give the times that game gives, every number round trips through osu!mania's stored scroll speed, and the keys
    /// step in the game's own steps.
    /// </summary>
    [TestFixture]
    public class SpeedScaleTest
    {
        private static readonly SpeedScale[] game_scales = Enum.GetValues<SpeedScale>().Where(s => s != SpeedScale.Mania).ToArray();

        private static readonly SpeedContext plain = new SpeedContext(LegacyManiaSkinConfiguration.DEFAULT_HIT_POSITION, 1, new HighwayScroll(0, true), false);

        /// <summary>
        /// Plain, argon's line (lower, so mania shows notes for longer), a Chunithm wall that keeps the speed, a wall that keeps
        /// the visible time, Double Time, and the flat camera with a wall it does not draw.
        /// </summary>
        private static IEnumerable<SpeedContext> contexts() => new[]
        {
            plain,
            plain with { HitPosition = Stage.HIT_TARGET_POSITION },
            plain with { Scroll = new HighwayScroll(WallScales.ChunithmWall(5), false) },
            plain with { Scroll = new HighwayScroll(0.4f, true) },
            plain with { Rate = 1.5 },
            plain with { Scroll = new HighwayScroll(0.4f, false), Flat = true },
        };

        /// <summary>
        /// Each game's own figures, as its research note gives them.
        /// </summary>
        [TestCase(SpeedScale.Chunithm, 6, 914)] // default
        [TestCase(SpeedScale.Chunithm, 8, 686)] // "a common scroll speed for MASTER charts"
        [TestCase(SpeedScale.Chunithm, 13.85, 396)] // Alpha's osu!mania 29
        [TestCase(SpeedScale.Chunithm, 16, 313)]
        [TestCase(SpeedScale.Chunithm, 19, 219)]
        [TestCase(SpeedScale.Iidx, 300, 500)] // GN 300 = 0.5 s
        [TestCase(SpeedScale.Sekai, 6, 2000)]
        [TestCase(SpeedScale.Sekai, 10, 741)]
        [TestCase(SpeedScale.Sekai, 12, 350)]
        [TestCase(SpeedScale.Sdvx, 7, 686)]
        [TestCase(SpeedScale.Sdvx, 6.7, 716)] // BPM x HS 670
        [TestCase(SpeedScale.BanGDream, 10, 1000)]
        [TestCase(SpeedScale.BanGDream, 11, 500)]
        [TestCase(SpeedScale.BanGDream, 11.5, 450)]
        [TestCase(SpeedScale.BanGDream, 12, 400)]
        [TestCase(SpeedScale.Arcaea, 4, 833)]
        [TestCase(SpeedScale.Djmax, 5, 539)]
        [TestCase(SpeedScale.Quaver, 15, 902)]
        [TestCase(SpeedScale.StepMania, 600, 600)]
        [TestCase(SpeedScale.StepMania, 800, 450)]
        public void TestGamesOwnFigures(SpeedScale scale, double units, double milliseconds)
        {
            Assert.That(SpeedScales.ToMilliseconds(scale, units), Is.EqualTo(milliseconds).Within(1));
            Assert.That(SpeedScales.FromMilliseconds(scale, SpeedScales.ToMilliseconds(scale, units)), Is.EqualTo(units).Within(1e-6));
        }

        /// <summary>
        /// A speed number means that time: across the whole track (most games keep the speed under a cover), or visible below
        /// the wall for the green number, whatever the wall, skin line, rate and camera.
        /// </summary>
        [TestCaseSource(nameof(contexts))]
        public void TestNumbersGiveTheirTime(SpeedContext context)
        {
            foreach (var scale in game_scales)
            {
                double units = SpeedScales.Snap(scale, (SpeedScales.RangeOf(scale).Min + SpeedScales.RangeOf(scale).Max) / 2);
                double speed = context.FromUnits(scale, units);
                bool visible = SpeedScales.MeasuresVisibleTime(scale);

                Assert.That(context.Milliseconds(speed, visible), Is.EqualTo(SpeedScales.ToMilliseconds(scale, units)).Within(0.1).Percent, $"{scale} {units}");
            }

            // The green number counts what the wall leaves; the others what is there to cross.
            double gn300 = context.FromUnits(SpeedScale.Iidx, 300);
            double visibleTime = VisibleTime.Of(DrawableManiaRuleset.ComputeScrollTime(gn300), context.HitPosition, context.Rate, context.Scroll, context.Flat);
            Assert.That(visibleTime, Is.EqualTo(500).Within(0.1).Percent);
        }

        /// <summary>
        /// Every number each game offers within osu!mania's speed range, at its finest step, reads back as itself once stored
        /// at maniaortho's precision.
        /// </summary>
        [TestCaseSource(nameof(contexts))]
        public void TestEveryNumberRoundTrips(SpeedContext context)
        {
            foreach (var scale in game_scales)
            {
                var range = SpeedScales.RangeOf(scale);

                for (int step = 0; range.Min + step * range.Precision <= range.Max + 1e-9; step++)
                {
                    double units = SpeedScales.Snap(scale, range.Min + step * range.Precision);
                    double speed = context.FromUnits(scale, units);

                    // Out of the stored range (clamped): the number cannot be reached.
                    if (speed <= 1 || speed >= SpeedScales.MAX_MANIA_SPEED)
                        continue;

                    Assert.That(context.ToUnits(scale, speed), Is.EqualTo(units).Within(1e-9), $"{scale} {units}");
                }
            }
        }

        [Test]
        public void TestNumbersStayOnTheGamesSteps()
        {
            Assert.That(SpeedScales.Snap(SpeedScale.Chunithm, 13.85), Is.EqualTo(13.75));
            Assert.That(SpeedScales.Snap(SpeedScale.Chunithm, 15.4), Is.EqualTo(15));
            Assert.That(SpeedScales.Snap(SpeedScale.Chunithm, 15.6), Is.EqualTo(16));
            Assert.That(SpeedScales.Snap(SpeedScale.Chunithm, 30), Is.EqualTo(19));
            Assert.That(SpeedScales.Snap(SpeedScale.Iidx, 99), Is.EqualTo(150));
            Assert.That(SpeedScales.Snap(SpeedScale.Sekai, 10.555), Is.EqualTo(10.56).Within(1e-9));
        }

        /// <summary>
        /// One press of F3 or F4 moves one of the game's steps (Chunithm's 0.25 up to 15, then whole numbers), from between two
        /// numbers lands on the next one that way, and stops at either end.
        /// </summary>
        [Test]
        public void TestKeySteps()
        {
            var c = plain;

            Assert.That(c.ToUnits(SpeedScale.Chunithm, c.Step(SpeedScale.Chunithm, c.FromUnits(SpeedScale.Chunithm, 9), 1)), Is.EqualTo(9.25));
            Assert.That(c.ToUnits(SpeedScale.Chunithm, c.Step(SpeedScale.Chunithm, c.FromUnits(SpeedScale.Chunithm, 9), -1)), Is.EqualTo(8.75));
            Assert.That(c.ToUnits(SpeedScale.Chunithm, c.Step(SpeedScale.Chunithm, c.FromUnits(SpeedScale.Chunithm, 15), 1)), Is.EqualTo(16));
            Assert.That(c.ToUnits(SpeedScale.Chunithm, c.Step(SpeedScale.Chunithm, c.FromUnits(SpeedScale.Chunithm, 16), -1)), Is.EqualTo(15));
            Assert.That(c.ToUnits(SpeedScale.Iidx, c.Step(SpeedScale.Iidx, c.FromUnits(SpeedScale.Iidx, 300), 1)), Is.EqualTo(290), "faster is a smaller green number");

            // osu!mania 29 is Chunithm 13.85 (with the default skin's line): F4 lands on 14, F3 on 13.75.
            double alpha = c.ManiaSpeed(SpeedScales.ToMilliseconds(SpeedScale.Chunithm, 13.85), false);
            Assert.That(c.ToUnits(SpeedScale.Chunithm, c.Step(SpeedScale.Chunithm, alpha, 1)), Is.EqualTo(14));
            Assert.That(c.ToUnits(SpeedScale.Chunithm, c.Step(SpeedScale.Chunithm, alpha, -1)), Is.EqualTo(13.75));

            // osu!mania's own: one speed per press, as in osu!mania.
            Assert.That(c.Step(SpeedScale.Mania, 29, 1), Is.EqualTo(30));
        }

        [TestCaseSource(nameof(contexts))]
        public void TestKeysNeverGoBack(SpeedContext context)
        {
            foreach (var scale in Enum.GetValues<SpeedScale>())
            {
                for (double speed = 1; speed <= SpeedScales.MAX_MANIA_SPEED; speed += 0.737)
                {
                    double up = context.Step(scale, speed, 1);
                    double down = context.Step(scale, speed, -1);

                    Assert.That(up, Is.GreaterThanOrEqualTo(speed), $"{scale} up from {speed}");
                    Assert.That(down, Is.LessThanOrEqualTo(speed), $"{scale} down from {speed}");
                    Assert.That(up, Is.InRange(1, SpeedScales.MAX_MANIA_SPEED));
                    Assert.That(down, Is.InRange(1, SpeedScales.MAX_MANIA_SPEED));
                }
            }
        }

        [TestCase(SpeedScale.Mania, 29, "speed 29.0")]
        [TestCase(SpeedScale.Chunithm, 9.25, "Chunithm 9.25")]
        [TestCase(SpeedScale.Iidx, 300, "GN 300")]
        [TestCase(SpeedScale.StepMania, 700, "C700")]
        [TestCase(SpeedScale.Sdvx, 7, "SDVX 7.00")]
        [TestCase(SpeedScale.BanGDream, 11.5, "BanG Dream! 11.5")]
        public void TestFormat(SpeedScale scale, double units, string expected) => Assert.That(SpeedScales.Format(scale, units), Is.EqualTo(expected));
    }
}
