using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Installs
{
    /// <summary>
    /// Finds osu! installs on this computer.
    /// </summary>
    public interface IInstallFinder
    {
        IReadOnlyList<OsuInstall> Find(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Where to look: places osu! says it is (<see cref="Known"/>: folders, executables or command lines), and folders to search
    /// a few levels down (<see cref="Searched"/>).
    /// </summary>
    public interface IInstallPlaces
    {
        IEnumerable<string> Known();

        IEnumerable<SearchRoot> Searched();
    }

    public sealed record SearchRoot(string Folder, int Depth);

    /// <summary>
    /// Looks in the places osu! says it is first, then searches drives and the person's own folders a few levels down. The search
    /// only lists folders and checks for <c>osu!.exe</c> in each, never opens a file in them, and stops after
    /// <see cref="MaxFolders"/> folders or <see cref="TimeLimit"/>, whichever comes first.
    /// </summary>
    public sealed class InstallFinder : IInstallFinder
    {
        public int MaxFolders { get; init; } = 60_000;

        public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(10);

        private readonly IInstallPlaces places;
        private readonly Action<string> log;

        /// <summary>
        /// Never inside these: they hold Windows, other programs' caches and packages, or are too big to be worth it.
        /// </summary>
        private static readonly HashSet<string> skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "$Recycle.Bin", "$WinREAgent", "System Volume Information", "Recovery", "PerfLogs", "Config.Msi", "MSOCache",
            "ProgramData", "AppData", "node_modules", ".git", ".nuget", ".cache", "obj", "WindowsApps", "WinSxS",
        };

        /// <summary>
        /// The system's own folders, at the top of a Unix file system.
        /// </summary>
        private static readonly HashSet<string> skippedAtUnixRoot = new HashSet<string>
        {
            "proc", "sys", "dev", "usr", "lib", "lib32", "lib64", "libx32", "bin", "sbin", "snap", "var", "etc", "boot", "run", "tmp", "srv", "opt",
        };

        public InstallFinder(IInstallPlaces places, Action<string> log)
        {
            this.places = places;
            this.log = log;
        }

        public IReadOnlyList<OsuInstall> Find(CancellationToken cancellationToken)
        {
            var found = new Dictionary<string, OsuInstall>(StringComparer.FromComparison(PathUtils.Comparison));

            void add(OsuInstall? install)
            {
                if (install != null)
                    found.TryAdd(install.Folder, install);
            }

            foreach (string place in places.Known())
                add(OsuInstall.Identify(CommandLinePath(place)));

            var clock = Stopwatch.StartNew();
            int visited = 0;
            bool stopped = false;

            foreach (var root in places.Searched())
            {
                var queue = new Queue<(string Folder, int Depth)>();
                queue.Enqueue((root.Folder, 0));

                while (queue.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (visited >= MaxFolders || clock.Elapsed > TimeLimit)
                    {
                        stopped = true;
                        break;
                    }

                    var (folder, depth) = queue.Dequeue();
                    visited++;

                    if (File.Exists(Path.Combine(folder, LazerInstall.GAME_EXECUTABLE)) || File.Exists(Path.Combine(folder, "current", LazerInstall.GAME_EXECUTABLE)))
                        add(OsuInstall.Identify(folder));

                    if (depth >= root.Depth)
                        continue;

                    foreach (string child in subfolders(folder))
                        queue.Enqueue((child, depth + 1));
                }
            }

            log($"searched {visited} folders in {clock.Elapsed.TotalSeconds:0.0} s{(stopped ? " (stopped at the limit)" : "")}: "
                + (found.Count == 0 ? "no osu! found" : string.Join("; ", found.Values)));

            return found.Values.OrderBy(i => i.Kind).ThenByDescending(i => lazerVersion(i)).ThenBy(i => i.Folder, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> subfolders(string folder)
        {
            try
            {
                return Directory.EnumerateDirectories(folder, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                    RecurseSubdirectories = false,
                }).Where(f => !skipped.Contains(Path.GetFileName(f)) && !(folder == "/" && skippedAtUnixRoot.Contains(Path.GetFileName(f)))).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return Array.Empty<string>();
            }
        }

        private static LazerVersion? lazerVersion(OsuInstall install)
            => install.Kind == OsuKind.Lazer && LazerVersion.TryParse(install.Release, out var version) ? version : null;

        /// <summary>
        /// The path in a registry value: a folder, an executable, or a command line such as <c>"C:\osu!\osu!.exe" "%1"</c>.
        /// </summary>
        public static string? CommandLinePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            if (value.StartsWith('"'))
            {
                int end = value.IndexOf('"', 1);
                return end > 1 ? value.Substring(1, end - 1) : null;
            }

            int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? value.Substring(0, exe + 4) : value.Split(',')[0];
        }
    }
}
