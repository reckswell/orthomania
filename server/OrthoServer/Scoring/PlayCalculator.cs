using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace OrthoServer.Scoring
{
    /// <summary>
    /// Rates a play with osu!'s published mania calculators, by the rules osu!'s score processor applies
    /// (<c>ppy/osu-queue-score-statistics</c>, <c>ScorePerformanceProcessor</c>).
    /// </summary>
    public static class PlayCalculator
    {
        private static readonly ManiaRuleset ruleset = new ManiaRuleset();

        /// <summary>
        /// The version of the <c>ppy.osu.Game</c> and <c>ppy.osu.Game.Rulesets.Mania</c> packages this server was built with
        /// (<c>server/calculator-version.txt</c>).
        /// </summary>
        public static string CalculatorVersion { get; } =
            typeof(PlayCalculator).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "OsuCalculatorVersion").Value!;

        /// <summary>
        /// The mania difficulty calculator's own version number, which the game's play log also records.
        /// </summary>
        public static int DifficultyVersion => ruleset.CreateDifficultyCalculator(new FlatWorkingBeatmap(new Beatmap())).Version;

        public static string Md5(byte[] data) => Convert.ToHexStringLower(MD5.HashData(data));

        /// <summary>
        /// Decodes a <c>.osu</c> file. osu!mania maps play natively; osu! maps are converted, as osu!mania converts them.
        /// </summary>
        public static Beatmap Decode(byte[] osuFile)
        {
            using (var stream = new MemoryStream(osuFile))
            using (var reader = new LineBufferedReader(stream))
                return Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        }

        /// <summary>
        /// Sets <paramref name="play"/>'s <see cref="PlayRecord.Pp"/>, <see cref="PlayRecord.Stars"/>,
        /// <see cref="PlayRecord.AwardsPp"/>, <see cref="PlayRecord.Reason"/> and <see cref="PlayRecord.CalculatorVersion"/>.
        /// </summary>
        public static void Rate(PlayRecord play, byte[] osuFile)
        {
            var beatmap = Decode(osuFile);

            if (beatmap.BeatmapInfo.Ruleset.OnlineID != 0 && beatmap.BeatmapInfo.Ruleset.OnlineID != ruleset.RulesetInfo.OnlineID)
                throw new InvalidDataException($"Only osu!mania maps (and osu! maps, converted) can be rated, not mode {beatmap.BeatmapInfo.Ruleset.OnlineID}.");

            beatmap.BeatmapInfo.OnlineID = play.BeatmapId;

            var working = new FlatWorkingBeatmap(beatmap);
            Mod[] mods = createMods(play.Mods);

            var attributes = ruleset.CreateDifficultyCalculator(working).Calculate(mods);

            var score = new ScoreInfo(working.BeatmapInfo, ruleset.RulesetInfo)
            {
                Mods = mods,
                Statistics = parseStatistics(play.Statistics),
                MaximumStatistics = parseStatistics(play.MaximumStatistics),
                MaxCombo = play.MaxCombo,
                Accuracy = play.Accuracy,
                TotalScore = play.TotalScore,
                Passed = play.Passed,
            };

            play.Stars = attributes.StarRating;
            play.Pp = ruleset.CreatePerformanceCalculator()?.Calculate(score, attributes).Total;
            play.Reason = whyNoPp(play, mods);
            play.AwardsPp = play.Reason == null && play.Pp != null;
            play.CalculatorVersion = CalculatorVersion;
        }

        /// <summary>
        /// Why osu! would not award this play pp, or null if it would: <c>ScorePerformanceProcessor.ProcessScoreAsync</c>,
        /// <c>BeatmapStore.IsBeatmapValidForPerformance</c> and <c>AllModsValidForPerformance</c> for a lazer score.
        /// </summary>
        private static string? whyNoPp(PlayRecord play, Mod[] mods)
        {
            if (!play.Passed)
                return "Failed";

            if (!Enum.TryParse(play.Status, true, out BeatmapOnlineStatus status) || (status != BeatmapOnlineStatus.Ranked && status != BeatmapOnlineStatus.Approved))
                return string.IsNullOrEmpty(play.Status) ? "Map status unknown" : $"Map is {play.Status}, not ranked";

            // A lazer score: Classic is not allowed either (it is only for scores imported from osu!stable).
            var unranked = mods.Where(m => !m.Ranked).Select(m => m.Acronym).ToArray();

            if (unranked.Length > 0)
                return $"Mod{(unranked.Length > 1 ? "s" : "")} not awarding pp: {string.Join(", ", unranked)}";

            return null;
        }

        private static Mod[] createMods(IEnumerable<APIMod> mods) => mods.Select(m => m.ToMod(ruleset)).ToArray();

        private static Dictionary<HitResult, int> parseStatistics(Dictionary<string, int> statistics)
        {
            var parsed = new Dictionary<HitResult, int>();

            foreach (var (name, count) in statistics)
            {
                if (!Enum.TryParse(name.Replace("_", string.Empty, StringComparison.Ordinal), true, out HitResult result))
                    throw new InvalidDataException($"Unknown judgement \"{name}\".");

                parsed[result] = count;
            }

            return parsed;
        }
    }
}
