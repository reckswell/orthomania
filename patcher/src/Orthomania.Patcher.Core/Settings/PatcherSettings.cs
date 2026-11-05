using System;
using System.IO;

namespace Orthomania.Patcher.Settings
{
    /// <summary>
    /// What the person can choose (<c>settings.json</c>). Every value has a default, so the file only holds what was changed.
    /// </summary>
    public sealed class PatcherSettings
    {
        /// <summary>
        /// Where official lazer is installed. Null: its installer's place (<c>%LOCALAPPDATA%\osulazer</c>).
        /// </summary>
        public string? LazerFolder { get; set; }

        /// <summary>
        /// orthomania's own data (its settings, play log, replays, score server sign-in); never osu!'s data, which osu! keeps in its own
        /// folder for plain lazer and orthomania alike. Null: <see cref="DefaultDataFolder"/>.
        /// </summary>
        public string? DataFolder { get; set; }

        /// <summary>
        /// Whether the game started by orthomania may use the osu! sign-in osu! saved. Off: osu! stays signed out while orthomania is
        /// in it, and the saved sign-in is left as it is for plain osu!.
        /// </summary>
        public bool OsuSignIn { get; set; }

        /// <summary>
        /// Where orthomania's releases are published (a GitHub repository, <c>owner/name</c>).
        /// </summary>
        public string ReleaseRepository { get; set; } = DEFAULT_RELEASE_REPOSITORY;

        /// <summary>
        /// Whether to check for and install lazer updates on each launch.
        /// </summary>
        public bool UpdateLazer { get; set; } = true;

        /// <summary>
        /// Development only: start the score server with the game. Null: never.
        /// </summary>
        public DevServerSettings? DevServer { get; set; }

        public const string DEFAULT_RELEASE_REPOSITORY = "Reckswell/orthomania";

        public string ResolveDataFolder() => PathUtils.Normalise(DataFolder ?? DefaultDataFolder);

        public string? ResolveLazerFolder() => LazerFolder == null ? null : PathUtils.Normalise(LazerFolder);

        /// <summary>
        /// <see cref="DevelopmentDataFolder"/> when it is there, so development keeps everything in one place; otherwise
        /// <c>%APPDATA%\orthomania\data</c>.
        /// </summary>
        public static string DefaultDataFolder
        {
            get
            {
                string development = DevelopmentDataFolder;

                if (Directory.Exists(development))
                    return development;

                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "orthomania", "data");
            }
        }

        /// <summary>
        /// <c>Documents\Projects\osu!\ortho-data</c>, beside the clones orthomania is developed in.
        /// </summary>
        public static string DevelopmentDataFolder
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Projects", "osu!", "ortho-data");
    }
}
