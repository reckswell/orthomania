// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Configuration
{
    /// <summary>
    /// The string each game's remembered settings are stored in.
    /// </summary>
    [TestFixture]
    public class GameMemoryTest
    {
        [Test]
        public void TestReadAndWrite()
        {
            var memory = GameMemory.Parse("Voltex:ScrollSpeed=9.5;Chunithm:Wall=0.3,LaneTilt=60");

            Assert.That(memory.Get(HighwayPreset.Chunithm, "LaneTilt"), Is.EqualTo("60"));
            Assert.That(memory.Get(HighwayPreset.Voltex, "ScrollSpeed"), Is.EqualTo("9.5"));
            Assert.That(memory.Get(HighwayPreset.Sekai, "ScrollSpeed"), Is.Null);
            Assert.That(memory.ToString(), Is.EqualTo("Chunithm:LaneTilt=60,Wall=0.3;Voltex:ScrollSpeed=9.5"), "sorted, so the same values always write the same");
        }

        [Test]
        public void TestSetAndForget()
        {
            var memory = GameMemory.Parse(null);

            Assert.That(memory.Set(HighwayPreset.Chunithm, "Wall", "0.3"), Is.True);
            Assert.That(memory.Set(HighwayPreset.Chunithm, "Wall", "0.3"), Is.False, "no change");
            Assert.That(memory.Remembers(HighwayPreset.Chunithm), Is.True);
            Assert.That(memory.Set(HighwayPreset.Chunithm, "Wall", null), Is.True);
            Assert.That(memory.Remembers(HighwayPreset.Chunithm), Is.False, "nothing left");
            Assert.That(memory.ToString(), Is.Empty);
        }

        /// <summary>
        /// A string from a newer version, with a game this one does not know, loads and keeps that game when written again.
        /// </summary>
        [Test]
        public void TestUnknownGameKept()
        {
            var memory = GameMemory.Parse("Chunithm:Wall=0.3;Maimai:LaneTilt=40;99:Wall=1;broken;Voltex:=1,ScrollSpeed=9");

            memory.Set(HighwayPreset.Chunithm, "Wall", "0.4");

            Assert.That(memory.ToString(), Is.EqualTo("Chunithm:Wall=0.4;Voltex:ScrollSpeed=9;Maimai:LaneTilt=40;99:Wall=1"));
        }
    }
}
