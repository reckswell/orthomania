// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.ManiaOrtho.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Parity
{
    /// <summary>
    /// The ruleset offers mania's mods. Where one is replaced to draw on the highway (Flashlight), the replacement must be
    /// the mania mod in every way that scores.
    /// </summary>
    [TestFixture]
    public class ModListTest
    {
        private readonly ManiaRuleset mania = new ManiaRuleset();
        private readonly ManiaOrthoRuleset ortho = new ManiaOrthoRuleset();

        [Test]
        public void TestSameModsAsMania([Values] ModType type)
        {
            var maniaMods = flatten(mania.GetModsFor(type)).ToArray();
            var orthoMods = flatten(ortho.GetModsFor(type)).ToArray();

            Assert.That(orthoMods.Select(m => m.Acronym), Is.EqualTo(maniaMods.Select(m => m.Acronym)));

            for (int i = 0; i < maniaMods.Length; i++)
                Assert.That(orthoMods[i], Is.InstanceOf(maniaMods[i].GetType()), maniaMods[i].Acronym);
        }

        [Test]
        public void TestFlashlightDrawsOnTheHighway()
        {
            Assert.That(ortho.CreateModFromAcronym("FL"), Is.InstanceOf<ManiaOrthoModFlashlight>());
            Assert.That(ortho.ConvertFromLegacyMods(LegacyMods.Flashlight), Has.One.InstanceOf<ManiaOrthoModFlashlight>());
        }

        /// <summary>
        /// Score multipliers are looked up by exact mod type, so a replacement would silently lose one mania adds later.
        /// </summary>
        [TestCase("FL")]
        [TestCase("FL", "HR")]
        [TestCase("FL", "DT")]
        [TestCase("FL", "EZ")]
        [TestCase("FL", "NF")]
        public void TestFlashlightMultiplierIsMania(params string[] acronyms)
        {
            var context = new ScoreMultiplierContext(new BeatmapDifficulty());

            double maniaMultiplier = mania.CreateScoreMultiplierCalculator(context).CalculateFor(acronyms.Select(a => mania.CreateModFromAcronym(a)!));
            double orthoMultiplier = ortho.CreateScoreMultiplierCalculator(context).CalculateFor(acronyms.Select(a => ortho.CreateModFromAcronym(a)!));

            Assert.That(orthoMultiplier, Is.EqualTo(maniaMultiplier));
        }

        [Test]
        public void TestFlashlightScoresAsMania()
        {
            var maniaFlashlight = (ManiaModFlashlight)mania.CreateModFromAcronym("FL")!;
            var orthoFlashlight = (ManiaModFlashlight)ortho.CreateModFromAcronym("FL")!;

            Assert.That(orthoFlashlight.Ranked, Is.EqualTo(maniaFlashlight.Ranked));
            Assert.That(orthoFlashlight.Type, Is.EqualTo(maniaFlashlight.Type));
            Assert.That(orthoFlashlight.IncompatibleMods, Is.EqualTo(maniaFlashlight.IncompatibleMods));
            Assert.That(orthoFlashlight.SizeMultiplier.Default, Is.EqualTo(maniaFlashlight.SizeMultiplier.Default));
            Assert.That(orthoFlashlight.DefaultFlashlightSize, Is.EqualTo(maniaFlashlight.DefaultFlashlightSize));
        }

        private static IEnumerable<Mod> flatten(IEnumerable<Mod> mods)
            => mods.SelectMany(m => m is MultiMod multi ? flatten(multi.Mods) : new[] { m });
    }
}
