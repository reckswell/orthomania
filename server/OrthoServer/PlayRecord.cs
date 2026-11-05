using System;
using System.Collections.Generic;
using osu.Game.Online.API;

namespace OrthoServer
{
    /// <summary>
    /// A stored play.
    /// </summary>
    public class PlayRecord
    {
        public long Id { get; set; }

        public string? ClientScoreId { get; set; }

        /// <summary>
        /// The account the play belongs to: the one signed in to the game when it was sent, else the server's first account
        /// (<see cref="Accounts.AccountStore.Owner"/>). Null for plays sent before any account existed; the first account takes those.
        /// </summary>
        public long? UserId { get; set; }

        public DateTimeOffset PlayedAt { get; set; }

        public DateTimeOffset ReceivedAt { get; set; }

        public bool Passed { get; set; }

        public string? Rank { get; set; }

        public string BeatmapMd5 { get; set; } = string.Empty;

        public int BeatmapId { get; set; }

        public int BeatmapSetId { get; set; }

        public string Status { get; set; } = string.Empty;

        public string Artist { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public string Creator { get; set; } = string.Empty;

        public int Keys { get; set; }

        public List<APIMod> Mods { get; set; } = new List<APIMod>();

        public Dictionary<string, int> Statistics { get; set; } = new Dictionary<string, int>();

        public Dictionary<string, int> MaximumStatistics { get; set; } = new Dictionary<string, int>();

        public int MaxCombo { get; set; }

        public double Accuracy { get; set; }

        public long TotalScore { get; set; }

        public double? ClientPp { get; set; }

        public double? ClientStars { get; set; }

        public int? ClientDifficultyVersion { get; set; }

        public Dictionary<string, string?> Settings { get; set; } = new Dictionary<string, string?>();

        public string? ReplayFile { get; set; }

        /// <summary>
        /// The play's pp by the server's calculators, whether or not it counts towards the profile.
        /// </summary>
        public double? Pp { get; set; }

        public double? Stars { get; set; }

        /// <summary>
        /// Whether osu! would award this play pp (passed, a ranked or approved map, only pp-awarding mods).
        /// </summary>
        public bool AwardsPp { get; set; }

        /// <summary>
        /// Why <see cref="AwardsPp"/> is false.
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// The calculator version <see cref="Pp"/> and <see cref="Stars"/> come from.
        /// </summary>
        public string CalculatorVersion { get; set; } = string.Empty;

        /// <summary>
        /// The key osu! groups a user's plays by: the beatmap's online id, or its checksum when it has none.
        /// </summary>
        public string BeatmapKey => BeatmapId > 0 ? BeatmapId.ToString(System.Globalization.CultureInfo.InvariantCulture) : BeatmapMd5;
    }
}
