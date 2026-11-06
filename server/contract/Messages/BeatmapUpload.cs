using Newtonsoft.Json;

// Shared by the game and the server, so one namespace for both projects.
// ReSharper disable once CheckNamespace
namespace ManiaOrtho.ScoreServerContract
{
    public class BeatmapUpload
    {
        /// <summary>
        /// MD5 of the <c>.osu</c> file, lower-case hex. The uploaded file must match it.
        /// </summary>
        [JsonProperty("md5")]
        public string Md5 { get; set; } = string.Empty;

        [JsonProperty("online_id")]
        public int OnlineId { get; set; }

        [JsonProperty("set_online_id")]
        public int SetOnlineId { get; set; }

        /// <summary>
        /// The beatmap's status on osu! as the game knows it (<see cref="osu.Game.Beatmaps.BeatmapOnlineStatus"/> name).
        /// Only <c>Ranked</c> and <c>Approved</c> maps award pp, as on osu!.
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("artist")]
        public string Artist { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("creator")]
        public string Creator { get; set; } = string.Empty;

        [JsonProperty("keys")]
        public int Keys { get; set; }
    }
}
