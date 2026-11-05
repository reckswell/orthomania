using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OrthoServer.Accounts;
using OrthoServer.Scoring;

namespace OrthoServer.Lazer
{
    /// <summary>
    /// What lazer reads from its web API, in the shapes and field names of lazer's own response models
    /// (<c>APIUser</c>, <c>UserStatistics</c>, <c>SoloScoreInfo</c>, <c>APIBeatmap</c>, <c>APIBeatmapSet</c>), built from this
    /// server's accounts and plays. Lists lazer expects are always present (empty), never null.
    /// </summary>
    public static class LazerJson
    {
        /// <summary>
        /// The rulesets lazer shows on a profile. maniaortho plays are rated as osu!mania plays, so they are the user's osu!mania figures.
        /// </summary>
        public static readonly string[] RULESETS = { "osu", "taiko", "fruits", "mania" };

        public const int MANIA_RULESET_ID = 3;

        /// <summary>
        /// Whether a ruleset (by short name) shows this server's plays: osu!mania, and maniaortho itself.
        /// </summary>
        public static bool ShowsPlays(string? ruleset) => ruleset is null or "mania" or "maniaortho";

        /// <summary>
        /// One account's figures, worked out once per request from every play on the server (ranks compare accounts).
        /// </summary>
        public record UserFigures(Account Account, List<PlayRecord> Plays, Profile Profile, int? Rank, Dictionary<int, (double Pp, int? Rank)> Variants);

        public static Dictionary<long, UserFigures> Figures(IReadOnlyList<Account> accounts, IReadOnlyList<PlayRecord> plays)
        {
            var figures = new Dictionary<long, UserFigures>();

            foreach (var account in accounts)
            {
                var own = plays.Where(p => p.UserId == account.Id).ToList();
                var variants = new Dictionary<int, (double, int?)>();

                foreach (int keys in new[] { 4, 7 })
                    variants[keys] = (ProfileCalculator.Calculate(own.Where(p => p.Keys == keys)).TotalPp, null);

                figures[account.Id] = new UserFigures(account, own, ProfileCalculator.Calculate(own), null, variants);
            }

            // Ranks among this server's accounts, by pp, for those with any pp (osu! ranks only active players with pp).
            int rank = 1;

            foreach (var f in figures.Values.Where(f => f.Profile.CountedScores > 0).OrderByDescending(f => f.Profile.TotalPp).ThenBy(f => f.Account.Id).ToList())
                figures[f.Account.Id] = f with { Rank = rank++ };

            foreach (int keys in new[] { 4, 7 })
            {
                rank = 1;

                foreach (var f in figures.Values.Where(f => f.Variants[keys].Pp > 0).OrderByDescending(f => f.Variants[keys].Pp).ThenBy(f => f.Account.Id).ToList())
                    f.Variants[keys] = (f.Variants[keys].Pp, rank++);
            }

            return figures;
        }

        /// <summary>
        /// A user as <c>/me</c> and <c>/users/{id}</c> answer: <c>statistics</c> for <paramref name="ruleset"/>, and every ruleset's
        /// under <c>statistics_rulesets</c>.
        /// </summary>
        public static Dictionary<string, object?> User(UserFigures figures, string? ruleset)
        {
            var user = CompactUser(figures.Account);
            var statistics = RULESETS.ToDictionary(r => r, r => Statistics(figures, r));

            user["statistics"] = Statistics(figures, ruleset);
            user["statistics_rulesets"] = statistics;
            user["rank_history"] = new Dictionary<string, object?> { ["mode"] = ruleset ?? "mania", ["data"] = Array.Empty<int>() };
            user["rank_highest"] = figures.Rank is int rank && ShowsPlays(ruleset)
                ? new Dictionary<string, object?> { ["rank"] = rank, ["updated_at"] = DateTimeOffset.Now }
                : null;

            int best = ShowsPlays(ruleset) ? figures.Profile.Top.Count : 0;
            int recent = ShowsPlays(ruleset) ? figures.Plays.Count(p => p.PlayedAt > DateTimeOffset.Now.AddDays(-1)) : 0;

            user["scores_best_count"] = best;
            user["scores_recent_count"] = recent;
            user["scores_first_count"] = 0;
            user["scores_pinned_count"] = 0;
            user["beatmap_playcounts_count"] = ShowsPlays(ruleset) ? figures.Plays.Select(p => p.BeatmapKey).Distinct().Count() : 0;
            return user;
        }

