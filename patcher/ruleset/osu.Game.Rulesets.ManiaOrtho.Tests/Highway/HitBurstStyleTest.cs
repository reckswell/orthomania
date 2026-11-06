// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// Which burst each mania result gets (research game-chunithm-feedback P1 and P2), the hold glow's pulse rate (P6), and how
    /// long a hit lights its lane's piece of the judgement line.
    /// </summary>
    [TestFixture]
    public class HitBurstStyleTest
    {
        [Test]
        public void TestEveryHitHasABurstAndAMissHasNone()
        {
            var hitWindows = new ManiaHitWindows();
            var results = System.Enum.GetValues<HitResult>().Where(r => r.IsBasic() && hitWindows.IsHitResultAllowed(r)).ToArray();

            Assert.That(results, Does.Contain(HitResult.Perfect).And.Contain(HitResult.Meh));

            foreach (var result in results)
            {
                if (result == HitResult.Miss)
                    Assert.That(HitBurstStyle.For(result), Is.Null, "a miss must not light the line");
                else
                    Assert.That(HitBurstStyle.For(result), Is.Not.Null, $"{result} has no burst");
            }

            Assert.That(HitBurstStyle.For(HitResult.IgnoreHit), Is.Null);
        }

        [Test]
        public void TestPerfectAndGreatAreEasyToTellApart()
        {
            var perfect = HitBurstStyle.For(HitResult.Perfect)!.Value;
            var great = HitBurstStyle.For(HitResult.Great)!.Value;

            Assert.That(perfect.Colour, Is.EqualTo(HitBurstStyle.PERFECT_COLOUR));
            Assert.That(great.Colour, Is.EqualTo(HitBurstStyle.GREAT_COLOUR));
            Assert.That(perfect.Sparkles, Is.GreaterThan(great.Sparkles));
            Assert.That(perfect.Sparkles, Is.LessThanOrEqualTo(HighwayHitBurst.MAX_SPARKLES));
        }

        [Test]
        public void TestLowerResultsAreSmallerAndDimmer()
        {
            var great = HitBurstStyle.For(HitResult.Great)!.Value;
            var good = HitBurstStyle.For(HitResult.Good)!.Value;
            var meh = HitBurstStyle.For(HitResult.Meh)!.Value;

            Assert.That(HitBurstStyle.For(HitResult.Ok), Is.EqualTo(good));
            Assert.That(good.RingEnd, Is.LessThan(great.RingEnd));
            Assert.That(good.Sparkles, Is.Zero);
            Assert.That(good.Core, Is.False);
            Assert.That(meh.Alpha, Is.LessThan(good.Alpha));
        }

        [TestCase(500, 250)] // 120 BPM: 1/8 notes.
        [TestCase(600, 150)] // 100 BPM: 1/16 notes.
        [TestCase(352.941, 176.4705)] // 170 BPM: 1/8 notes.
        [TestCase(240, 240)] // 250 BPM: 1/4 notes.
        [TestCase(0, 125)] // No usable timing.
        [TestCase(6, 192)] // 10000 BPM gimmick: 6 ms doubled into range, not a flicker.
        [TestCase(60000, 234.375)] // 1 BPM: 15 s halved into range, not a stopped pulse.
        public void TestHoldTicksAtChunithmsCadence(double beatLength, double interval)
        {
            Assert.That(HighwayHitEffectLayer.HoldTickInterval(beatLength), Is.EqualTo(interval).Within(1e-3));
        }

        [Test]
        public void TestHoldTickPhaseFollowsTheBeatGrid()
        {
            // 170 BPM from 1000 ms: a tick every half beat (176.47 ms).
            var timing = new TimingControlPoint { Time = 1000, BeatLength = 60000 / 170.0 };
            double interval = HighwayHitEffectLayer.HoldTickInterval(timing.BeatLength);

            Assert.That(HighwayHitEffectLayer.HoldTickPhase(1000, timing), Is.EqualTo(0).Within(1e-4));
            Assert.That(HighwayHitEffectLayer.HoldTickPhase(1000 + interval * 0.25, timing), Is.EqualTo(0.25f).Within(1e-4));
            Assert.That(HighwayHitEffectLayer.HoldTickPhase(1000 + interval * 7.5, timing), Is.EqualTo(0.5f).Within(1e-4));

            // Before the timing point (a hold under the first one): still on the grid, in [0, 1).
            Assert.That(HighwayHitEffectLayer.HoldTickPhase(1000 - interval * 0.25, timing), Is.EqualTo(0.75f).Within(1e-4));
        }

        /// <summary>
        /// A hit lights its lane's piece of the line and the light falls linearly to nothing over 300 ms (research
        /// game-sekai.md K7, shortened from Sekai's 500 ms).
        /// </summary>
        [TestCase(0, 1f)]
        [TestCase(75, 0.75f)]
        [TestCase(150, 0.5f)]
        [TestCase(297, 0.01f)]
        [TestCase(300, 0f)]
        [TestCase(5000, 0f)]
        [TestCase(-16, 0f)] // Rewound before the hit.
        [TestCase(double.NaN, 0f)] // Never hit.
        public void TestSlotLightFadesLinearly(double elapsed, float level)
            => Assert.That(HighwayHitEffectLayer.SlotLevel(elapsed), Is.EqualTo(level).Within(1e-4f));

        /// <summary>
        /// A slot lights as strongly as the result's burst: Perfect and Great at the full 80%, a Meh dimmer (a miss has none).
        /// </summary>
        [TestCase(HitResult.Perfect, 0.8f)]
        [TestCase(HitResult.Great, 0.8f)]
        [TestCase(HitResult.Good, 0.72f)]
        [TestCase(HitResult.Meh, 0.48f)]
        public void TestSlotLightPeak(HitResult result, float peak)
            => Assert.That(HighwayHitEffectLayer.SlotPeak(HitBurstStyle.For(result)!.Value), Is.EqualTo(peak).Within(1e-4f));

        /// <summary>
        /// Slots take the burst's colour, except a Perfect's pale gold, which stands out from the judgement line's gold glow.
        /// </summary>
        [Test]
        public void TestSlotLightColours()
        {
            Assert.That(HighwayHitEffectLayer.SlotColour(HitResult.Perfect, HitBurstStyle.For(HitResult.Perfect)!.Value),
                Is.EqualTo(HighwayHitEffectLayer.PERFECT_SLOT_COLOUR));
            Assert.That(HighwayHitEffectLayer.SlotColour(HitResult.Great, HitBurstStyle.For(HitResult.Great)!.Value), Is.EqualTo(HitBurstStyle.GREAT_COLOUR));
            Assert.That(HighwayHitEffectLayer.SlotColour(HitResult.Meh, HitBurstStyle.For(HitResult.Meh)!.Value), Is.EqualTo(HitBurstStyle.MEH_COLOUR));

            // Paler than the glow's gold (255, 226, 119): more blue, so a lit lane reads as lit rather than as a thicker glow.
            Assert.That(HighwayHitEffectLayer.PERFECT_SLOT_COLOUR.B, Is.GreaterThan(0.75f));
        }
    }
}
