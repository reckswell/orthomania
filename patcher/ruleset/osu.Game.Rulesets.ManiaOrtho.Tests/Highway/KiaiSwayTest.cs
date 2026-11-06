// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.ManiaOrtho.Highway;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for the optional kiai sway (SPEC 2): only in kiai sections, with the beat, growing and settling smoothly.
    /// </summary>
    [TestFixture]
    public class KiaiSwayTest
    {
        private const float tolerance = 1e-4f;

        /// <summary>
        /// 120 BPM (500 ms beats) from 0, with a kiai section from 10 s to 20 s.
        /// </summary>
        private static ControlPointInfo createControlPoints(double beatLength = 500)
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = beatLength });
            controlPoints.Add(10000, new EffectControlPoint { KiaiMode = true });
            controlPoints.Add(20000, new EffectControlPoint());
            return controlPoints;
        }

        [Test]
        public void TestNoKiaiNoSway()
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 500 });
            var sway = new KiaiSway(controlPoints);

            Assert.That(sway.SectionCount, Is.Zero);
            Assert.That(sway.At(10000), Is.Zero);
        }

        [TestCase(0)]
        [TestCase(9999)]
        [TestCase(10000)] // it grows from nothing
        [TestCase(20500)] // and has settled a beat after the end
        [TestCase(30000)]
        [TestCase(double.NaN)]
        public void TestStillOutsideKiai(double time)
        {
            Assert.That(new KiaiSway(createControlPoints()).At(time), Is.Zero);
        }

        /// <summary>
        /// Fully grown a beat in: at one side on each beat, the other side on the next, through the middle between them.
        /// </summary>
        [TestCase(10500, -1)]
        [TestCase(11000, 1)]
        [TestCase(11250, 0)]
        [TestCase(15500, -1)]
        [TestCase(20000, 1)]
        public void TestSwingsWithTheBeat(double time, float expected)
        {
            Assert.That(new KiaiSway(createControlPoints()).At(time), Is.EqualTo(expected).Within(tolerance));
        }

        [Test]
        public void TestGrowsAndSettlesSmoothly()
        {
            var sway = new KiaiSway(createControlPoints());

            // A quarter of a beat into the fade: smoothstep(0.25) of the swing, and smoothstep(0.75) a quarter beat after the end.
            Assert.That(sway.At(10125), Is.EqualTo(0.15625 * System.Math.Cos(System.Math.PI * 20.25)).Within(tolerance));
            Assert.That(sway.At(20125), Is.EqualTo(0.84375 * System.Math.Cos(System.Math.PI * 40.25)).Within(tolerance));

            // No jumps anywhere around the section's edges.
            for (double t = 9900; t < 20700; t += 10)
                Assert.That(System.Math.Abs(sway.At(t + 1) - sway.At(t)), Is.LessThan(0.02f), $"jumps at {t}");
        }

        [Test]
        public void TestKiaiPointsInARowAreOneSection()
        {
            var controlPoints = createControlPoints();
            controlPoints.Add(15000, new EffectControlPoint { KiaiMode = true, ScrollSpeed = 2 });
            var sway = new KiaiSway(controlPoints);

            Assert.That(sway.SectionCount, Is.EqualTo(1));
            Assert.That(sway.At(15000), Is.EqualTo(1).Within(tolerance), "no new fade at a scroll speed change");
        }

        [Test]
        public void TestSectionsCloserThanTheirFadeAreOne()
        {
            var controlPoints = createControlPoints();
            controlPoints.Add(20200, new EffectControlPoint { KiaiMode = true });
            controlPoints.Add(25000, new EffectControlPoint());
            controlPoints.Add(30000, new EffectControlPoint { KiaiMode = true });
            controlPoints.Add(35000, new EffectControlPoint());
            var sway = new KiaiSway(controlPoints);

            Assert.That(sway.SectionCount, Is.EqualTo(2), "the 200 ms gap is bridged, the 5 s one is not");
            Assert.That(sway.At(20100), Is.EqualTo(System.Math.Cos(System.Math.PI * 40.2)).Within(tolerance), "still fully swaying");
            Assert.That(sway.At(27000), Is.Zero);
        }

        /// <summary>
        /// A red line inside a kiai section, off the beat or with gimmick timing, carries the swing on instead of snapping it.
        /// </summary>
        [Test]
        public void TestRedLinesDoNotJump()
        {
            var controlPoints = createControlPoints();
            controlPoints.Add(15250, new TimingControlPoint { BeatLength = 500 });
            controlPoints.Add(16000, new TimingControlPoint { BeatLength = 6 });
            controlPoints.Add(17000, new TimingControlPoint { BeatLength = 250 });
            var sway = new KiaiSway(controlPoints);

            for (double t = 14900; t < 18000; t += 1)
                Assert.That(System.Math.Abs(sway.At(t + 1) - sway.At(t)), Is.LessThan(0.02f), $"jumps at {t}");
        }

        [TestCase(500, 500)]
        [TestCase(300, 300)]
        [TestCase(250, 500)] // 240 BPM: two beats
        [TestCase(6, 384)] // gimmick timing: never a shake
        [TestCase(0, 1000)]
        [TestCase(double.NaN, 1000)]
        public void TestSwingLength(double beatLength, double swing)
        {
            Assert.That(KiaiSway.SwingLength(beatLength), Is.EqualTo(swing));
        }

        [Test]
        public void TestFastSongsSwingEveryTwoBeats()
        {
            // 240 BPM: one swing takes two 250 ms beats.
            var sway = new KiaiSway(createControlPoints(250));

            Assert.That(sway.At(10500), Is.EqualTo(-1).Within(tolerance));
            Assert.That(sway.At(10750), Is.EqualTo(0).Within(tolerance));
            Assert.That(sway.At(11000), Is.EqualTo(1).Within(tolerance));
        }
    }
}
