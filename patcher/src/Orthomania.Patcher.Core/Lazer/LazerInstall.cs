using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Orthomania.Patcher.Lazer
{
    /// <summary>
    /// An official osu!(lazer) install, as its installer (Velopack) lays it out:
    /// <code>
    /// %LOCALAPPDATA%\osulazer\Update.exe          the updater
    /// %LOCALAPPDATA%\osulazer\current\osu!.exe    the game, with sq.version naming its release
    /// %LOCALAPPDATA%\osulazer\packages\           downloaded updates
    /// </code>
    /// orthomania only ever reads this folder. The one thing that changes it is lazer's own updater (<see cref="ILazerUpdater"/>).
    /// </summary>
    public sealed class LazerInstall
    {
        public const string GAME_EXECUTABLE = "osu!.exe";

        public const string GAME_ASSEMBLY = "osu.Game.dll";

        public const string VERSION_MANIFEST = "sq.version";

        /// <summary>
        /// The process name of both osu!(lazer) and osu!(stable): tell them apart by path.
        /// </summary>
        public const string PROCESS_NAME = "osu!";

        public string Root { get; }

        public string Current => Path.Combine(Root, "current");

        public string GameExecutable => Path.Combine(Current, GAME_EXECUTABLE);

        public string UpdateExe => Path.Combine(Root, "Update.exe");

        public string PackagesDir => Path.Combine(Root, "packages");

        /// <summary>
        /// Velopack's record of the installed release (a nuspec).
        /// </summary>
        public string VersionManifest => Path.Combine(Current, VERSION_MANIFEST);

        /// <summary>
        /// Lazer's resources (fonts, textures, shaders), which the patcher's window borrows its font from.
        /// </summary>
        public string ResourcesAssembly => Path.Combine(Current, "osu.Game.Resources.dll");

        public LazerInstall(string root)
        {
            Root = Path.GetFullPath(root);
        }

        /// <summary>
        /// Where lazer's installer puts it for the current user.
        /// </summary>
        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osulazer");

        /// <summary>
        /// The install at <paramref name="root"/> (or the default place), or null when no game is there.
        /// </summary>
        public static LazerInstall? Find(string? root = null)
        {
            var install = new LazerInstall(root ?? DefaultRoot);
            return File.Exists(install.GameExecutable) ? install : null;
        }

        /// <summary>
        /// The installed release: from <c>sq.version</c>, else from <c>osu.Game.dll</c>'s assembly version.
        /// </summary>
        public InstalledRelease ReadRelease()
            => ReadReleaseFrom(Current) ?? throw new InvalidDataException($"Cannot tell which osu! release is installed in {Root}.");

        /// <summary>
        /// The release of the lazer files in <paramref name="folder"/> (an install's <c>current</c> folder, or a version folder,
        /// which links the same files), or null when it cannot be told.
        /// </summary>
        public static InstalledRelease? ReadReleaseFrom(string folder)
        {
            string versionManifest = Path.Combine(folder, VERSION_MANIFEST);

            if (File.Exists(versionManifest))
            {
                var metadata = XDocument.Load(versionManifest).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");

                string? read(string name) => metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

                string? versionText = read("version");

                if (LazerVersion.TryParse(versionText, out var version))
                    return new InstalledRelease(version, read("id") ?? "osulazer", versionText!, read("channel"));
            }

            var assemblyVersion = ReadGameAssemblyVersion(folder);

            if (assemblyVersion != null)
                return new InstalledRelease(assemblyVersion, "osulazer", assemblyVersion.Number, null);

            return null;
        }

        /// <summary>
        /// The release <c>osu.Game.dll</c> in <paramref name="folder"/> was built as (its assembly version), or null.
        /// </summary>
        public static LazerVersion? ReadGameAssemblyVersion(string folder)
        {
            string gameAssembly = Path.Combine(folder, GAME_ASSEMBLY);

            if (!File.Exists(gameAssembly))
                return null;

            try
            {
                var assemblyVersion = AssemblyName.GetAssemblyName(gameAssembly).Version;

                // A development build is 0.0.0.0, which says nothing.
                if (assemblyVersion != null && assemblyVersion.Major > 0 && LazerVersion.TryParse(assemblyVersion.ToString(), out var version))
                    return version;
            }
            catch (Exception e) when (e is BadImageFormatException or IOException)
            {
            }

            return null;
        }

        /// <summary>
        /// The installed release as text, or "unknown" when it cannot be read.
        /// </summary>
        public string ReadReleaseSafely()
        {
            try
            {
                return ReadReleaseFrom(Current)?.Version.Number ?? "(unknown release)";
            }
            catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                return "(unknown release)";
            }
        }

        /// <summary>
        /// Whether <paramref name="path"/> is this install's game or anything else inside its folder.
        /// </summary>
        public bool Contains(string path) => PathUtils.IsInside(path, Root);
    }

    /// <summary>
    /// What Velopack recorded about an installed release.
    /// </summary>
    /// <param name="Version">The release.</param>
    /// <param name="AppId">Velopack's id for the app (<c>osulazer</c>).</param>
    /// <param name="VersionText">The version exactly as Velopack wrote it, which its updater compares against.</param>
    /// <param name="Channel">Velopack's channel (<c>win</c>), or null when unrecorded.</param>
    public sealed record InstalledRelease(LazerVersion Version, string AppId, string VersionText, string? Channel);
}
