using System;
using System.Diagnostics;
using System.IO;
using Orthomania.Patcher.Folders;

namespace Orthomania.Patcher.Launch
{
    /// <summary>
    /// Starts lazer from a version folder, which is what hooks orthomania in: lazer loads the ruleset DLL beside it, and the DLL
    /// puts its hooks in as it loads.
    /// </summary>
    public static class GameLauncher
    {
        /// <summary>
        /// Lazer reads this (<c>OsuGameDesktop.IsPackageManaged</c>): it then leaves updates to whoever started it, so the copy the
        /// patcher starts never runs lazer's installer hooks or updates itself from the version folder. The patcher updates lazer
        /// before starting it instead.
        /// </summary>
        public const string UPDATE_PROVIDER = "orthomania (updated by orthomania.exe)";

        public static ProcessStartInfo CreateStartInfo(VersionFolder folder)
        {
            var info = new ProcessStartInfo(folder.GameExecutable)
            {
                WorkingDirectory = folder.Directory,
                UseShellExecute = false,
            };

            info.Environment["OSU_EXTERNAL_UPDATE_PROVIDER"] = UPDATE_PROVIDER;

            // Crashes of a game with orthomania in it are not osu!'s to look at.
            info.Environment["OSU_DISABLE_ERROR_REPORTING"] = "1";

            return info;
        }

        public static Process Start(VersionFolder folder)
        {
            if (!File.Exists(folder.GameExecutable))
                throw new FileNotFoundException($"No osu! in {folder.Directory}. Repair it from Options.", folder.GameExecutable);

            return Process.Start(CreateStartInfo(folder)) ?? throw new InvalidOperationException($"Could not start {folder.GameExecutable}.");
        }
    }
}