        /// <summary>
        /// The fields every user object has (as in lazer's friends, chat and score lists), with the lists the profile reads.
        /// </summary>
        public static Dictionary<string, object?> CompactUser(Account account) => new Dictionary<string, object?>
        {
            ["id"] = account.Id,
            ["username"] = account.Username,
            ["join_date"] = account.JoinedAt,
            ["country_code"] = "XX",
            ["country"] = new Dictionary<string, object?> { ["code"] = "XX", ["name"] = "Unknown" },
            // Empty, not missing: lazer then shows its guest avatar without asking for one (a missing one would be fetched from osu!'s
            // a.ppy.sh by this account's id, another person's picture; an address here would be refused and warned about).
            ["avatar_url"] = string.Empty,
            ["cover_url"] = null,
            ["cover"] = new Dictionary<string, object?> { ["custom_url"] = null, ["url"] = null, ["id"] = null },
            ["default_group"] = "default",
            ["is_active"] = true,
            ["is_bot"] = false,
            ["is_deleted"] = false,
            ["is_online"] = true,
            ["is_supporter"] = false,
            ["is_restricted"] = false,
            ["support_level"] = 0,
            ["has_supported"] = false,
            ["pm_friends_only"] = false,
            ["profile_colour"] = null,
            ["playmode"] = "mania",
            ["playstyle"] = new[] { "keyboard" },
            ["profile_order"] = new[] { "me", "recent_activity", "top_ranks", "medals", "historical", "beatmaps", "kudosu" },
            ["previous_usernames"] = Array.Empty<string>(),
            ["badges"] = Array.Empty<object>(),
            ["groups"] = Array.Empty<object>(),
            ["user_achievements"] = Array.Empty<object>(),
            ["monthly_playcounts"] = Array.Empty<object>(),
            ["replays_watched_counts"] = Array.Empty<object>(),
            ["active_tournament_banners"] = Array.Empty<object>(),
            ["account_history"] = Array.Empty<object>(),
            ["kudosu"] = new Dictionary<string, object?> { ["total"] = 0, ["available"] = 0 },
            ["page"] = new Dictionary<string, object?> { ["html"] = string.Empty, ["raw"] = string.Empty },
            ["follower_count"] = 0,
            ["mapping_follower_count"] = 0,
            ["post_count"] = 0,
            ["comments_count"] = 0,
            ["favourite_beatmapset_count"] = 0,
            ["graveyard_beatmapset_count"] = 0,
            ["guest_beatmapset_count"] = 0,
            ["loved_beatmapset_count"] = 0,
            ["nominated_beatmapset_count"] = 0,
            ["pending_beatmapset_count"] = 0,
            ["ranked_beatmapset_count"] = 0,
            ["title"] = null,
            ["location"] = null,
            ["interests"] = null,
            ["occupation"] = null,
            ["twitter"] = null,
            ["discord"] = null,
            ["website"] = null,
            ["last_visit"] = DateTimeOffset.Now,
            ["session_verified"] = true,
            ["session_verification_method"] = null,
        };

