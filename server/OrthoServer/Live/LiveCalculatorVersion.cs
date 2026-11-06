using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OrthoServer.Live
{
    /// <summary>
    /// Finds the mania calculator version osu! rates scores with live: the <c>ppy.osu.Game.Rulesets.Mania</c> package that
    /// the newest release of osu!'s score processor (<c>ppy/osu-queue-score-statistics</c>) is built with.
    /// </summary>
    /// <remarks>
    /// Release tags are read from git's own ref listing (what <c>git ls-remote</c> reads), which needs no API key and has no
    /// paging. A release tag is the best public signal of what is deployed; ppy announce pp reworks on the osu! news too.
    /// </remarks>
    public static partial class LiveCalculatorVersion
    {
        public const string REPOSITORY = "https://github.com/ppy/osu-queue-score-statistics";

        public const string PROJECT_PATH = "osu.Server.Queues.ScoreStatisticsProcessor/osu.Server.Queues.ScoreStatisticsProcessor.csproj";

        public record Result(string ReleaseTag, string CalculatorVersion);

        public static async Task<Result> FetchAsync(HttpClient http, CancellationToken cancellationToken = default)
        {
            string refs = await http.GetStringAsync($"{REPOSITORY}.git/info/refs?service=git-upload-pack", cancellationToken).ConfigureAwait(false);
            string tag = LatestReleaseTag(ParseTags(refs)) ?? throw new InvalidOperationException("osu!'s score processor has no release tags.");

            string project = await http.GetStringAsync($"https://raw.githubusercontent.com/ppy/osu-queue-score-statistics/{tag}/{PROJECT_PATH}", cancellationToken)
                                       .ConfigureAwait(false);

            return new Result(tag, ParseManiaPackageVersion(project) ?? throw new InvalidOperationException($"No mania package reference in the score processor at {tag}."));
        }

        /// <summary>
        /// Tag names in a git smart-HTTP ref advertisement (peeled <c>^{}</c> entries are skipped).
        /// </summary>
        public static IEnumerable<string> ParseTags(string refAdvertisement)
            => tagPattern().Matches(refAdvertisement).Select(m => m.Groups[1].Value).Where(t => !t.EndsWith("^{}", StringComparison.Ordinal)).Distinct();

        /// <summary>
        /// The newest release among tags named like versions (<c>2026.1019.0</c>); other tags are ignored.
        /// </summary>
        public static string? LatestReleaseTag(IEnumerable<string> tags)
            => tags.Select(t => (tag: t, version: Version.TryParse(t, out var v) ? v : null))
                   .Where(t => t.version != null)
                   .MaxBy(t => t.version)
                   .tag;

        public static string? ParseManiaPackageVersion(string projectFile)
        {
            var match = maniaPackagePattern().Match(projectFile);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Whether <paramref name="live"/> is a different version from <paramref name="current"/> (newer or, after a revert, older).
        /// </summary>
        public static bool Differs(string current, string live)
            => Version.TryParse(current, out var a) && Version.TryParse(live, out var b) ? a != b : !string.Equals(current, live, StringComparison.Ordinal);

        [GeneratedRegex(@"refs/tags/([^\s\x00]+)")]
        private static partial Regex tagPattern();

        [GeneratedRegex(@"<PackageReference\s+Include=""ppy\.osu\.Game\.Rulesets\.Mania""\s+Version=""([^""]+)""")]
        private static partial Regex maniaPackagePattern();
    }
}
