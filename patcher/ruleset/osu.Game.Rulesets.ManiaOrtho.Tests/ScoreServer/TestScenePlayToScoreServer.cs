// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Scoring;
using osu.Game.Rulesets.ManiaOrtho.ScoreServer;
using osu.Game.Rulesets.ManiaOrtho.Tests.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.ScoreServer
{
    /// <summary>
    /// A real play, sent: a synthetic map (stored as osu! stores map files) played with no input under No Fail passes, is
    /// logged, and goes to the score server (here <see cref="FakeScoreServer"/>, over a socket) with its map file and replay.
    /// </summary>
    [HeadlessTest]
    public partial class TestScenePlayToScoreServer : PlayerTestScene
    {
        [Resolved]
        private Storage storage { get; set; } = null!;

        private Storage log => storage.GetStorageForDirectory(PlayLog.DIRECTORY);

        private static readonly byte[] map_file = Encoding.UTF8.GetBytes(SyntheticManiaMap.Create(4, 8, 300, 8));

        private FakeScoreServer? server;

        protected override bool HasCustomSteps => true;

        protected override Ruleset CreatePlayerRuleset() => new ManiaOrthoRuleset();

        private ScoreServerConfigManager config => ((ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(new ManiaOrthoRuleset()).AsNonNull()).ScoreServer;

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = ScoreServerParityTest.Decode(map_file);

            beatmap.BeatmapInfo.Hash = Convert.ToHexStringLower(SHA256.HashData(map_file));
            beatmap.BeatmapInfo.MD5Hash = ScoreServerPlays.Md5(map_file);

            return beatmap;
        }

        [Test]
        public void TestPlayGoesToTheScoreServer()
        {
            AddStep("start score server", () => server = new FakeScoreServer());
            AddStep("map file in osu!'s store", () =>
            {
                string hash = Convert.ToHexStringLower(SHA256.HashData(map_file));

                using (var stream = storage.CreateFileSafely(Path.Combine("files", hash.Remove(1), hash.Remove(2), hash)))
                    stream.Write(map_file);
            });
            AddStep("send plays there", () =>
            {
                config.SetValue(ScoreServerSetting.ScoreServerEnabled, true);
                config.SetValue(ScoreServerSetting.ScoreServerAddress, server!.Address);
                log.DeleteDirectory(ScoreServerClient.OUTBOX_DIRECTORY);
            });

            CreateTest();

            AddUntilStep("passed (No Fail)", () => Player.GameplayState.HasPassed);
            AddUntilStep("logged and sent", () => recorder.Writing?.IsCompleted == true);

            AddAssert("sent and rated", () => recorder.ScoreServerOutcome?.Kind, () => Is.EqualTo(ScoreServerOutcomeKind.Sent));
            AddAssert("one request", () => server!.Requests, () => Has.Count.EqualTo(1));
            AddAssert("the play as logged", () =>
            {
                var sent = JsonConvert.DeserializeObject<PlayUpload>(server!.Requests.Single().PartText("play"))!;

                return new object?[]
                {
                    sent.Client, sent.ScoreId, sent.Passed, sent.Beatmap.Md5, sent.Beatmap.Keys, string.Join(" ", sent.Mods.Select(m => m.Acronym)),
                    sent.Statistics["Miss"].ToString(CultureInfo.InvariantCulture), sent.Settings["preset"],
                };
            }, () => Is.EqualTo(loggedRow()));
            AddAssert("with its map file", () => server!.Requests.Single().Parts["beatmap"], () => Is.EqualTo(map_file));
            AddAssert("and its replay", () => server!.Requests.Single().Parts["replay"].Length, () => Is.GreaterThan(0));
            AddAssert("nothing queued", () => new ScoreServerClient(log).QueuedCount, () => Is.Zero);
        }

        /// <summary>
        /// The same fields, from the play log's last row.
        /// </summary>
        private object?[] loggedRow()
        {
            string[] row = PlayLogTest.ParseRow(File.ReadAllText(log.GetFullPath(PlayLog.FILENAME)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Last());
            return new object?[] { "maniaortho", field(row, "score_id"), true, field(row, "beatmap_md5"), 4, "NF", field(row, "miss"), field(row, "preset") };
        }

        private static string field(string[] row, string column) => row[Array.IndexOf(PlayLog.COLUMNS, column)];

        private PlayLogRecorder recorder => Player.ChildrenOfType<PlayLogRecorder>().Single();

        [TearDownSteps]
        public override void TearDownSteps()
        {
            base.TearDownSteps();

            AddStep("stop sending plays", () =>
            {
                config.SetValue(ScoreServerSetting.ScoreServerEnabled, false);
                config.GetBindable<string>(ScoreServerSetting.ScoreServerAddress).SetDefault();
                server?.Dispose();
                server = null;
            });
        }
    }
}
