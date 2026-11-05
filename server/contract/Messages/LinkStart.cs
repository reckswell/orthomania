using Newtonsoft.Json;

// Shared by the game and the server, so one namespace for both projects.
// ReSharper disable once CheckNamespace
namespace ManiaOrtho.ScoreServerContract
{
    /// <summary>
    /// The server's answer to <c>POST /api/link</c>, the start of linking the game to an account on the server by signing in
    /// with osu!. The game opens <see cref="Url"/> in the browser and asks <c>POST /api/link/poll</c> (form field
    /// <c>device_code</c>) every <see cref="Interval"/> seconds until it answers something other than
    /// <see cref="LinkStatus.PENDING"/>. Field names are pinned by <c>contract/link-start.example.json</c>.
    /// </summary>
    public class LinkStart
    {
        /// <summary>
        /// Known only to the game, which alone can collect the sign-in with it. Never shown.
        /// </summary>
        [JsonProperty("device_code")]
        public string DeviceCode { get; set; } = string.Empty;

        /// <summary>
        /// Shown by the game and by the server's page in the browser, so the player can see the two belong together.
        /// </summary>
        [JsonProperty("user_code")]
        public string UserCode { get; set; } = string.Empty;

        /// <summary>
        /// The server's page that sends the browser on to osu!'s sign-in.
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Seconds until the codes stop working.
        /// </summary>
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Seconds to wait between polls.
        /// </summary>
        [JsonProperty("interval")]
        public int Interval { get; set; }
    }
}
