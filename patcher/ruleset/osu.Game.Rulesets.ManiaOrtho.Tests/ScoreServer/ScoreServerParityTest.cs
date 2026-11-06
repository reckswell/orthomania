// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.ScoreServer
{
    /// <summary>
    /// The game's calculators (this repository's osu!mania, through <see cref="ManiaOrthoRuleset"/>) rate the score server's
    /// contract plays (<c>server/contract/pp-parity.json</c>) as the file says. The server's tests check that osu!'s
    /// published calculators give the same values, so together they pin the in-game pp to the server's.
    /// With <c>ORTHO_WRITE_PP_PARITY=1</c> this writes the values instead (after a calculator change on purpose).
    /// </summary>
    [TestFixture]
    public class ScoreServerParityTest
    {
        private const string write_variable = "ORTHO_WRITE_PP_PARITY";

        private static bool writing => Environment.GetEnvironmentVariable(write_variable) == "1";

        [Test]
        public void TestGameRatesContractPlaysAsTheFileSays()
        {
            string path = ContractPath("pp-parity.json");
            var fixture = JObject.Parse(File.ReadAllText(path));
            var plays = (JArray)fixture["plays"]!;

            Assert.That(plays, Is.Not.Empty);

            foreach (var entry in plays)
            {
                (double stars, double pp) = Rate(entry);

                if (writing)
                {
                    entry["stars"] = stars;
                    entry["pp"] = pp;
                    continue;
                }

                Assert.That(entry["pp"]?.Type, Is.EqualTo(JTokenType.Float).Or.EqualTo(JTokenType.Integer),
                    $"{path} has no expected values yet: run this test with {write_variable}=1.");

                Assert.Multiple(() =>
                {
                    Assert.That(stars, Is.EqualTo((double)entry["stars"]!).Within(1e-6), $"{entry["name"]}: star rating");
                    Assert.That(pp, Is.EqualTo((double)entry["pp"]!).Within(1e-6), $"{entry["name"]}: pp");
                });
            }

            if (writing)
                File.WriteAllText(path, fixture.ToString(Formatting.Indented) + "\n");
        }

        [Test]
        public void TestContractPlaysAreWholePlays()
        {
            foreach (var entry in (JArray)JObject.Parse(File.ReadAllText(ContractPath("pp-parity.json")))["plays"]!)
            {
                var beatmap = Decode(Map(entry));
                var playable = new FlatWorkingBeatmap(beatmap).GetPlayableBeatmap(new ManiaOrthoRuleset().RulesetInfo, mods(entry));

                // A note is judged once, a hold twice (its head and tail; its body and the hold itself count towards no statistic).
                int judged = playable.HitObjects.SelectMany(h => h.NestedHitObjects.Prepend(h)).Count(h => h.Judgement.MaxResult.AffectsAccuracy());
                int counted = entry["statistics"]!.ToObject<Dictionary<string, int>>()!.Values.Sum();

                Assert.That(counted, Is.EqualTo(judged), $"{entry["name"]}: judgements");
            }
        }

        /// <summary>
        /// Stars and pp as the game's play log works them out for this play (<c>PlayLogRecorder</c>).
        /// </summary>
        public static (double Stars, double Pp) Rate(JToken entry)
        {
            var ruleset = new ManiaOrthoRuleset();
            var working = new FlatWorkingBeatmap(Decode(Map(entry)));
            Mod[] playMods = mods(entry);

            var attributes = ruleset.CreateDifficultyCalculator(working).Calculate(playMods);

            var score = new ScoreInfo(working.BeatmapInfo, ruleset.RulesetInfo)
            {
                Mods = playMods,
                Statistics = entry["statistics"]!.ToObject<Dictionary<string, int>>()!.ToDictionary(s => Enum.Parse<HitResult>(s.Key), s => s.Value),
                Passed = true,
            };

            return (attributes.StarRating, ruleset.CreatePerformanceCalculator().Calculate(score, attributes).Total);
        }

        public static byte[] Map(JToken entry) => System.Text.Encoding.UTF8.GetBytes(SyntheticManiaMap.Create(
            (int)entry["keys"]!, (int)entry["notes"]!, (double)entry["bpm"]!, (double)entry["overall_difficulty"]!));

        public static Beatmap Decode(byte[] osuFile)
        {
            using (var stream = new MemoryStream(osuFile))
            using (var reader = new LineBufferedReader(stream))
                return Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        }

        /// <summary>
        /// A file of <c>server/contract</c>, found from the test's build output.
        /// </summary>
        public static string ContractPath(string name)
        {
            for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "server", "contract", name);

                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException($"server/contract/{name} was not found above {TestContext.CurrentContext.TestDirectory}.");
        }

        private static Mod[] mods(JToken entry)
        {
            var ruleset = new ManiaOrthoRuleset();
            return entry["mods"]!.ToObject<List<APIMod>>()!.Select(m => m.ToMod(ruleset)).ToArray();
        }
    }
}