        /// <summary>
        /// A user's statistics for one ruleset, as osu! computes them from that user's plays. Rulesets other than osu!mania have none.
        /// </summary>
        public static Dictionary<string, object?> Statistics(UserFigures figures, string? ruleset)
        {
            bool has = ShowsPlays(ruleset);
            var plays = has ? figures.Plays : new List<PlayRecord>();
            var profile = figures.Profile;

            // Ranked score and grades: each map's best passed score on a map that is ranked or approved (any mods).
            var bestOnRanked = plays.Where(p => p.Passed && isRankedStatus(p.Status))
                                    .GroupBy(p => p.BeatmapKey)
                                    .Select(g => g.MaxBy(p => p.TotalScore)!)
                                    .ToList();

            long totalScore = plays.Sum(p => p.TotalScore);
            var (level, progress) = Level(totalScore);

            return new Dictionary<string, object?>
            {
                ["count_300"] = 0,
                ["count_100"] = 0,
                ["count_50"] = 0,
                ["count_miss"] = 0,
                ["level"] = new Dictionary<string, object?> { ["current"] = level, ["progress"] = progress },
                ["global_rank"] = has ? figures.Rank : null,
                ["global_rank_percent"] = null,
                ["country_rank"] = has ? figures.Rank : null,
                ["pp"] = has ? profile.TotalPp : 0,
                ["ranked_score"] = bestOnRanked.Sum(p => p.TotalScore),
                ["hit_accuracy"] = has ? profile.Accuracy : 0,
                ["play_count"] = plays.Count,
                ["play_time"] = 0,
                ["total_score"] = totalScore,
                ["total_hits"] = plays.Sum(p => p.Statistics.Where(s => !isMiss(s.Key)).Sum(s => s.Value)),
                ["maximum_combo"] = plays.Count > 0 ? plays.Max(p => p.MaxCombo) : 0,
                ["replays_watched_by_others"] = 0,
                ["is_ranked"] = has && figures.Rank != null,
                ["grade_counts"] = new Dictionary<string, object?>
                {
                    ["ssh"] = bestOnRanked.Count(p => p.Rank == "XH"),
                    ["ss"] = bestOnRanked.Count(p => p.Rank == "X"),
                    ["sh"] = bestOnRanked.Count(p => p.Rank == "SH"),
                    ["s"] = bestOnRanked.Count(p => p.Rank == "S"),
                    ["a"] = bestOnRanked.Count(p => p.Rank == "A"),
                },
                ["variants"] = has
                    ? figures.Variants.Select(v => new Dictionary<string, object?>
                    {
                        ["mode"] = "mania",
                        ["variant"] = $"{v.Key}k",
                        ["pp"] = v.Value.Pp,
                        ["global_rank"] = v.Value.Rank,
                        ["country_rank"] = v.Value.Rank,
                    }).ToArray()
                    : Array.Empty<object>(),
            };
        }

        /// <summary>
        /// A stored play as a lazer score (<c>SoloScoreInfo</c>), with its map, its set and its owner.
        /// </summary>
        public static Dictionary<string, object?> Score(PlayRecord play, Account? owner, double? weight = null)
        {
            var score = new Dictionary<string, object?>
            {
                ["id"] = play.Id,
                ["type"] = "solo_score",
                ["user_id"] = play.UserId ?? 0,
                ["beatmap_id"] = play.BeatmapId,
                ["ruleset_id"] = MANIA_RULESET_ID,
                ["build_id"] = null,
                ["passed"] = play.Passed,
                ["total_score"] = play.TotalScore,
                ["total_score_without_mods"] = play.TotalScore,
                ["accuracy"] = play.Accuracy,
                ["max_combo"] = play.MaxCombo,
                ["rank"] = play.Passed ? play.Rank ?? "D" : "F",
                ["started_at"] = null,
                ["ended_at"] = play.PlayedAt,
                ["mods"] = play.Mods,
                ["statistics"] = hitResults(play.Statistics),
                ["maximum_statistics"] = hitResults(play.MaximumStatistics),
                ["legacy_total_score"] = 0,
                ["legacy_score_id"] = null,
                ["pp"] = play.AwardsPp ? play.Pp : null,
                ["has_replay"] = false,
                ["ranked"] = play.AwardsPp,
                ["preserve"] = true,
                ["processed"] = true,
                ["beatmap"] = Beatmap(play),
                ["beatmapset"] = BeatmapSet(play),
                ["user"] = owner != null ? CompactUser(owner) : null,
            };

            if (weight is double w && play.Pp is double pp)
                score["weight"] = new Dictionary<string, object?> { ["percentage"] = w * 100, ["pp"] = pp * w };

            return score;
        }

