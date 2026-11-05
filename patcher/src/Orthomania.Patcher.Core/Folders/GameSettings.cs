using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orthomania.Patcher.Folders
{
    /// <summary>
    /// <c>orthomania-game.json</c>, beside the ruleset DLL in each version folder: everything the game side of orthomania is told.
    /// <para>
    /// A build (manifest format 2 and later) keeps everything of its own in <see cref="DataFolder"/>, never in osu!'s data folder, and
    /// keeps osu! signed out while it is loaded unless <see cref="OsuSignIn"/> is on: it neither uses the sign-in osu! saved nor
    /// changes it, so plain osu! stays signed in as it was.
    /// </para>
    /// </summary>
    public sealed class GameSettings
    {
        public const string FILE_NAME = "orthomania-game.json";

        public const int CURRENT_FORMAT = 1;

        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        public int Format { get; set; } = CURRENT_FORMAT;

        /// <summary>
        /// orthomania's own data folder.
        /// </summary>
        public string DataFolder { get; set; } = string.Empty;

        /// <summary>
        /// Whether the game may use the osu! sign-in osu! saved. Off unless the person turns it on.
        /// </summary>
        public bool OsuSignIn { get; set; }

        public GameSettings()
        {
        }

        public GameSettings(string dataFolder, bool osuSignIn)
        {
            DataFolder = PathUtils.Normalise(dataFolder);
            OsuSignIn = osuSignIn;
        }

        [JsonIgnore]
        public string Json => JsonSerializer.Serialize(this, json_options) + "\r\n";

        public static GameSettings? Parse(string text) => JsonSerializer.Deserialize<GameSettings>(text, json_options);
    }
}
