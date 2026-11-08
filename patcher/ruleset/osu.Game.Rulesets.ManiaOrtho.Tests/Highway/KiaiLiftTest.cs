// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for the optional kiai lift (research/topic-camera-motion.md C1): only in kiai sections, ramping over three
    /// beats, and a lift that holds the judgement line and the notes' speed at it.
    /// </summary>
    [TestFixture]
    public class KiaiLiftTest
    {
        private const float tolerance = 1e-4f;

        /// <summary>
        /// 120 BPM (500 ms beats, so 1500 ms ramps) from 0, with a kiai section from 10 s to 20 s.
        /// </summary>
        private static ControlPointInfo createControlPoints(double beatLength = 500)
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = beatLength });
            controlPoints.Add(10000, new EffectControlPoint { KiaiMode = true });
            controlPoints.Add(20000, new EffectControlPoint());
            return controlPoints;
        }

        [TestCase(0)]
        [TestCase(9999)]
        [TestCase(10000)] // it rises from nothing
        [TestCase(21500)] // and has settled three beats after the end
        [TestCase(30000)]
        [TestCase(double.NaN)]
        public void TestAtRestOutsideKiai(double time)
        {
            Assert.That(new KiaiLift(createControlPoints()).At(time), Is.Zero);
        }

        [TestCase(10750, 0.5f)] // halfway up the ramp: smoothstep's middle
        [TestCase(11500, 1f)]
        [TestCase(15000, 1f)]
        [TestCase(20000, 1f)]
        [TestCase(20750, 0.5f)]
        public void TestRampsOverThreeBeats(double time, float expected)
        {
            Assert.That(new KiaiLift(createControlPoints()).At(time), Is.EqualTo(expected).Within(tolerance));
        }

        [TestCase(100, KiaiLift.MIN_RAMP)] // 600 BPM: three beats would jolt
        [TestCase(1000, KiaiLift.MAX_RAMP)] // 60 BPM: three beats would drift
        [TestCase(double.NaN, KiaiLift.MAX_RAMP)]
        public void TestRampIsClamped(double beatLength, double expected)
        {
            Assert.That(KiaiLift.RampLength(beatLength), Is.EqualTo(expected));
        }

        [Test]
        public void TestCloseSectionsStayLifted()
        {
            var controlPoints = createControlPoints();
            controlPoints.Add(21000, new EffectControlPoint { KiaiMode = true });
            controlPoints.Add(30000, new EffectControlPoint());

            var lift = new KiaiLift(controlPoints);

            Assert.That(lift.SectionCount, Is.EqualTo(1));
            Assert.That(lift.At(20900), Is.EqualTo(1).Within(tolerance));
        }

        /// <summary>
        /// SDVX-like rest pose: the line, near width and the notes' speed at the line hold; the far end rises and widens.
        /// </summary>
        [TestCase(0.3f)]
        [TestCase(1f)]
        public void TestLiftHoldsTheLine(float amount)
        {
            var size = new Vector2(1920, 1080);
            var rest = new HighwayLayout { JudgementY = 0.941f, FarY = 0.41f, NearWidth = 0.435f, FarWidthRatio = 0.071f };
            var lifted = rest.Lifted(amount);

            var before = new HighwayProjection(rest, size);
            var after = new HighwayProjection(lifted, size);

            Assert.That(lifted.FarY, Is.EqualTo(0.41f * (1 - amount)).Within(tolerance));
            Assert.That(lifted.FarWidthRatio, Is.GreaterThan(rest.FarWidthRatio));
            Assert.That(after.ToScreen(0, 0), Is.EqualTo(before.ToScreen(0, 0)));
            Assert.That(after.ToScreen(1, 0), Is.EqualTo(before.ToScreen(1, 0)));

            // The speed at the line, dy/dd at depth 0: the line-to-far-edge height times 1 + c = 1 / r.
            Assert.That((lifted.JudgementY - lifted.FarY) / lifted.FarWidthRatio, Is.EqualTo((rest.JudgementY - rest.FarY) / rest.FarWidthRatio).Within(1e-3f));
            Assert.That(after.YAt(1e-4f) - after.YAt(0), Is.EqualTo(before.YAt(1e-4f) - before.YAt(0)).Within(0.005f));
        }

        [Test]
        public void TestLaneAtTheTopDoesNotMove()
        {
            var layout = new HighwayLayout { JudgementY = 0.926f, FarY = 0, FarWidthRatio = 0.24f };
            Assert.That(layout.Lifted(1), Is.EqualTo(layout));
        }

        [Test]
        public void TestLeanKeepsItsConvergencePoint()
        {
            var size = new Vector2(1920, 1080);
            var rest = new HighwayLayout { JudgementY = 0.941f, FarY = 0.41f, NearWidth = 0.435f, FarWidthRatio = 0.071f }.LeanTowards(0.3f);
            var lifted = new HighwayProjection(rest.Lifted(0.5f), size);

            Assert.That(lifted.VanishingPoint!.Value.X, Is.EqualTo(0.3f * size.X).Within(0.5f));
        }
    }
}
