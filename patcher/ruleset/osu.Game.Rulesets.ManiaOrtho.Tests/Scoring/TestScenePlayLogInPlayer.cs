// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.ManiaOrtho.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Scoring
{
    /// <summary>
    /// The play log in a real player: a short chart played with no input under No Fail passes with every note missed, and
    /// that pass is logged with its replay.
    /// </summary>
    [HeadlessTest]
    public partial class TestScenePlayLogInPlayer : PlayerTestScene
    {
        private const int notes = 8;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private Storage log => storage.GetStorageForDirectory(PlayLog.DIRECTORY);

        protected override bool HasCustomSteps => true;

        protected override Ruleset CreatePlayerRuleset() => new ManiaOrthoRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new ManiaBeatmap(new StageDefinition(4))
            {
                BeatmapInfo = new BeatmapInfo
                {
                    Ruleset = new ManiaRuleset().RulesetInfo,
                    Difficulty = new BeatmapDifficulty { CircleSize = 4 },
                    Metadata = new BeatmapMetadata { Artist = "maniaortho", Title = "play log, eight notes" },
                },
            };

            for (int i = 0; i < notes; i++)
                beatmap.HitObjects.Add(new Note { StartTime = 1000 + i * 150, Column = i % 4 });

            return beatmap;
        }

        [Test]
        public void TestPassWithNoInputIsLogged()
        {
            AddStep("no log or replays yet", () =>
            {
                log.Delete(PlayLog.FILENAME);
                log.DeleteDirectory(PlayLog.REPLAY_DIRECTORY);
            });

            CreateTest();

            AddUntilStep("passed (No Fail)", () => Player.GameplayState.HasPassed);
            AddUntilStep("row written", () => Player.ChildrenOfType<PlayLogRecorder>().Single().Writing?.IsCompleted == true);

            AddAssert("one row", () => lines().Length, () => Is.EqualTo(2));
            AddAssert("a pass with every note missed", () => new[] { field("result"), field("mods"), field("miss"), field("perfect"), field("keys") },
                () => Is.EqualTo(new[] { "pass", "NF", notes.ToString(), "0", "4" }));
            AddAssert("its replay is saved", () => log.Exists(field("replay")));
        }

        private string[] lines() => File.ReadAllText(log.GetFullPath(PlayLog.FILENAME)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        private string field(string column) => PlayLogTest.ParseRow(lines().Last())[Array.IndexOf(PlayLog.COLUMNS, column)];
    }
}
