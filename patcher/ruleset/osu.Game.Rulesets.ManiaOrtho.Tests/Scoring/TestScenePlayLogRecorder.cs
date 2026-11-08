// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Replays;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Replays;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.Scoring;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Scoring
{
    /// <summary>
    /// <see cref="PlayLogRecorder"/> adds one row per pass or fail to <c>maniaortho/plays.csv</c>, rated with osu!mania's
    /// calculators, and nothing for quits and replays.
    /// </summary>
    [HeadlessTest]
    public partial class TestScenePlayLogRecorder : OsuTestScene
    {
        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        /// <summary>
        /// The play's input: one key pressed and released.
        /// </summary>
        private static readonly (double time, ManiaAction[] actions)[] replay_frames =
        {
            (0, Array.Empty<ManiaAction>()),
            (1000, new[] { ManiaAction.Key1 }),
            (1100, Array.Empty<ManiaAction>()),
        };

        private BeatmapInfo beatmap = null!;
        private GameplayState state = null!;
        private PlayLogRecorder recorder = null!;
        private readonly Bindable<bool> hasReplayLoaded = new Bindable<bool>();
        private readonly Dictionary<HitResult, int> applied = new Dictionary<HitResult, int>();

        private Storage log => storage.GetStorageForDirectory(PlayLog.DIRECTORY);

        private ManiaOrthoRulesetConfigManager config => (ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(new ManiaOrthoRuleset()).AsNonNull();

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("import the 4K test beatmap", () =>
            {
                var imported = beatmaps.Import(new ImportTask(PlaytestBeatmaps.CreateReal4KArchive(), "maniaortho real 4k.osz")).GetResultSafely().AsNonNull();
                beatmap = imported.PerformRead(set => set.Beatmaps.Single().Detach());
            });

            AddStep("no log, stamp or replays yet", () =>
            {
                log.Delete(PlayLog.FILENAME);
                log.Delete(PlayLogRatings.STAMP_FILENAME);

                foreach (string kept in log.GetFiles(".", "plays before *.csv").ToArray())
                    log.Delete(kept);

                log.DeleteDirectory(PlayLog.REPLAY_DIRECTORY);
            });
            AddStep("Voltex preset, wall in Chunithm's steps", () =>
            {
                config.Highway.ForgetGames(); // each game remembers its settings; start from its own
                config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                config.Highway.SetValue(HighwaySetting.WallScale, WallScale.Chunithm);
            });
            AddStep("not a replay", () => hasReplayLoaded.Value = false);
        }

        [TearDownSteps]
        public void TearDownSteps()
        {
            AddStep("default preset and wall scale", () =>
            {
                config.Highway.GetBindable<WallScale>(HighwaySetting.WallScale).SetDefault();
                config.Highway.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime).SetDefault();
                config.Highway.ForgetGames(); // each game remembers its settings; start from its own
                config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                config.Highway.SetValue(HighwaySetting.Preset, HighwayPresets.DEFAULT);
            });
        }

        [Test]
        public void TestPassIsLoggedWithManiasRating()
        {
            start("DT");
            judge(300);
            AddStep("pass", () => state.HasPassed = true);
            waitForWrite();

            AddAssert("header and one row", () => lines().Length, () => Is.EqualTo(2));
            AddAssert("header", () => lines()[0], () => Is.EqualTo(string.Join(",", PlayLog.COLUMNS)));
            AddAssert("result", () => field("result"), () => Is.EqualTo("pass"));
            AddAssert("map", () => new[] { field("beatmap_md5"), field("keys"), field("difficulty") }, () => Is.EqualTo(new[] { beatmap.MD5Hash, "4", beatmap.DifficultyName }));
            AddAssert("mods", () => field("mods"), () => Is.EqualTo("DT"));
            AddAssert("mod settings kept", () => field("mods_json"), () => Does.Contain("\"acronym\":\"DT\""));
            AddAssert("judgement counts", () => new[] { field("perfect"), field("great"), field("good"), field("miss") },
                () => Is.EqualTo(new[] { HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Miss }.Select(r => applied.GetValueOrDefault(r).ToString(CultureInfo.InvariantCulture))));
            AddAssert("score and accuracy", () => new[] { field("score"), field("accuracy"), field("max_combo") },
                () => Is.EqualTo(new[]
                {
                    state.ScoreProcessor.TotalScore.Value.ToString(CultureInfo.InvariantCulture),
                    (state.ScoreProcessor.Accuracy.Value * 100).ToString("0.0000", CultureInfo.InvariantCulture),
                    state.ScoreProcessor.HighestCombo.Value.ToString(CultureInfo.InvariantCulture),
                }));
            AddAssert("stars and pp are mania's", () => new[] { field("stars"), field("pp") }, () => Is.EqualTo(maniaRating("DT")));
            AddAssert("camera, wall and speed", () => new[]
                {
                    field("preset"), field("camera"), field("wall"), field("wall_scale"), field("lift"), field("wall_keeps_visible_time"), field("scroll_speed")
                },
                () => Is.EqualTo(new[]
                {
                    "Voltex",
                    config.Highway.Get<HighwayCameraMode>(HighwaySetting.CameraMode).ToString(),
                    config.Highway.Get<float>(HighwaySetting.Wall).ToString("0.0000", CultureInfo.InvariantCulture),
                    $"Chunithm {WallScales.ChunithmStep(config.Highway.Get<float>(HighwaySetting.Wall)).ToString(CultureInfo.InvariantCulture)}",
                    config.Highway.Get<float>(HighwaySetting.JudgementLineHeight).ToString("0.000", CultureInfo.InvariantCulture),
                    config.Highway.Get<bool>(HighwaySetting.WallKeepsVisibleTime) ? "yes" : "no",
                    config.Get<double>(ManiaRulesetSetting.ScrollSpeed).ToString(CultureInfo.InvariantCulture),
                }));

            AddAssert("replay saved in the log's folder", () => field("replay"), () => Does.StartWith("replays/").And.EndWith(".osr"));
            AddAssert("it is an osu!mania replay of the play", () =>
            {
                Score replay;

                using (var stream = File.OpenRead(log.GetFullPath(field("replay"))))
                    replay = new DatabasedLegacyScoreDecoder(rulesets, beatmaps).Parse(stream);

                return new object[]
                {
                    replay.ScoreInfo.Ruleset.ShortName,
                    replay.ScoreInfo.BeatmapInfo!.MD5Hash,
                    string.Join(" ", replay.ScoreInfo.Mods.Select(m => m.Acronym)),
                    replay.ScoreInfo.Statistics.GetValueOrDefault(HitResult.Perfect),
                    replay.ScoreInfo.Statistics.GetValueOrDefault(HitResult.Great),
                    replay.ScoreInfo.Statistics.GetValueOrDefault(HitResult.Good),
                    replay.ScoreInfo.Statistics.GetValueOrDefault(HitResult.Miss),
                    replay.Replay.Frames.Count,
                };
            }, () => Is.EqualTo(new object[]
            {
                "mania",
                beatmap.MD5Hash,
                "DT",
                applied.GetValueOrDefault(HitResult.Perfect),
                applied.GetValueOrDefault(HitResult.Great),
                applied.GetValueOrDefault(HitResult.Good),
                applied.GetValueOrDefault(HitResult.Miss),
                replay_frames.Length,
            }));
        }

        /// <summary>
        /// A simulated calculator change (BACKLOG M4): the next play rates the saved plays again, from the beatmap osu! has
        /// and each play's mods and judgement counts.
        /// </summary>
        [Test]
        public void TestPlaysAreRatedAgainAfterACalculatorChange()
        {
            string[] expected = Array.Empty<string>();

            start("DT");
            judge(300);
            AddStep("pass", () => state.HasPassed = true);
            waitForWrite();
            AddAssert("the log is stamped with this build's calculators", () => File.ReadAllText(log.GetFullPath(PlayLogRatings.STAMP_FILENAME)),
                () => Does.Contain(PlayLogRatings.LiveManiaRelease));
            AddStep("note osu!mania's rating of it", () => expected = maniaRating("DT"));

            AddStep("simulate a calculator change: the play rated otherwise, by an older calculator", () =>
            {
                PlayLog.Rewrite(log, plays =>
                {
                    plays[0][Array.IndexOf(PlayLog.COLUMNS, "stars")] = "1.0000";
                    plays[0][Array.IndexOf(PlayLog.COLUMNS, "pp")] = "1.00";
                    return true;
                }, "plays before the test's change.csv");
                File.WriteAllText(log.GetFullPath(PlayLogRatings.STAMP_FILENAME), "an older calculator");
            });

            start();
            judge(50);
            AddStep("fail", () => state.HasFailed = true);
            waitForWrite();

            AddAssert("the first play is rated as osu!mania rates it now", () =>
            {
                string[] first = PlayLogTest.ParseRow(lines()[1]);
                return new[] { first[Array.IndexOf(PlayLog.COLUMNS, "stars")], first[Array.IndexOf(PlayLog.COLUMNS, "pp")] };
            }, () => Is.EqualTo(expected));
            AddAssert("the new play follows it", () => lines().Skip(1).Select(l => PlayLogTest.ParseRow(l)[Array.IndexOf(PlayLog.COLUMNS, "result")]),
                () => Is.EqualTo(new[] { "pass", "fail" }));
            AddAssert("stamped again", () => File.ReadAllText(log.GetFullPath(PlayLogRatings.STAMP_FILENAME)), () => Does.Contain(PlayLogRatings.LiveManiaRelease));
            AddAssert("the old file is kept", () => log.GetFiles(".", "plays before rating again *.csv").Count(), () => Is.EqualTo(1));
        }

        [Test]
        public void TestFailIsLoggedAsF()
        {
            start();
            judge(100);
            AddStep("fail", () => state.HasFailed = true);
            waitForWrite();

            AddAssert("result", () => new[] { field("result"), field("rank") }, () => Is.EqualTo(new[] { "fail", "F" }));
            AddAssert("no mods", () => field("mods"), () => Is.Empty);
        }

        [Test]
        public void TestEachPlayAddsARow()
        {
            start();
            judge(50);
            AddStep("pass", () => state.HasPassed = true);
            waitForWrite();

            start();
            judge(50);
            AddStep("fail", () => state.HasFailed = true);
            waitForWrite();

            AddAssert("header and two rows", () => lines().Length, () => Is.EqualTo(3));
            AddAssert("in order", () => lines().Skip(1).Select(l => PlayLogTest.ParseRow(l)[Array.IndexOf(PlayLog.COLUMNS, "result")]), () => Is.EqualTo(new[] { "pass", "fail" }));
        }

        [Test]
        public void TestQuitsAndReplaysAreNotLogged()
        {
            start();
            judge(50);
            AddStep("quit", () => state.HasQuit = true);
            AddWaitStep("let it update", 5);
            AddAssert("nothing written", () => recorder.Writing, () => Is.Null);

            start();
            AddStep("watching a replay", () => hasReplayLoaded.Value = true);
            judge(50);
            AddStep("replay passes", () => state.HasPassed = true);
            AddWaitStep("let it update", 5);
            AddAssert("nothing written", () => recorder.Writing, () => Is.Null);

            AddAssert("no log", () => log.Exists(PlayLog.FILENAME), () => Is.False);
            AddAssert("no replay", () => log.GetStorageForDirectory(PlayLog.REPLAY_DIRECTORY).GetFiles(".").Count(), () => Is.Zero);
        }

        /// <summary>
        /// A new play of the imported chart under maniaortho, with a recorder.
        /// </summary>
        private void start(params string[] mods)
        {
            AddStep($"start a play ({(mods.Length > 0 ? string.Join(" ", mods) : "no mods")})", () =>
            {
                var ruleset = new ManiaOrthoRuleset();
                var playMods = mods.Select(m => ruleset.CreateModFromAcronym(m).AsNonNull()).ToArray();
                var playable = beatmaps.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, playMods);

                var scoreProcessor = ruleset.CreateScoreProcessor();
                scoreProcessor.Mods.Value = playMods;
                scoreProcessor.ApplyBeatmap(playable);

                var score = new Score
                {
                    ScoreInfo = new ScoreInfo(beatmap, ruleset.RulesetInfo) { Mods = playMods },
                    Replay = new Replay { Frames = replay_frames.Select(f => (ReplayFrame)new ManiaReplayFrame(f.time, f.actions)).ToList() },
                };

                state = new GameplayState(playable, ruleset, playMods, score, scoreProcessor, ruleset.CreateHealthProcessor(0));
                applied.Clear();

                Child = new DependencyProvidingContainer
                {
                    CachedDependencies = new (Type, object)[] { (typeof(GameplayState), state) },
                    Child = recorder = new PlayLogRecorder(hasReplayLoaded, config),
                };
            });
        }

        /// <summary>
        /// Judges the first <paramref name="count"/> notes with a mix of results, as mania would.
        /// </summary>
        private void judge(int count) => AddStep($"judge {count} notes", () =>
        {
            var notes = state.Beatmap.HitObjects.SelectMany(flatten).OfType<Note>().Take(count).ToArray();

            for (int i = 0; i < notes.Length; i++)
            {
                HitResult type = i % 25 == 3 ? HitResult.Miss : i % 10 == 1 ? HitResult.Good : i % 4 == 2 ? HitResult.Great : HitResult.Perfect;

                state.ScoreProcessor.ApplyResult(new JudgementResult(notes[i], notes[i].Judgement) { Type = type });
                applied[type] = applied.GetValueOrDefault(type) + 1;
            }
        });

        private static IEnumerable<HitObject> flatten(HitObject hitObject) => hitObject.NestedHitObjects.SelectMany(flatten).Prepend(hitObject);

        private void waitForWrite() => AddUntilStep("row written", () => recorder.Writing?.IsCompleted == true);

        private string[] lines() => File.ReadAllText(log.GetFullPath(PlayLog.FILENAME)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        private string field(string column) => PlayLogTest.ParseRow(lines().Last())[Array.IndexOf(PlayLog.COLUMNS, column)];

        /// <summary>
        /// osu!mania's star rating and pp for the play so far, from its own calculators on the decoded file.
        /// </summary>
        private string[] maniaRating(params string[] mods)
        {
            var mania = new ManiaRuleset();
            var score = new ScoreInfo(beatmap, mania.RulesetInfo) { Mods = mods.Select(m => mania.CreateModFromAcronym(m).AsNonNull()).ToArray() };
            state.ScoreProcessor.PopulateScore(score);

            var attributes = mania.CreateDifficultyCalculator(new TestWorkingBeatmap(PlaytestBeatmaps.Load(PlaytestBeatmaps.REAL_4K))).Calculate(score.Mods);
            double pp = mania.CreatePerformanceCalculator()!.Calculate(score, attributes).Total;

            return new[]
            {
                attributes.StarRating.ToString("0.0000", CultureInfo.InvariantCulture),
                pp.ToString("0.00", CultureInfo.InvariantCulture),
            };
        }
    }
}
