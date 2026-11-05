using Newtonsoft.Json;

// Shared by the game and the server, so one namespace for both projects.
// ReSharper disable once CheckNamespace
namespace ManiaOrtho.ScoreServerContract
{
    /// <summary>
    /// The server's answer to an upload. Field names are pinned by <c>contract/play-result.example.json</c>.
    /// </summary>
    public class PlayResult
    {
        [JsonProperty("play_id")]
        public long PlayId { get; set; }

        /// <summary>
        /// The play had been received before (a retry); nothing was added.
        /// </summary>
        [JsonProperty("already_stored")]
        public bool AlreadyStored { get; set; }

        [JsonProperty("pp")]
        public double? Pp { get; set; }

        [JsonProperty("stars")]
        public double? Stars { get; set; }

        [JsonProperty("awards_pp")]
        public bool AwardsPp { get; set; }

        [JsonProperty("reason")]
        public string? Reason { get; set; }

        /// <summary>
        /// Where the play stands among the profile's best plays (1 is the best), if it counts.
        /// </summary>
        [JsonProperty("top_position")]
        public int? TopPosition { get; set; }

        [JsonProperty("weight")]
        public double? Weight { get; set; }

        [JsonProperty("total_pp")]
        public double TotalPp { get; set; }

        [JsonProperty("total_pp_before")]
        public double TotalPpBefore { get; set; }

        [JsonProperty("accuracy")]
        public double Accuracy { get; set; }

        [JsonProperty("calculator_version")]
        public string CalculatorVersion { get; set; } = string.Empty;

        /// <summary>
        /// The calculator version osu! runs live, when the server has checked and it differs from <see cref="CalculatorVersion"/>.
        /// </summary>
        [JsonProperty("live_calculator_version")]
        public string? LiveCalculatorVersion { get; set; }

        [JsonProperty("client_pp")]
        public double? ClientPp { get; set; }
    }
}
