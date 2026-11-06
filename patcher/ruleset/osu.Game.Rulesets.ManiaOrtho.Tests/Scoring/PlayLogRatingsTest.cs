// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.ManiaOrtho.Scoring;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Scoring
{
    /// <summary>
    /// Saved plays are rated again when the calculators that rate them change (BACKLOG M4: a simulated bump rates every
    /// score again).
    /// </summary>
    [TestFixture]
    public class PlayLogRatingsTest
    {
        private const string chart = "chart-md5";
        private const string gone = "deleted-md5";

        private static readonly ManiaOrthoRuleset ruleset = new ManiaOrthoRuleset();

        private int version;

        [SetUp]
        public void SetUp() => version = ruleset.CreateDifficultyCalculator(working()).Version;

        [Test]
        public void TestNewCalculatorsRateEveryPlayAgain()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestNewCalculatorsRateEveryPlayAgain)))
            {
                writeLog(storage, play("1", chart, new ManiaModDoubleTime()), play("2", chart), play("3", gone, new ManiaModDoubleTime()));
                File.WriteAllText(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME), "osu!mania difficulty calculator 1, scoring of osu!mania 2026.101.0");
                string before = File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME));

                var result = PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5);

                Assert.That(counts(result), Is.EqualTo((2, 1, 0)));

                var plays = readLog(storage);
                Assert.That(ratingOf(plays[0]), Is.EqualTo(maniaRating(new ManiaModDoubleTime())), "the first play, rated as osu!mania rates it now");
                Assert.That(ratingOf(plays[1]), Is.EqualTo(maniaRating()), "the second play, rated as osu!mania rates it now");
                Assert.That(ratingOf(plays[2]), Is.EqualTo(new[] { "1.0000", "1.00", "1" }), "a play of a deleted beatmap keeps its values");
                Assert.That(plays.Select(p => p[0]), Is.EqualTo(new[] { "1", "2", "3" }), "the plays stay in order");
                Assert.That(plays[0].Where((_, i) => !rated(i)), Is.EqualTo(play("1", chart, new ManiaModDoubleTime()).Where((_, i) => !rated(i))), "nothing else changes");

                Assert.That(stampOf(storage), Is.EqualTo(new[] { PlayLogRatings.Calculators(version), "3 deleted-md5 912345" }), "rated by these, the third play waiting");

                string[] backups = storage.GetFiles(".", "plays before rating again *.csv").ToArray();
                Assert.That(backups, Has.Length.EqualTo(1));
                Assert.That(File.ReadAllText(storage.GetFullPath(backups[0])), Is.EqualTo(before), "the old file is kept");
            }
        }

        /// <summary>
        /// A play whose beatmap is not in osu! (deleted, or not imported yet) is tried again after each later play, and
        /// rated once its beatmap is back.
        /// </summary>
        [Test]
        public void TestPlayWaitsForItsBeatmap()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestPlayWaitsForItsBeatmap)))
            {
                writeLog(storage, play("1", chart), play("2", gone));
                File.WriteAllText(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME), "older");

                Assert.That(counts(PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5)), Is.EqualTo((1, 1, 0)));
                string afterFirst = File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME));

                Assert.That(counts(PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5)), Is.EqualTo((0, 1, 0)), "tried again");
                Assert.That(File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME)), Is.EqualTo(afterFirst), "nothing to write");
                Assert.That(storage.GetFiles(".", "plays before rating again *.csv").Count(), Is.EqualTo(1), "nor to keep");

                Assert.That(counts(PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, md5 => md5 == gone ? working() : beatmapByMd5(md5))), Is.EqualTo((1, 0, 0)),
                    "rated once its beatmap is back");

                var plays = readLog(storage);
                Assert.That(ratingOf(plays[0]), Is.EqualTo(maniaRating()), "the first play is not rated twice");
                Assert.That(ratingOf(plays[1]), Is.EqualTo(maniaRating()));
                Assert.That(stampOf(storage), Is.EqualTo(new[] { PlayLogRatings.Calculators(version) }));

                Assert.That(PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5), Is.Null, "once is enough");
            }
        }

        /// <summary>
        /// A play ending while the others are rated is added at once (the log is not held), and kept after them.
        /// </summary>
        [Test]
        public void TestPlayEndingMeanwhileIsNotKeptWaiting()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestPlayEndingMeanwhileIsNotKeptWaiting)))
            {
                writeLog(storage, play("1", chart), play("2", chart));
                File.WriteAllText(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME), "older");
                bool added = false;

                var result = PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, md5 =>
                {
                    // Another thread, as the next play's recorder: the log's lock is reentrant.
                    if (!added)
                        added = Task.Run(() => PlayLog.Append(storage, play("3", chart))).Wait(TimeSpan.FromSeconds(10));

                    return beatmapByMd5(md5);
                });

                Assert.That(added, Is.True, "added while the others were rated");
                Assert.That(counts(result), Is.EqualTo((2, 0, 0)));

                var plays = readLog(storage);
                Assert.That(plays.Select(p => p[0]), Is.EqualTo(new[] { "1", "2", "3" }));
                Assert.That(ratingOf(plays[0]), Is.EqualTo(maniaRating()));
                Assert.That(ratingOf(plays[1]), Is.EqualTo(maniaRating()));
                Assert.That(ratingOf(plays[2]), Is.EqualTo(new[] { "1.0000", "1.00", "1" }), "the new play is kept as it was added");
            }
        }

        /// <summary>
        /// A play removed from the log while the others are rated (in a spreadsheet) stays removed, and the rest are rated
        /// where they now are.
        /// </summary>
        [Test]
        public void TestPlayRemovedMeanwhileStaysRemoved()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestPlayRemovedMeanwhileStaysRemoved)))
            {
                writeLog(storage, play("1", chart), play("2", chart));
                File.WriteAllText(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME), "older");
                bool removed = false;

                var result = PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, md5 =>
                {
                    if (!removed)
                        removed = PlayLog.Rewrite(storage, plays => plays.Remove(plays[0]), "edited.csv");

                    return beatmapByMd5(md5);
                });

                Assert.That(removed, Is.True);
                Assert.That(counts(result), Is.EqualTo((2, 0, 0)));

                var plays = readLog(storage);
                Assert.That(plays.Select(p => p[0]), Is.EqualTo(new[] { "2" }));
                Assert.That(ratingOf(plays[0]), Is.EqualTo(maniaRating()));
            }
        }

        /// <summary>
        /// Mods are rated with their settings (here a rate other than Double Time's own), and a play whose mods do not read
        /// back (a mod this build lacks) is kept rather than rated as another play.
        /// </summary>
        [Test]
        public void TestModsAreRatedWithTheirSettings()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestModsAreRatedWithTheirSettings)))
            {
                var faster = new ManiaModDoubleTime();
                faster.SpeedChange.Value = 1.3;

                string[] unknown = play("2", chart);
                unknown[column("mods_json")] = "[{\"acronym\":\"XX\"}]";

                // As a build that orders Double Time's settings otherwise would have written it.
                string[] reordered = play("3", chart, faster);
                reordered[column("mods_json")] = "[{\"acronym\":\"DT\",\"settings\":{\"adjust_pitch\":false,\"speed_change\":1.3}}]";

                writeLog(storage, play("1", chart, faster), unknown, reordered);
                File.WriteAllText(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME), "older");

                Assert.That(counts(PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5)), Is.EqualTo((2, 0, 1)));

                var plays = readLog(storage);
                Assert.That(ratingOf(plays[0]), Is.EqualTo(maniaRating(faster)));
                Assert.That(ratingOf(plays[0]), Is.Not.EqualTo(maniaRating(new ManiaModDoubleTime())), "not Double Time's own rate");
                Assert.That(ratingOf(plays[1]), Is.EqualTo(new[] { "1.0000", "1.00", "1" }), "a mod this build lacks: kept");
                Assert.That(ratingOf(plays[2]), Is.EqualTo(maniaRating(faster)), "settings in another order read back");
            }
        }

        /// <summary>
        /// A log open in a program that locks it: nothing changes, and the next play tries again.
        /// </summary>
        [Test]
        public void TestLockedLogIsTriedAgain()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestLockedLogIsTriedAgain)))
            {
                writeLog(storage, play("1", chart));
                File.WriteAllText(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME), "older");

                using (new FileStream(storage.GetFullPath(PlayLog.FILENAME), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.Throws(Is.InstanceOf<IOException>(), () => PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5));

                Assert.That(stampOf(storage), Is.EqualTo(new[] { "older" }));
                Assert.That(counts(PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5)), Is.EqualTo((1, 0, 0)), "tried again");
            }
        }

        [Test]
        public void TestSameCalculatorsChangeNothing()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestSameCalculatorsChangeNothing)))
            {
                writeLog(storage, play("1", chart, new ManiaModDoubleTime()));
                File.WriteAllText(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME), PlayLogRatings.Calculators(version));
                string before = File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME));

                Assert.That(PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5), Is.Null);
                Assert.That(File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME)), Is.EqualTo(before));
                Assert.That(storage.GetFiles(".", "plays before rating again *.csv"), Is.Empty);
            }
        }

        /// <summary>
        /// A log written before the stamp existed: only plays a different difficulty calculator rated are known to be out
        /// of date.
        /// </summary>
        [Test]
        public void TestUnstampedLogRatesOnlyOtherVersions()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestUnstampedLogRatesOnlyOtherVersions)))
            {
                string[] current = play("1", chart);
                current[column("difficulty_version")] = version.ToString(CultureInfo.InvariantCulture);
                writeLog(storage, current, play("2", chart));

                var result = PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5);

                Assert.That(result!.Rated, Is.EqualTo(1));
                var plays = readLog(storage);
                Assert.That(ratingOf(plays[0]), Is.EqualTo(new[] { "1.0000", "1.00", version.ToString(CultureInfo.InvariantCulture) }), "rated by this version: kept");
                Assert.That(ratingOf(plays[1]), Is.EqualTo(maniaRating()), "rated by another: rated again");
                Assert.That(stampOf(storage), Is.EqualTo(new[] { PlayLogRatings.Calculators(version) }));
            }
        }

        [Test]
        public void TestUnreadablePlayIsKept()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestUnreadablePlayIsKept)))
            {
                string[] broken = play("1", chart);
                broken[column("mods_json")] = "not json";
                writeLog(storage, broken, play("2", chart));
                File.WriteAllText(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME), "older");

                var result = PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5);

                Assert.That(counts(result), Is.EqualTo((1, 0, 1)));
                Assert.That(ratingOf(readLog(storage)[0]), Is.EqualTo(new[] { "1.0000", "1.00", "1" }));
                Assert.That(stampOf(storage), Has.Length.EqualTo(2), "it waits");
                Assert.That(counts(PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5)), Is.EqualTo((0, 0, 1)), "tried again after the next play");
            }
        }

        [Test]
        public void TestNoLogOnlyStamps()
        {
            using (var storage = new TemporaryNativeStorage(nameof(TestNoLogOnlyStamps)))
            {
                var result = PlayLogRatings.UpdateIfOutdated(storage, ruleset, version, beatmapByMd5);

                Assert.That(counts(result), Is.EqualTo((0, 0, 0)));
                Assert.That(storage.Exists(PlayLog.FILENAME), Is.False);
                Assert.That(stampOf(storage), Is.EqualTo(new[] { PlayLogRatings.Calculators(version) }));
            }
        }

        /// <summary>
        /// The build names the release last checked against osu!'s live scoring (patcher/ruleset/live-mania-version.txt), so a bump rates saved plays again.
        /// </summary>
        [Test]
        public void TestReleaseIsTheCheckedOne()
        {
            string? directory = TestContext.CurrentContext.TestDirectory;

            while (directory != null && !File.Exists(Path.Combine(directory, "live-mania-version.txt")))
                directory = Path.GetDirectoryName(directory);

            Assert.That(directory, Is.Not.Null, "the repository is above the test build");
            Assert.That(PlayLogRatings.LiveManiaRelease, Is.EqualTo(File.ReadAllText(Path.Combine(directory!, "live-mania-version.txt")).Trim()));
            Assert.That(PlayLogRatings.Calculators(version), Does.Contain(PlayLogRatings.LiveManiaRelease));
        }

        private static WorkingBeatmap working() => new TestWorkingBeatmap(PlaytestBeatmaps.Load(PlaytestBeatmaps.REAL_4K));

        private static WorkingBeatmap? beatmapByMd5(string md5) => md5 == chart ? working() : null;

        /// <summary>
        /// A logged play of 300 notes, rated stale (1 star, 1 pp, difficulty version 1).
        /// </summary>
        private static string[] play(string time, string md5, params Mod[] mods)
        {
            string[] values = Enumerable.Repeat(string.Empty, PlayLog.COLUMNS.Length).ToArray();

            void set(string name, string value) => values[column(name)] = value;

            set("time", time);
            set("result", "pass");
            set("title", "a title, with a comma");
            set("beatmap_md5", md5);
            set("mods", string.Join(" ", mods.Select(m => m.Acronym)));
            set("mods_json", JsonConvert.SerializeObject(mods.Select(m => new APIMod(m)).ToArray()));
            set("score", "912345");
            set("accuracy", "96.5000");
            set("max_combo", "150");
            set("perfect", "200");
            set("great", "60");
            set("good", "20");
            set("ok", "10");
            set("meh", "4");
            set("miss", "6");
            set("stars", "1.0000");
            set("pp", "1.00");
            set("difficulty_version", "1");
            return values;
        }

        /// <summary>
        /// Stars, pp and difficulty version as osu!mania's own calculators give them for <see cref="play"/>'s counts.
        /// </summary>
        private static string[] maniaRating(params Mod[] maniaMods)
        {
            var mania = new ManiaRuleset();
            var calculator = mania.CreateDifficultyCalculator(working());
            var attributes = calculator.Calculate(maniaMods);
            var score = new ScoreInfo(working().BeatmapInfo, mania.RulesetInfo)
            {
                Mods = maniaMods,
                Statistics = new Dictionary<HitResult, int>
                {
                    [HitResult.Perfect] = 200,
                    [HitResult.Great] = 60,
                    [HitResult.Good] = 20,
                    [HitResult.Ok] = 10,
                    [HitResult.Meh] = 4,
                    [HitResult.Miss] = 6,
                },
            };

            return new[]
            {
                attributes.StarRating.ToString("0.0000", CultureInfo.InvariantCulture),
                mania.CreatePerformanceCalculator()!.Calculate(score, attributes).Total.ToString("0.00", CultureInfo.InvariantCulture),
                calculator.Version.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static (int, int, int) counts(PlayLogRatings.Result? result)
        {
            Assert.That(result, Is.Not.Null, "something to do");
            return (result!.Rated, result.Missing, result.Failed);
        }

        private static string[] stampOf(Storage storage) => File.ReadAllLines(storage.GetFullPath(PlayLogRatings.STAMP_FILENAME));

        private static string[] ratingOf(string[] play) => new[] { play[column("stars")], play[column("pp")], play[column("difficulty_version")] };

        private static bool rated(int index) => index == column("stars") || index == column("pp") || index == column("difficulty_version");

        private static int column(string name) => Array.IndexOf(PlayLog.COLUMNS, name);

        private static void writeLog(Storage storage, params string[][] plays)
        {
            foreach (string[] p in plays)
                PlayLog.Append(storage, p);
        }

        private static List<string[]> readLog(Storage storage) => PlayLog.ParseRows(File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME))).Skip(1).ToList();
    }
}