        public static Dictionary<string, object?> Beatmap(PlayRecord play) => new Dictionary<string, object?>
        {
            ["id"] = play.BeatmapId,
            ["beatmapset_id"] = play.BeatmapSetId,
            ["version"] = play.Version,
            ["checksum"] = play.BeatmapMd5,
            ["difficulty_rating"] = play.Stars ?? 0,
            ["mode"] = "mania",
            ["mode_int"] = MANIA_RULESET_ID,
            ["ruleset_id"] = MANIA_RULESET_ID,
            ["status"] = play.Status.ToLowerInvariant(),
            ["cs"] = play.Keys,
            ["total_length"] = 0,
            ["user_id"] = 0,
        };

        public static Dictionary<string, object?> BeatmapSet(PlayRecord play) => new Dictionary<string, object?>
        {
            ["id"] = play.BeatmapSetId,
            ["title"] = play.Title,
            ["title_unicode"] = play.Title,
            ["artist"] = play.Artist,
            ["artist_unicode"] = play.Artist,
            ["creator"] = play.Creator,
            ["status"] = play.Status.ToLowerInvariant(),
            ["user_id"] = 0,
            // osu!'s public covers (the game already loads them from assets.ppy.sh). A map without a set has none: an empty object,
            // which lazer reads as no picture (null would not read at all).
            ["covers"] = play.BeatmapSetId > 0 ? covers(play.BeatmapSetId) : new Dictionary<string, object?>(),
        };

        /// <summary>
        /// osu!'s level for a total score (osu! wiki, "Total score": 5000/3 (4n³ - 3n² - n) + 1.25 · 1.8^(n - 60) to reach level n up
        /// to 100, then 99,999,999,999 a level), with the percentage towards the next.
        /// </summary>
        public static (int Level, int Progress) Level(long totalScore)
        {
            static double required(int n) => n <= 100
                ? 5000.0 / 3 * (4.0 * n * n * n - 3.0 * n * n - n) + 1.25 * Math.Pow(1.8, n - 60)
                : 26_931_190_827 + 99_999_999_999.0 * (n - 100);

            int level = 1;

            while (level < 1000 && required(level + 1) <= totalScore)
                level++;

            double from = required(level), to = required(level + 1);
            return (level, (int)Math.Clamp(Math.Floor((totalScore - from) / (to - from) * 100), 0, 99));
        }

        private static bool isRankedStatus(string status) => status.Equals("Ranked", StringComparison.OrdinalIgnoreCase) || status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

        private static bool isMiss(string result) => result.Equals("Miss", StringComparison.OrdinalIgnoreCase) || result.Equals("miss", StringComparison.Ordinal);

        /// <summary>
        /// Judgement counts by lazer's JSON names (<c>Perfect</c> is <c>perfect</c>, <c>LargeTickHit</c> is <c>large_tick_hit</c>).
        /// </summary>
        private static Dictionary<string, int> hitResults(Dictionary<string, int> statistics) => statistics.ToDictionary(s => snakeCase(s.Key), s => s.Value);

        private static string snakeCase(string name)
        {
            var text = new StringBuilder(name.Length + 4);

            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0)
                    text.Append('_');

                text.Append(char.ToLowerInvariant(name[i]));
            }

            return text.ToString();
        }

        private static Dictionary<string, object?> covers(int setId)
        {
            string root = string.Create(CultureInfo.InvariantCulture, $"https://assets.ppy.sh/beatmaps/{setId}/covers");

            return new Dictionary<string, object?>
            {
                ["cover"] = $"{root}/cover.jpg",
                ["cover@2x"] = $"{root}/cover@2x.jpg",
                ["card"] = $"{root}/card.jpg",
                ["card@2x"] = $"{root}/card@2x.jpg",
                ["list"] = $"{root}/list.jpg",
                ["list@2x"] = $"{root}/list@2x.jpg",
                ["slimcover"] = $"{root}/slimcover.jpg",
                ["slimcover@2x"] = $"{root}/slimcover@2x.jpg",
            };
        }
    }
}
