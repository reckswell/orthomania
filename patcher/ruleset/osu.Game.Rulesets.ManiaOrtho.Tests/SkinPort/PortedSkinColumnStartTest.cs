// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.ManiaOrtho.SkinPort;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.SkinPort
{
    /// <summary>
    /// The porter reads <c>ColumnStart</c> (where stable puts the stage) from osu!'s own parse of a skin's <c>skin.ini</c>.
    /// </summary>
    [TestFixture]
    public class PortedSkinColumnStartTest
    {
        [TestCase("ColumnStart: 200", 200f)] // R Skin's 4K: a stage on the left.
        [TestCase("ColumnStart: 290.5", 290.5f)]
        [TestCase("ColumnStart: 136", null)] // stable's default: centred.
        [TestCase("", null)]
        public void TestColumnStartIsRead(string line, float? expected)
        {
            var skin = new IniSkin($"[General]\nName: test\n\n[Mania]\nKeys: 4\n{line}\n");

            Assert.That(PortedSkin.ReadColumnStart(skin, 4), Is.EqualTo(expected));
        }

        [Test]
        public void TestOnlyTheKeyCountPlayedCounts()
        {
            var skin = new IniSkin("[General]\nName: test\n\n[Mania]\nKeys: 4\nColumnStart: 200\n\n[Mania]\nKeys: 7\nColumnStart: 250\n");

            Assert.That(PortedSkin.ReadColumnStart(skin, 4), Is.EqualTo(200));
            Assert.That(PortedSkin.ReadColumnStart(skin, 7), Is.EqualTo(250));
            Assert.That(PortedSkin.ReadColumnStart(skin, 5), Is.Null, "no [Mania] section for 5K");
        }

        /// <summary>
        /// The side a skin puts an N+1 map's special column on, per key count (research/game-djmax.md R3).
        /// </summary>
        [TestCase("SpecialStyle: 1", LegacyManiaSkinConfiguration.LegacySpecialStyle.Left)]
        [TestCase("SpecialStyle: 2", LegacyManiaSkinConfiguration.LegacySpecialStyle.Right)]
        [TestCase("SpecialStyle: 0", LegacyManiaSkinConfiguration.LegacySpecialStyle.None)]
        [TestCase("", null)]
        public void TestSpecialStyleIsRead(string line, LegacyManiaSkinConfiguration.LegacySpecialStyle? expected)
        {
            var skin = new IniSkin($"[General]\nName: test\n\n[Mania]\nKeys: 8\n{line}\n");

            Assert.That(PortedSkin.ReadSpecialStyle(skin, 8), Is.EqualTo(expected));
            Assert.That(PortedSkin.ReadSpecialStyle(skin, 6), Is.Null, "no [Mania] section for 6K");
        }

        /// <summary>
        /// A legacy skin parsed from a <c>skin.ini</c> held in memory.
        /// </summary>
        private class IniSkin : LegacySkin
        {
            public IniSkin(string ini)
                : base(new SkinInfo { Name = "test" }, null, new IniStore(ini))
            {
            }
        }

        private class IniStore : IResourceStore<byte[]>
        {
            private const string file_name = "skin.ini";

            private readonly byte[] ini;

            public IniStore(string ini)
            {
                this.ini = Encoding.UTF8.GetBytes(ini);
            }

            public byte[] Get(string name) => name == file_name ? ini : null!;

            public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

            public Stream GetStream(string name) => name == file_name ? new MemoryStream(ini) : null!;

            public IEnumerable<string> GetAvailableResources() => new[] { file_name };

            public void Dispose()
            {
            }
        }
    }
}
