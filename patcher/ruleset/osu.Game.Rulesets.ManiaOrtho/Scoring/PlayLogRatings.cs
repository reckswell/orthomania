// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.ManiaOrtho.Scoring
{
    /// <summary>
    /// Rates the plays in <see cref="PlayLog"/> again (stars and pp) when the calculators that rate them change: osu!mania's
    /// difficulty calculator version, or the osu!mania release whose scoring code this build was checked against
    /// (<c>patcher/ruleset/live-mania-version.txt</c>, embedded). Each row is rated from its
    /// beatmap, its mods with their settings and its judgement counts, as the play itself was, so its pp is what osu!mania
    /// gives that play now.
    /// </summary>
    /// <remarks>
    /// <see cref="STAMP_FILENAME"/>, next to the log, names the calculators that rated it on its first line. A log without
    /// one (written before it existed) has only the rows whose difficulty version differs rated again. A play that cannot be
    /// rated (its beatmap is no longer in osu!, or it does not read back) keeps its values and is listed on the stamp's
    /// further lines, to be tried again after each later play until it is rated. The log is kept as it was under a dated
    /// name before it is rewritten.
    /// </remarks>
    public static class PlayLogRatings
    {
        public const string STAMP_FILENAME = "plays rated by.txt";

        private static readonly object rating_lock = new object();

        /// <summary>
        /// What rating a play again changes.
        /// </summary>
        private static readonly string[] rating_columns = { "stars", "pp", "difficulty_version" };

        /// <summary>
        /// The osu!mania release whose judgement, scoring and difficulty code this build was last checked to match.
        /// </summary>
        public static string LiveManiaRelease { get; } = readLiveManiaRelease();

        /// <summary>
        /// What rates plays in this build, as the stamp names it.
        /// </summary>
        public static string Calculators(int difficultyVersion)
            => $"osu!mania difficulty calculator {difficultyVersion.ToString(CultureInfo.InvariantCulture)}, scoring of osu!mania {LiveManiaRelease}";

        /// <summary>
        /// Rates the log's plays again if other calculators rated them (and those still waiting to be rated), then stamps it
        /// with these. The log's lock is held only to read it and to write it back, so a play ending meanwhile is added at once.
        /// </summary>
        /// <param name="log">The log's folder (<see cref="PlayLog.DIRECTORY"/>).</param>
        /// <param name="ruleset">The ruleset the plays are rated under, maniaortho's (osu!mania's calculators).</param>
        /// <param name="difficultyVersion">The version of the difficulty calculator rating plays now.</param>
        /// <param name="beatmapByMd5">The beatmap of a play, by the MD5 of its file, or null when osu! no longer has it.</param>
        /// <returns>
        /// What was done, or null when there was nothing to do: the plays were rated by these calculators already, or an
        /// earlier play is rating them now (its rating covers this one's, which these calculators rated).
        /// </returns>
        /// <exception cref="IOException">The log or the stamp could not be read or written (open in another program); the
        /// stamp is left as it was, so the next play tries again.</exception>
        public static Result? UpdateIfOutdated(Storage log, Ruleset ruleset, int difficultyVersion, Func<string, WorkingBeatmap?> beatmapByMd5)
        {
            if (!Monitor.TryEnter(rating_lock))
                return null;

            try
            {
                return update(log, ruleset, difficultyVersion, beatmapByMd5);
            }
            finally
            {
                Monitor.Exit(rating_lock);
            }
        }

        private static Result? update(Storage log, Ruleset ruleset, int difficultyVersion, Func<string, WorkingBeatmap?> beatmapByMd5)
        {
            string calculators = Calculators(difficultyVersion);
            string stampPath = log.GetFullPath(STAMP_FILENAME, true);
            string[] stamp = File.Exists(stampPath) ? File.ReadAllLines(stampPath) : Array.Empty<string>();
            string? ratedBy = stamp.Length > 0 ? stamp[0].Trim() : null;
            var waiting = new HashSet<string>(stamp.Skip(1).Select(line => line.Trim()).Where(line => line.Length > 0));

            bool sameCalculators = ratedBy == calculators;

            if (sameCalculators && waiting.Count == 0)
                return null;

            string version = difficultyVersion.ToString(CultureInfo.InvariantCulture);

            // Read and rated without the log's lock: rating every play can take minutes, and a play ending meanwhile must
            // not wait for it (or be lost when the game closes).
            List<string[]> plays = PlayLog.ReadPlays(log) ?? new List<string[]>();
            var rated = new Dictionary<string, string[]>();
            var stillWaiting = new List<string>();
            var result = new Result();

            foreach (string[] play in plays)
            {
                bool due = sameCalculators
                    ? waiting.Contains(keyOf(play))
                    // Without a stamp, only plays a different difficulty calculator rated are known to be out of date.
                    : ratedBy != null || field(play, "difficulty_version") != version;

                if (!due)
                    continue;

                switch (rate(play, ruleset, beatmapByMd5))
                {
                    case true:
                        rated[keyOf(play)] = play;
                        result.Rated++;
                        break;

                    case false:
                        result.Failed++;
                        stillWaiting.Add(keyOf(play));
                        break;

                    default:
                        result.Missing++;
                        stillWaiting.Add(keyOf(play));
                        break;
                }
            }

            string backup = $"plays before rating again {DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture)}.csv";

            if (result.Rated > 0)
            {
                bool written = PlayLog.Rewrite(log, current =>
                {
                    bool changed = false;

                    // By key, not by place: plays added since (rated by these calculators already) are not among them, and
                    // a play removed meanwhile (in a spreadsheet) is gone, not rated.
                    foreach (string[] play in current)
                    {
                        if (!rated.TryGetValue(keyOf(play), out string[]? ratedPlay))
                            continue;

                        foreach (string column in rating_columns)
                            set(play, column, field(ratedPlay, column));

                        changed = true;
                    }

                    return changed;
                }, backup);

                // Replaced meanwhile (by a log of other columns, say): rated again from the start after the next play.
                if (!written)
                    return result;
            }

            File.WriteAllLines(stampPath, stillWaiting.Prepend(calculators));

            if (result.Rated + result.Missing + result.Failed > 0)
            {
                string summary = $"{PlayLog.DIRECTORY}/{PlayLog.FILENAME}: {result.Rated} play(s) rated again by {calculators} (before: {ratedBy ?? "unstamped"})"
                                 + (result.Rated > 0 ? $", the old file kept as {backup}" : string.Empty)
                                 + $"; {result.Missing} waiting for their beatmap to be back in osu!, {result.Failed} could not be read (see the log above)";

                // Said once, when the calculators change; tries after later plays only go to the log file.
                Logger.Log(summary, level: result.Failed > 0 && !sameCalculators ? LogLevel.Important : LogLevel.Verbose);
            }

            return result;
        }

        /// <summary>
        /// Rates one play again in place.
        /// </summary>
        /// <returns>True when rated, null when its beatmap is missing, false when rating it failed (it is kept).</returns>
        private static bool? rate(string[] play, Ruleset ruleset, Func<string, WorkingBeatmap?> beatmapByMd5)
        {
            try
            {
                var working = beatmapByMd5(field(play, "beatmap_md5"));

                if (working == null)
                    return null;

                APIMod[] stored = JsonConvert.DeserializeObject<APIMod[]>(field(play, "mods_json")) ?? Array.Empty<APIMod>();
                Mod[] mods = stored.Select(m => m.ToMod(ruleset)).ToArray();

                // A mod this build does not have, or a setting it no longer takes, would rate another play than the one played.
                if (!readBack(stored, mods))
                    throw new InvalidDataException($"its mods ({field(play, "mods_json")}) do not all read back with their settings");

                // Read before the calculators run, so a row that cannot be read fails at once each time it is tried.
                var statistics = new Dictionary<HitResult, int>
                {
                    [HitResult.Perfect] = number(play, "perfect"),
                    [HitResult.Great] = number(play, "great"),
                    [HitResult.Good] = number(play, "good"),
                    [HitResult.Ok] = number(play, "ok"),
                    [HitResult.Meh] = number(play, "meh"),
                    [HitResult.Miss] = number(play, "miss"),
                };
                int maxCombo = number(play, "max_combo");
                double accuracy = double.Parse(field(play, "accuracy"), CultureInfo.InvariantCulture) / 100;
                long totalScore = long.Parse(field(play, "score"), CultureInfo.InvariantCulture);

                var calculator = ruleset.CreateDifficultyCalculator(working);
                var attributes = calculator.Calculate(mods);

                // The play's maxima, as its score processor had them (as the results screen's score does).
                var processor = ruleset.CreateScoreProcessor();
                processor.Mods.Value = mods;
                processor.ApplyBeatmap(working.GetPlayableBeatmap(ruleset.RulesetInfo, mods));

                var score = new ScoreInfo(working.BeatmapInfo, ruleset.RulesetInfo)
                {
                    Mods = mods,
                    Passed = field(play, "result") == "pass",
                    MaxCombo = maxCombo,
                    Accuracy = accuracy,
                    TotalScore = totalScore,
                    Statistics = statistics,
                    MaximumStatistics = processor.MaximumStatistics,
                };

                set(play, "stars", attributes.StarRating.ToString("0.0000", CultureInfo.InvariantCulture));
                set(play, "pp", ruleset.CreatePerformanceCalculator()?.Calculate(score, attributes).Total.ToString("0.00", CultureInfo.InvariantCulture));
                set(play, "difficulty_version", calculator.Version.ToString(CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception e)
            {
                // Only to the log file: a calculator change can fail many plays at once (one line says so, above).
                Logger.Log($"A play in {PlayLog.DIRECTORY}/{PlayLog.FILENAME} could not be rated again ({field(play, "time")}, {field(play, "title")}): {e}");
                return false;
            }
        }

        /// <summary>
        /// Whether each mod is the stored one: a mod this build has, with every stored setting taken as it was. Values are
        /// compared, not text, so a change in the order or defaults of a mod's settings does not fail a play.
        /// </summary>
        private static bool readBack(APIMod[] stored, Mod[] mods)
        {
            for (int i = 0; i < stored.Length; i++)
            {
                if (mods[i] is UnknownMod || mods[i].Acronym != stored[i].Acronym)
                    return false;

                foreach (var (name, value) in stored[i].Settings)
                {
                    var property = mods[i].GetSettingsSourceProperties().Select(p => p.Item2).FirstOrDefault(p => p.Name.ToSnakeCase() == name);

                    if (property == null || JsonConvert.SerializeObject(property.GetValue(mods[i])!.GetUnderlyingSettingValue()) != JsonConvert.SerializeObject(value))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Names a play on the stamp's list of plays still to rate: its time, beatmap, score and score ID.
        /// </summary>
        private static string keyOf(string[] play) => $"{field(play, "time")} {field(play, "beatmap_md5")} {field(play, "score")} {field(play, "score_id")}".Trim();

        private static string field(string[] play, string column)
        {
            int index = Array.IndexOf(PlayLog.COLUMNS, column);
            return index < play.Length ? play[index] : string.Empty;
        }

        private static int number(string[] play, string column) => int.Parse(field(play, column), CultureInfo.InvariantCulture);

        private static void set(string[] play, string column, string? value) => play[Array.IndexOf(PlayLog.COLUMNS, column)] = value ?? string.Empty;

        private static string readLiveManiaRelease()
        {
            using (var stream = typeof(PlayLogRatings).Assembly.GetManifestResourceStream("live-mania-version.txt"))
            {
                if (stream == null)
                    return "unknown";

                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd().Trim();
            }
        }

        public class Result
        {
            /// <summary>
            /// Plays rated again.
            /// </summary>
            public int Rated { get; set; }

            /// <summary>
            /// Plays whose beatmap osu! no longer has, kept as they were and tried again after the next play.
            /// </summary>
            public int Missing { get; set; }

            /// <summary>
            /// Plays that could not be rated (an unreadable row, or a mod this build lacks), kept as they were and tried
            /// again after the next play.
            /// </summary>
            public int Failed { get; set; }
        }
    }
}
