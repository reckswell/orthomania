using System;
using System.Diagnostics;
using System.IO;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Installs
{
    public enum OsuKind
    {
        /// <summary>
        /// osu!(lazer) as its installer lays it out (<see cref="LazerInstall"/>), which orthomania runs on.
        /// </summary>
        Lazer,

        /// <summary>
        /// osu!(stable), also called classic.
        /// </summary>
        Stable,
    }

    /// <summary>
    /// An osu! install found on this computer: for lazer, the folder its installer made (with <c>current\</c> and
    /// <c>Update.exe</c>); for stable, the folder with its <c>osu!.exe</c>.
    /// </summary>
    public sealed record OsuInstall(OsuKind Kind, string Folder, string? Release)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public string Name => Kind == OsuKind.Lazer ? "osu!(lazer)" : "osu!(stable)";

        public override string ToString() => Release != null ? $"{Name} {Release} at {Folder}" : $"{Name} at {Folder}";

        /// <summary>
        /// What is at <paramref name="path"/>: a lazer install (its folder, its <c>current</c> folder, or a file in either, such as
        /// <c>osu!.exe</c>), a stable install (its folder or a file in it), or neither. Only file names and versions are read.
        /// </summary>
        public static OsuInstall? Identify(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string folder;

            try
            {
                folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));

                if (File.Exists(folder))
                    folder = Path.GetDirectoryName(folder)!;

                if (!Directory.Exists(folder))
                    return null;
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return null;
            }

            folder = PathUtils.Normalise(folder);

            // Lazer: the installer's folder, or the "current" folder in it.
            foreach (string? root in new[] { folder, Path.GetDirectoryName(folder) })
            {
                if (root == null)
                    continue;

                var lazer = LazerInstall.Find(root);

                if (lazer != null)
                    return new OsuInstall(OsuKind.Lazer, PathUtils.Normalise(lazer.Root), readLazerRelease(lazer));
            }

            if (IsStable(folder))
                return new OsuInstall(OsuKind.Stable, folder, readFileVersion(Path.Combine(folder, LazerInstall.GAME_EXECUTABLE)));

            return null;
        }

        /// <summary>
        /// Stable's folder: its <c>osu!.exe</c> without lazer's <c>osu.Game.dll</c> beside it, and its songs, database or settings.
        /// </summary>
        public static bool IsStable(string folder)
            => File.Exists(Path.Combine(folder, LazerInstall.GAME_EXECUTABLE))
               && !File.Exists(Path.Combine(folder, LazerInstall.GAME_ASSEMBLY))
               && (Directory.Exists(Path.Combine(folder, "Songs")) || File.Exists(Path.Combine(folder, "osu!.db")) || File.Exists(Path.Combine(folder, "osu!.cfg")));

        private static string? readLazerRelease(LazerInstall lazer)
        {
            try
            {
                return LazerInstall.ReadReleaseFrom(lazer.Current)?.VersionText;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException or BadImageFormatException)
            {
                return null;
            }
        }

        private static string? readFileVersion(string executable)
        {
            try
            {
                string? version = FileVersionInfo.GetVersionInfo(executable).FileVersion;
                return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
