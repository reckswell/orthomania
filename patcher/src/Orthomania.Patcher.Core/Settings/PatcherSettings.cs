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
        /// orthomania's game data (database, settings, imported files, replays). Null: <see cref="DefaultDataFolder"/>.
        /// </summary>
        public string? DataFolder { get; set; }

        /// <summary>
        /// osu!(stable)'s install, where screenshots are moved after each run. Null: <c>%LOCALAPPDATA%\osu!</c>.
        /// </summary>
        public string? StableFolder { get; set; }

        /// <summary>
        /// Where orthomania's releases are published (a GitHub repository, <c>owner/name</c>).
        /// </summary>
        public string ReleaseRepository { get; set; } = DEFAULT_RELEASE_REPOSITORY;

        /// <summary>
        /// Whether to check for and install lazer updates on each launch.
        /// </summary>
        public bool UpdateLazer { get; set; } = true;

        /// <summary>
        /// How many earlier version folders and builds to keep beside the current one, to fall back on.
        /// </summary>
        public int KeepPrevious { get; set; } = 1;

        /// <summary>
        /// Development only: start the score server with the game. Null: never.
        /// </summary>
        public DevServerSettings? DevServer { get; set; }

        public const string DEFAULT_RELEASE_REPOSITORY = "Reckswell/orthomania";

        public string ResolveDataFolder() => PathUtils.Normalise(DataFolder ?? DefaultDataFolder);

        public string ResolveStableFolder()
            => PathUtils.Normalise(StableFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!"));

        public string? ResolveLazerFolder() => LazerFolder == null ? null : PathUtils.Normalise(LazerFolder);

        /// <summary>
        /// The data folder the old launcher (<c>osu.exe</c>, next to <c>ortho-data</c> in <c>Documents\Projects\osu!</c>) used, when it
        /// is there, so nothing moves; otherwise <c>%APPDATA%\orthomania\data</c>.
        /// </summary>
        public static string DefaultDataFolder
        {
            get
            {
                string legacy = LegacyDataFolder;

                if (Directory.Exists(legacy))
                    return legacy;

                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "orthomania", "data");
            }
        }

        public static string LegacyDataFolder
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Projects", "osu!", "ortho-data");
    }
}
