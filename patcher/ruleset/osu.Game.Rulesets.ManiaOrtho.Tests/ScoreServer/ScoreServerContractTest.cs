// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Scoring;
using osu.Game.Rulesets.ManiaOrtho.ScoreServer;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.ScoreServer
{
    /// <summary>
    /// What the game sends has every field of the contract's example, and the toast reads the server's answer.
    /// </summary>
    [TestFixture]
    public class ScoreServerContractTest
    {
        private static JObject example(string name) => JObject.Parse(File.ReadAllText(ScoreServerParityTest.ContractPath(name)));

        [Test]
        public void TestUploadHasTheExamplesFields()
        {
            var beatmap = new BeatmapInfo
            {
                MD5Hash = "0123456789abcdef0123456789abcdef",
                Status = BeatmapOnlineStatus.Ranked,
                OnlineID = 123,
                Metadata = new BeatmapMetadata { Artist = "maniaortho tests", Title = "Synthetic 4K", Author = { Username = "maniaortho" } },
                DifficultyName = "800 notes",
                BeatmapSet = new BeatmapSetInfo { OnlineID = 45 },
            };

            var score = new ScoreInfo(beatmap, new ManiaOrthoRuleset().RulesetInfo)
            {
                Mods = new Mod[] { new ManiaModDoubleTime() },
                Date = DateTimeOffset.Now,
                Statistics = { [HitResult.Perfect] = 10, [HitResult.Miss] = 1, [HitResult.Good] = 0 },
                MaximumStatistics = { [HitResult.Perfect] = 11 },
            };

            var upload = ScoreServerPlays.Create(score, beatmap, 4, Guid.NewGuid().ToString(), "2026.1104.0");

            upload.ClientPp = 1;
            upload.ClientStars = 2;
            upload.ClientDifficultyVersion = 3;

            foreach (string column in PlayLogRecorder.SCORE_SERVER_SETTINGS)
                upload.Settings[column] = "x";

            var sent = JObject.Parse(JsonConvert.SerializeObject(upload));
            var expected = example("play-upload.example.json");

            Assert.That(names(sent), Is.EquivalentTo(names(expected)));
            Assert.That(names((JObject)sent["beatmap"]!), Is.EquivalentTo(names((JObject)expected["beatmap"]!)));
            Assert.That(names((JObject)sent["settings"]!), Is.EquivalentTo(names((JObject)expected["settings"]!)));

            Assert.That((string)sent["client"]!, Is.EqualTo("maniaortho"));
            Assert.That((string)sent["beatmap"]!["status"]!, Is.EqualTo("Ranked"));
            Assert.That((int)sent["beatmap"]!["set_online_id"]!, Is.EqualTo(45));
            Assert.That((string)sent["mods"]![0]!["acronym"]!, Is.EqualTo("DT"));
            // Judgements by name, as osu!'s own score JSON keys them (the server reads either spelling); none that did not happen.
            Assert.That(names((JObject)sent["statistics"]!), Is.EquivalentTo(new[] { "Perfect", "Miss" }));

            static string[] names(JObject o) => o.Properties().Select(p => p.Name).ToArray();
        }

        [Test]
        public void TestExampleAnswerReads()
        {
            var result = JsonConvert.DeserializeObject<PlayResult>(File.ReadAllText(ScoreServerParityTest.ContractPath("play-result.example.json")),
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error })!;

            string toast = ScoreServerToast.Describe(new ScoreServerOutcome(ScoreServerOutcomeKind.Sent, result, null), "Song [Hard]");

            Assert.That(toast, Is.EqualTo("Unofficial pp, Song [Hard]: 123.45pp, #3 of your best plays. Total 1,234.56pp (+12.34)."));
        }

        [TestCase("link-start.example.json", typeof(LinkStart))]
        [TestCase("link-status.example.json", typeof(LinkStatus))]
        public void TestLinkExamplesRead(string file, Type type)
        {
            string json = File.ReadAllText(ScoreServerParityTest.ContractPath(file));
            object read = JsonConvert.DeserializeObject(json, type, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error })!;

            Assert.That(JObject.FromObject(read).Properties().Select(p => p.Name), Is.EquivalentTo(JObject.Parse(json).Properties().Select(p => p.Name)));
        }

        [Test]
        public void TestToastSaysWhenThePlayDoesNotCount()
        {
            var result = new PlayResult { Pp = 80, AwardsPp = false, Reason = "Map is Loved, not ranked", TotalPp = 500, TotalPpBefore = 500 };

            Assert.That(ScoreServerToast.Describe(new ScoreServerOutcome(ScoreServerOutcomeKind.Sent, result, null), "Song [Hard]"),
                Is.EqualTo("Unofficial pp, Song [Hard]: 80.00pp if it counted, but map is Loved, not ranked. Total 500.00pp."));
        }

        [Test]
        public void TestToastSaysWhenTheGameAndOsuDisagree()
        {
            var result = new PlayResult
            {
                Pp = 100,
                ClientPp = 98.5,
                AwardsPp = true,
                TopPosition = 1,
                TotalPp = 600,
                TotalPpBefore = 500,
                CalculatorVersion = "2026.730.0",
                LiveCalculatorVersion = "2026.1201.0",
            };

            string toast = ScoreServerToast.Describe(new ScoreServerOutcome(ScoreServerOutcomeKind.Sent, result, null) { SentFromOutbox = 2 }, "Song");

            Assert.That(toast, Does.Contain("The game showed 98.50pp: its copy of the calculator differs from the server's (2026.730.0)."));
            Assert.That(toast, Does.Contain("osu! now rates with 2026.1201.0: update the score server (server/scripts/server-update)."));
            Assert.That(toast, Does.EndWith("2 earlier plays sent too."));
        }

        [Test]
        public void TestToastWhenQueued()
        {
            var outcome = new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, "not reachable at 127.0.0.1:7727 (Connection refused)") { Queued = 3 };

            Assert.That(ScoreServerToast.Describe(outcome, "Song"),
                Is.EqualTo("Score server: not reachable at 127.0.0.1:7727 (Connection refused). Song is kept and sent with a later play once the server "
                           + "takes it (3 plays waiting)."));
        }

        [Test]
        public void TestToastWhenRefused()
        {
            var kept = new ScoreServerOutcome(ScoreServerOutcomeKind.Refused, null, "Beatmap checksum mismatch.") { SetAside = true };
            var notKept = new ScoreServerOutcome(ScoreServerOutcomeKind.Refused, null, "this map's file is not in osu!'s file store");

            Assert.That(ScoreServerToast.Describe(kept, "Song"), Is.EqualTo("Score server: Song was not sent: Beatmap checksum mismatch. It is kept in \"outbox refused\"."));
            Assert.That(ScoreServerToast.Describe(notKept, "Song"), Is.EqualTo("Score server: Song was not sent: this map's file is not in osu!'s file store."));
        }

        /// <summary>
        /// The three settings enums are stored under the same ruleset by member name: a shared name would be one setting.
        /// </summary>
        [Test]
        public void TestSettingNamesDoNotClash()
        {
            string[] all = Enum.GetNames<ManiaRulesetSetting>().Concat(Enum.GetNames<HighwaySetting>()).Concat(Enum.GetNames<ScoreServerSetting>()).ToArray();

            Assert.That(all, Is.Unique);
        }
    }
}
