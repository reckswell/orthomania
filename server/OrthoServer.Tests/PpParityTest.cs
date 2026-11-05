using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Game.Online.API;
using OrthoServer.Scoring;

namespace OrthoServer.Tests
{
    /// <summary>
    /// The server (osu!'s published calculators, at the live version) rates the contract's synthetic plays exactly as the
    /// game's own calculators did when <c>pp-parity.json</c> was written. If this fails after a calculator bump, osu!'s live
    /// mania pp and the game's copy differ: the game's in-game pp will not match the server's until the game catches up.
    /// </summary>
    [TestFixture]
    public class PpParityTest
    {
        public static IEnumerable<TestCaseData> Plays()
        {
            var fixture = JObject.Parse(File.ReadAllText(TestPlays.ContractPath("pp-parity.json")));

            foreach (var entry in (JArray)fixture["plays"]!)
                yield return new TestCaseData(entry).SetName($"Parity: {entry["name"]}");
        }

        [TestCaseSource(nameof(Plays))]
        public void TestServerRatesLikeTheGame(JToken entry)
        {
            Assert.That(entry["pp"]?.Type, Is.EqualTo(JTokenType.Float).Or.EqualTo(JTokenType.Integer),
                "pp-parity.json has no expected values yet: run the game's ScoreServerParityTest with ORTHO_WRITE_PP_PARITY=1.");

            byte[] map = Encoding.UTF8.GetBytes(SyntheticManiaMap.Create(
                (int)entry["keys"]!, (int)entry["notes"]!, (double)entry["bpm"]!, (double)entry["overall_difficulty"]!));

            var play = new PlayRecord
            {
                Status = "Ranked",
                Passed = true,
                Mods = entry["mods"]!.ToObject<List<APIMod>>()!,
                Statistics = entry["statistics"]!.ToObject<Dictionary<string, int>>()!,
            };

            PlayCalculator.Rate(play, map);

            Assert.That(play.Stars, Is.EqualTo((double)entry["stars"]!).Within(1e-6), "star rating");
            Assert.That(play.Pp, Is.EqualTo((double)entry["pp"]!).Within(1e-6), "pp");
        }

        [Test]
        public void TestFixtureCoversModsAndKeyCounts()
        {
            var fixture = JObject.Parse(File.ReadAllText(TestPlays.ContractPath("pp-parity.json")));
            var plays = ((JArray)fixture["plays"]!).ToList();

            Assert.That(plays.Select(p => (int)p["keys"]!).Distinct(), Is.EquivalentTo(new[] { 4, 7 }));
            Assert.That(plays.SelectMany(p => p["mods"]!.Select(m => (string)m["acronym"]!)), Is.SupersetOf(new[] { "DT", "HT", "NF", "EZ", "HD" }));
        }
    }
}
