using System;
using System.Collections.Generic;
using System.Linq;

namespace OrthoServer.Scoring
{
    /// <summary>
    /// A player's total pp and accuracy from their plays, as osu!'s score processor computes them
    /// (<c>ppy/osu-queue-score-statistics</c>: <c>MarkNonPreservedScoresCommand.CheckIsUserHigh</c>,
    /// <c>UserTotalPerformanceProcessor</c> and <c>UserTotalPerformanceAggregateHelper</c>).
    /// </summary>
    public static class ProfileCalculator
    {
        /// <summary>
        /// osu! sums at most this many of a user's scores.
        /// </summary>
        public const int SCORE_LIMIT = 1000;

        public static Profile Calculate(IEnumerable<PlayRecord> plays)
        {
            var counted = CountedScores(plays);

            // Group by beatmap, take the best pp of each, highest first.
            var best = counted.GroupBy(p => p.BeatmapKey)
                              .Select(g => g.OrderByDescending(p => p.Pp).First())
                              .OrderByDescending(p => p.Pp)
                              .ToArray();

            double factor = 1;
            double totalPp = 0;
            double totalAccuracy = 0;
            var top = new List<WeightedPlay>(best.Length);

            foreach (var play in best)
            {
                top.Add(new WeightedPlay(play, factor));
                totalPp += play.Pp!.Value * factor;
                totalAccuracy += play.Accuracy * factor;
                factor *= 0.95;
            }

            // osu!'s bonus for the number of scores (kept for compatibility with osu!stable's 0.25 diminishing by 0.9994).
            double bonusPp = (417.0 - 1.0 / 3.0) * (1.0 - Math.Pow(0.995, counted.Count));
            totalPp += bonusPp;

            if (best.Length > 0)
                totalAccuracy *= 100.0 / (20 * (1 - Math.Pow(0.95, best.Length)));

            return new Profile(totalPp, bonusPp, Math.Clamp(totalAccuracy, 0, 100), counted.Count, top);
        }

        /// <summary>
        /// The scores osu! counts: pp-awarding ones that it keeps ("preserves"), the best <see cref="SCORE_LIMIT"/> by pp.
        /// For each beatmap and set of mods osu! keeps a user's highest-pp score and highest-score score (one score when they
        /// are the same) and deletes the rest, so playing a map again does not add to the score count.
        /// </summary>
        public static List<PlayRecord> CountedScores(IEnumerable<PlayRecord> plays)
        {
            var awarding = plays.Where(p => p.AwardsPp && p.Pp != null).ToList();
            var preserved = new HashSet<long>();

            foreach (var group in awarding.GroupBy(p => (p.BeatmapKey, modKey(p))))
            {
                // Ties keep the earliest, as the lowest score id wins a MaxBy over scores in id order.
                var inOrder = group.OrderBy(p => p.Id).ToArray();

                preserved.Add(inOrder.MaxBy(p => p.Pp)!.Id);
                preserved.Add(inOrder.MaxBy(p => p.TotalScore)!.Id);
            }

            return awarding.Where(p => preserved.Contains(p.Id))
                           .OrderByDescending(p => p.Pp)
                           .ThenBy(p => p.Id)
                           .Take(SCORE_LIMIT)
                           .ToList();
        }

        /// <summary>
        /// osu! compares mods by their acronyms only (settings are ignored).
        /// </summary>
        private static string modKey(PlayRecord play) => string.Join(",", play.Mods.Select(m => m.Acronym).OrderBy(a => a, StringComparer.Ordinal));
    }

    public record Profile(double TotalPp, double BonusPp, double Accuracy, int CountedScores, IReadOnlyList<WeightedPlay> Top);

    public record WeightedPlay(PlayRecord Play, double Weight)
    {
        public double WeightedPp => Play.Pp!.Value * Weight;
    }
}
