using System;
using System.IO;
using NUnit.Framework;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher.Tests.Releases
{
    [TestFixture]
    public class OsuCompatibilityTest
    {
        private static readonly Version game = new Version(2026, 1026, 0, 0);
        private static readonly Version framework = new Version(2026, 1019, 0, 0);
        private static readonly Version harmony = new Version(2, 4, 3, 0);

        private TempFolder temp = null!;
        private string osu = null!;
        private string ruleset = null!;
        private string harmonyDll = null!;

        [SetUp]
        public void SetUp()
        {
            temp = new TempFolder();
            osu = temp.Combine("osu");

            Fakes.Assembly(Path.Combine(osu, "osu.Game.dll"), "osu.Game", "game", game);
            Fakes.Assembly(Path.Combine(osu, "osu.Framework.dll"), "osu.Framework", "framework", framework);
            Fakes.Assembly(Path.Combine(osu, "Newtonsoft.Json.dll"), "Newtonsoft.Json", "json", new Version(13, 0, 0, 0));

            ruleset = temp.Combine("build", "osu.Game.Rulesets.ManiaOrtho.dll");
            harmonyDll = temp.Combine("build", "0Harmony.dll");
            Fakes.Assembly(harmonyDll, "0Harmony", "harmony", harmony);
        }

        [TearDown]
        public void TearDown() => temp.Dispose();

        private void writeRuleset(params (string, Version)[] references) => Fakes.Assembly(ruleset, "osu.Game.Rulesets.ManiaOrtho", "ruleset", null, references);

        private bool check(out string? problem) => OsuCompatibility.Check(new[] { ruleset, harmonyDll }, osu, out problem);

        [Test]
        public void TestFitsTheOsuItWasBuiltFor()
        {
            writeRuleset(("osu.Game", game), ("osu.Framework", framework), ("0Harmony", harmony), ("Newtonsoft.Json", new Version(12, 0, 0, 0)),
                ("System.Runtime", new Version(10, 0, 0, 0)));

            Assert.That(check(out string? problem), Is.True, problem);
        }

        [TestCase(2026, 1105, "newer")]
        [TestCase(2026, 1005, "older")]
        public void TestRefusesAnotherOsuGame(int year, int monthDay, string which)
        {
            writeRuleset(("osu.Game", new Version(year, monthDay, 0, 0)));

            Assert.That(check(out string? problem), Is.False, which);
            Assert.That(problem, Is.EqualTo($"osu.Game.Rulesets.ManiaOrtho.dll was built for osu.Game {year}.{monthDay}.0.0, but osu! has 2026.1026.0.0"));
        }

        [Test]
        public void TestRefusesAnotherFramework()
        {
            writeRuleset(("osu.Framework", new Version(2026, 1105, 0, 0)));

            Assert.That(check(out string? problem), Is.False);
            Assert.That(problem, Does.Contain("osu.Framework 2026.1105.0.0"));
        }

        [Test]
        public void TestOtherLibrariesMayBeNewerButNotOlder()
        {
            writeRuleset(("Newtonsoft.Json", new Version(14, 0, 0, 0)));

            Assert.That(check(out string? problem), Is.False);
            Assert.That(problem, Does.Contain("Newtonsoft.Json 14.0.0.0").And.Contain("osu! has 13.0.0.0"));
        }

        [Test]
        public void TestRefusesAnAssemblyOsuDoesNotHave()
        {
            writeRuleset(("Realm", new Version(20, 1, 0, 0)));

            Assert.That(check(out string? problem), Is.False);
            Assert.That(problem, Is.EqualTo("osu.Game.Rulesets.ManiaOrtho.dll needs Realm 20.1.0.0, which is not in osu!'s folder"));
        }

        [Test]
        public void TestRefusesOwnFilesOlderThanNeeded()
        {
            writeRuleset(("0Harmony", new Version(2, 5, 0, 0)));

            Assert.That(check(out string? problem), Is.False);
            Assert.That(problem, Does.Contain("needs 0Harmony 2.5.0.0, but orthomania's is 2.4.3.0"));
        }

        [Test]
        public void TestTheRuntimeComesFromOsuWhenOsuCarriesIt()
        {
            writeRuleset(("System.Runtime", new Version(10, 0, 0, 0)));
            Assert.That(check(out _), Is.True, "osu! without its own runtime uses the computer's");

            Fakes.Assembly(Path.Combine(osu, "System.Private.CoreLib.dll"), "System.Private.CoreLib", "corelib", new Version(10, 0, 0, 0));

            Assert.That(check(out string? problem), Is.False, "a self-contained osu! must have it");
            Assert.That(problem, Does.Contain("System.Runtime"));
        }

        [Test]
        public void TestRefusesADllThatIsNotAnAssembly()
        {
            File.WriteAllText(ruleset, "not a program");

            Assert.That(check(out string? problem), Is.False);
            Assert.That(problem, Does.StartWith("osu.Game.Rulesets.ManiaOrtho.dll"));
        }

        [Test]
        public void TestNeedsExactlyOneRuleset()
        {
            Assert.That(OsuCompatibility.Check(new[] { harmonyDll }, osu, out string? problem), Is.False);
            Assert.That(problem, Is.EqualTo("the build has no ruleset DLL"));
        }
    }
}
