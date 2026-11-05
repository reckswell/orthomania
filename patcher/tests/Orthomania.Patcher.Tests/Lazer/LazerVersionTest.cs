using NUnit.Framework;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Tests.Lazer
{
    [TestFixture]
    public class LazerVersionTest
    {
        [TestCase("2026.1026.0", 2026, 1026, 0, "")]
        [TestCase("v2026.1026.0", 2026, 1026, 0, "")]
        [TestCase("2026.1026.0-lazer", 2026, 1026, 0, "lazer")]
        [TestCase("2026.1026.1-tachyon", 2026, 1026, 1, "tachyon")]
        [TestCase("2026.1026.0.0", 2026, 1026, 0, "")]
        [TestCase(" 2026.905.2 ", 2026, 905, 2, "")]
        public void TestParses(string text, int year, int monthDay, int patch, string suffix)
        {
            Assert.That(LazerVersion.TryParse(text, out var version), Is.True);
            Assert.That(version.Year, Is.EqualTo(year));
            Assert.That(version.MonthDay, Is.EqualTo(monthDay));
            Assert.That(version.Patch, Is.EqualTo(patch));
            Assert.That(version.Suffix, Is.EqualTo(suffix));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("2026.1026")]
        [TestCase("2026.1026.0.1")]
        [TestCase("lazer")]
        [TestCase("2026.x.0")]
        public void TestRefuses(string? text)
        {
            Assert.That(LazerVersion.TryParse(text, out _), Is.False);
        }

        [Test]
        public void TestOrder()
        {
            Assert.That(LazerVersion.Parse("2026.1026.0"), Is.GreaterThan(LazerVersion.Parse("2026.905.3")));
            Assert.That(LazerVersion.Parse("2026.1026.1"), Is.GreaterThan(LazerVersion.Parse("2026.1026.0")));
            Assert.That(LazerVersion.Parse("2027.101.0"), Is.GreaterThan(LazerVersion.Parse("2026.1231.9")));
            Assert.That(LazerVersion.Parse("2026.1026.0") >= LazerVersion.Parse("2026.1026.0"), Is.True);
            Assert.That(LazerVersion.Parse("2026.1026.0") <= LazerVersion.Parse("2026.905.0"), Is.False);
        }

        [Test]
        public void TestSuffixIsNotPartOfTheRelease()
        {
            Assert.That(LazerVersion.Parse("2026.1026.0-lazer"), Is.EqualTo(LazerVersion.Parse("2026.1026.0")));
            Assert.That(LazerVersion.Parse("2026.1026.0-lazer").Number, Is.EqualTo("2026.1026.0"));
            Assert.That(LazerVersion.Parse("2026.1026.0-lazer").ToString(), Is.EqualTo("2026.1026.0-lazer"));
        }
    }
}
