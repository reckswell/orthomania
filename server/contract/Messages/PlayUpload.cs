using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Online.API;

// Shared by the game and the server, so one namespace for both projects.
// ReSharper disable once CheckNamespace
namespace ManiaOrtho.ScoreServerContract
{
    /// <summary>
    /// The JSON part (<c>play</c>) of an upload from the game to the score server. Compiled into both the game's ruleset and
    /// the server, so neither can rename a field alone; <c>play-upload.example.json</c> documents it, and both test suites
    /// check the example still reads as this type.
    /// </summary>
    public class PlayUpload
    {
        /// <summary>
        /// The ruleset the play was made in (<c>maniaortho</c>). Plays are rated as osu!mania.
        /// </summary>
        [JsonProperty("client")]
        public string Client { get; set; } = string.Empty;

        [JsonProperty("client_version")]
        public string? ClientVersion { get; set; }

        /// <summary>
        /// The game's own id for the play. Sending the same play twice (a retry after a lost answer) stores it once.
        /// </summary>
        [JsonProperty("score_id")]
        public string? ScoreId { get; set; }

        [JsonProperty("played_at")]
        public DateTimeOffset PlayedAt { get; set; }

        [JsonProperty("passed")]
        public bool Passed { get; set; }

        [JsonProperty("rank")]
        public string? Rank { get; set; }

        [JsonProperty("beatmap")]
        public BeatmapUpload Beatmap { get; set; } = new BeatmapUpload();

        [JsonProperty("mods")]
        public List<APIMod> Mods { get; set; } = new List<APIMod>();

        /// <summary>
        /// Judgement counts by <see cref="osu.Game.Rulesets.Scoring.HitResult"/> name (<c>Perfect</c>, <c>Great</c>, ...).
        /// </summary>
        [JsonProperty("statistics")]
        public Dictionary<string, int> Statistics { get; set; } = new Dictionary<string, int>();

        [JsonProperty("maximum_statistics")]
        public Dictionary<string, int> MaximumStatistics { get; set; } = new Dictionary<string, int>();

        [JsonProperty("max_combo")]
        public int MaxCombo { get; set; }

        /// <summary>
        /// 0 to 1.
        /// </summary>
        [JsonProperty("accuracy")]
        public double Accuracy { get; set; }

        [JsonProperty("total_score")]
        public long TotalScore { get; set; }

        /// <summary>
        /// The pp the game showed, from its own copy of the calculators. Kept to show when the game and osu!'s live version differ.
        /// </summary>
        [JsonProperty("client_pp")]
        public double? ClientPp { get; set; }

        [JsonProperty("client_stars")]
        public double? ClientStars { get; set; }

        [JsonProperty("client_difficulty_version")]
        public int? ClientDifficultyVersion { get; set; }

        /// <summary>
        /// The highway settings the play was made with (preset, camera, wall, ...), as the game's play log writes them.
        /// Stored for the player's own records; they never change the rating.
        /// </summary>
        [JsonProperty("settings")]
        public Dictionary<string, string?> Settings { get; set; } = new Dictionary<string, string?>();
    }
}
