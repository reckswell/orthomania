using Newtonsoft.Json;

// Shared by the game and the server, so one namespace for both projects.
// ReSharper disable once CheckNamespace
namespace ManiaOrtho.ScoreServerContract
{
    /// <summary>
    /// The server's answer to <c>POST /api/link/poll</c>. Field names are pinned by <c>contract/link-status.example.json</c>.
    /// </summary>
    public class LinkStatus
    {
        /// <summary>
        /// The player has not finished signing in yet; ask again after the interval.
        /// </summary>
        public const string PENDING = "pending";

        /// <summary>
        /// Signed in: <see cref="Token"/> is the game's sign-in on the server. It is given once.
        /// </summary>
        public const string LINKED = "linked";

        /// <summary>
        /// The player did not allow it on osu!.
        /// </summary>
        public const string DENIED = "denied";

        /// <summary>
        /// The codes expired, were already used, or the server restarted meanwhile; start again.
        /// </summary>
        public const string EXPIRED = "expired";

        [JsonProperty("status")]
        public string Status { get; set; } = PENDING;

        /// <summary>
        /// What the game sends as <c>Authorization: Bearer</c> with each play, until it unlinks (<c>DELETE /api/link</c>). It
        /// is a token of this server only; nothing of osu!'s.
        /// </summary>
        [JsonProperty("token")]
        public string? Token { get; set; }

        /// <summary>
        /// The account's name on the server: the osu! username.
        /// </summary>
        [JsonProperty("username")]
        public string? Username { get; set; }

        [JsonProperty("osu_user_id")]
        public long? OsuUserId { get; set; }
    }
}
