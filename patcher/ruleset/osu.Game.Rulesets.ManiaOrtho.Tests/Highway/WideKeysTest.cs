// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.UI;
using LegacySpecialStyle = osu.Game.Skinning.LegacyManiaSkinConfiguration.LegacySpecialStyle;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// L0 checks for the wide-key markings (research/topic-wide-keys.md W1 and W3): hand groups and the note gap's floor.
    /// </summary>
    [TestFixture]
    public class WideKeysTest
    {
        [TestCase(1, new[] { 1 })]
        [TestCase(4, new[] { 4 })]
        [TestCase(5, new[] { 5 })]
        [TestCase(6, new[] { 3, 3 })]
        [TestCase(7, new[] { 3, 1, 3 })]
        [TestCase(8, new[] { 4, 4 })]
        [TestCase(9, new[] { 4, 1, 4 })]
        [TestCase(10, new[] { 5, 5 })]
        public void TestHandGroups(int keys, int[] groups) => Assert.That(groupsOf(keys), Is.EqualTo(groups));

        /// <summary>
        /// An N+1 map's stage is split 1 | N−1 beside its special column (research/game-djmax.md R3).
        /// </summary>
        [TestCase(6, SpecialColumnSide.Left, new[] { 1, 5 })]
        [TestCase(8, SpecialColumnSide.Left, new[] { 1, 7 })]
        [TestCase(8, SpecialColumnSide.Right, new[] { 7, 1 })]
        [TestCase(10, SpecialColumnSide.Right, new[] { 9, 1 })]
        [TestCase(7, SpecialColumnSide.Left, new[] { 1, 6 })] // a stage of 7 + 7
        [TestCase(8, SpecialColumnSide.None, new[] { 4, 4 })]
        public void TestSpecialColumnGroups(int keys, SpecialColumnSide special, int[] groups) => Assert.That(groupsOf(keys, special), Is.EqualTo(groups));

        /// <summary>
        /// Where the special column is: only on N+1 maps (the map's flag) with an even total key count from 6; on the skin's
        /// side, the left when it names none; with two stages the skin's left is the outer columns and its right the inner
        /// ones, whatever each stage's own count.
        /// </summary>
        [TestCase(false, null, 8, 0, 1, SpecialColumnSide.None)]
        [TestCase(true, null, 8, 0, 1, SpecialColumnSide.Left)]
        [TestCase(true, LegacySpecialStyle.None, 8, 0, 1, SpecialColumnSide.Left)]
        [TestCase(true, LegacySpecialStyle.Left, 6, 0, 1, SpecialColumnSide.Left)]
        [TestCase(true, LegacySpecialStyle.Right, 10, 0, 1, SpecialColumnSide.Right)]
        [TestCase(true, LegacySpecialStyle.Right, 7, 0, 1, SpecialColumnSide.None)] // odd: the centre key is special already
        [TestCase(true, null, 4, 0, 1, SpecialColumnSide.None)]
        [TestCase(true, null, 2, 0, 1, SpecialColumnSide.None)]
        [TestCase(true, LegacySpecialStyle.Left, 8, 0, 2, SpecialColumnSide.Left)] // outer
        [TestCase(true, LegacySpecialStyle.Left, 8, 1, 2, SpecialColumnSide.Right)]
        [TestCase(true, LegacySpecialStyle.Right, 8, 0, 2, SpecialColumnSide.Right)] // inner
        [TestCase(true, LegacySpecialStyle.Right, 8, 1, 2, SpecialColumnSide.Left)]
        [TestCase(true, null, 14, 0, 2, SpecialColumnSide.Left)] // 7 + 7
        [TestCase(true, null, 14, 1, 2, SpecialColumnSide.Right)]
        [TestCase(true, LegacySpecialStyle.Right, 18, 0, 2, SpecialColumnSide.Right)] // 9 + 9, inner
        [TestCase(true, LegacySpecialStyle.Right, 18, 1, 2, SpecialColumnSide.Left)]
        [TestCase(true, null, 10, 0, 2, SpecialColumnSide.Left)] // 5 + 5
        public void TestSpecialSide(bool specialStyle, LegacySpecialStyle? skinStyle, int totalColumns, int stage, int stageCount, SpecialColumnSide expected)
            => Assert.That(LaneGroups.SpecialSide(specialStyle, skinStyle, totalColumns, stage, stageCount), Is.EqualTo(expected));

        [Test]
        public void TestGapIsAShareOfTheLaneWhereThatIsAPixel()
        {
            // A 4K lane under Chunithm at the line: 0.25 of a 1200 px track, so 1% is 3 px.
            Assert.That(HighwayNoteLayer.NoteGap(0.25f, 1200), Is.EqualTo(0.25f * HighwayNoteLayer.NOTE_GAP).Within(1e-7f));
        }

        [Test]
        public void TestGapIsNeverUnderAPixel()
        {
            // A 10K lane at Voltex's far edge: 0.1 of a 90 px track, 9 px, so 1% would be 0.09 px.
            Assert.That(HighwayNoteLayer.NoteGap(0.1f, 90) * 90, Is.EqualTo(HighwayNoteLayer.MIN_GAP_PIXELS).Within(1e-5f));
        }

        [Test]
        public void TestGapLeavesANoteOnATinyLane()
        {
            // A 2 px lane: a pixel each side would leave nothing, so each side takes a quarter.
            Assert.That(HighwayNoteLayer.NoteGap(0.1f, 20), Is.EqualTo(0.025f).Within(1e-7f));
            Assert.That(HighwayNoteLayer.NoteGap(0.1f, 0), Is.EqualTo(0.1f * HighwayNoteLayer.NOTE_GAP).Within(1e-7f), "no screen scale known");
        }

        private static int[] groupsOf(int keys, SpecialColumnSide special = SpecialColumnSide.None)
        {
            var groups = new List<int>();
            int size = 1;

            for (int column = 1; column < keys; column++)
            {
                if (LaneGroups.StartsGroup(keys, column, special))
                {
                    groups.Add(size);
                    size = 0;
                }

                size++;
            }

            groups.Add(size);
            return groups.ToArray();
        }
    }
}
