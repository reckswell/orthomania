// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for <see cref="HighwayNoteLayer.ColumnColour"/>: native notes take the skin's column colour when it is a
    /// note colour (argon, triangles), and the built-in colours when it is a dark lane background (legacy skins, classic).
    /// </summary>
    [TestFixture]
    public class NoteColourTest
    {
        private static readonly Color4 outer = HighwayNoteLayer.OUTER_COLOUR;
        private static readonly Color4 inner = HighwayNoteLayer.INNER_COLOUR;
        private static readonly Color4 special = HighwayNoteLayer.SPECIAL_COLOUR;

        private static readonly Color4 argon_pink = new Color4(213, 35, 90, 255);

        [Test]
        public void TestBrightColumnColourIsUsed()
        {
            Assert.That(HighwayNoteLayer.ColumnColour(argon_pink, 0, 4, false), Is.EqualTo(argon_pink));
        }

        [Test]
        public void TestUsedColourIsOpaque()
        {
            Assert.That(HighwayNoteLayer.ColumnColour(new Color4(1, 1, 0, 0.8f), 0, 4, false).A, Is.EqualTo(1));
        }

        [TestCase(0, 0, 0, 1, TestName = "Black (classic skin, legacy default)")]
        [TestCase(0, 0, 0, 233 / 255f, TestName = "Alpha's skin: black at 233/255")]
        [TestCase(0.12f, 0.1f, 0.2f, 1, TestName = "Dark lane background")]
        [TestCase(1, 1, 1, 0.1f, TestName = "Faint white lanes")]
        public void TestLaneBackgroundsFallBackToTheBuiltInColours(float r, float g, float b, float a)
        {
            var colour = new Color4(r, g, b, a);
            Assert.That(Enumerable.Range(0, 4).Select(i => HighwayNoteLayer.ColumnColour(colour, i, 4, false)), Is.EqualTo(new[] { outer, inner, inner, outer }));
        }

        [Test]
        public void TestSevenKeys()
        {
            var colours = Enumerable.Range(0, 7).Select(i => HighwayNoteLayer.ColumnColour(Color4.Black, i, 7, i == 3));
            Assert.That(colours, Is.EqualTo(new[] { outer, inner, outer, special, outer, inner, outer }));
        }

        [Test]
        public void TestSixKeys()
        {
            var colours = Enumerable.Range(0, 6).Select(i => HighwayNoteLayer.ColumnColour(Color4.Black, i, 6, false));
            Assert.That(colours, Is.EqualTo(new[] { outer, inner, outer, outer, inner, outer }));
        }
    }
}
