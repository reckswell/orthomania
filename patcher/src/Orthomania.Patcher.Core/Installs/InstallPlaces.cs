using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Installs
{
    /// <summary>
    /// Where osu! is on this computer, by what osu! itself leaves behind: lazer's installer and its links (<c>osu://</c>, <c>.osz</c>),
    /// stable's, a running <c>osu!.exe</c>, and the usual folders. Then the fixed drives and the person's own folders, searched.
    /// Only ever read.
    /// </summary>
    public sealed class InstallPlaces : IInstallPlaces
    {
        public IEnumerable<string> Known()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var places = new List<string>
            {
                LazerInstall.DefaultRoot,
                Path.Combine(local, "osu!"),
                Path.Combine(home, ".osu"),
            };

            if (OperatingSystem.IsWindows())
                places.AddRange(registry());

            places.AddRange(running());

            foreach (var drive in fixedDrives())
            {
                foreach (string folder in new[] { "osu!", "osu", "osulazer", Path.Combine("Games", "osu!"), Path.Combine("Program Files", "osu!"), Path.Combine("Program Files (x86)", "osu!") })
                    places.Add(Path.Combine(drive, folder));
            }

            return places;
        }

        public IEnumerable<SearchRoot> Searched()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // The person's own folders first, deeper: osu! is often in Documents, Desktop, Downloads or a Games folder.
            foreach (string folder in new[] { "Desktop", "Documents", "Downloads", "Games", "OneDrive" })
            {
                string path = Path.Combine(home, folder);
                if (Directory.Exists(path))
                    yield return new SearchRoot(path, 3);
            }

            foreach (string drive in fixedDrives())
                yield return new SearchRoot(drive, 3);
        }

        private static IEnumerable<string> fixedDrives()
        {
            DriveInfo[] drives;

            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var drive in drives)
            {
                bool usable;

                try
                {
                    usable = drive.DriveType == DriveType.Fixed && drive.IsReady;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    usable = false;
                }

                if (usable && (OperatingSystem.IsWindows() || drive.RootDirectory.FullName == "/"))
                    yield return drive.RootDirectory.FullName;
            }
        }

        /// <summary>
        /// The <c>osu!.exe</c> of osu! running now, lazer or stable.
        /// </summary>
        private static IEnumerable<string> running()
        {
            var paths = new List<string>();

            foreach (var process in Process.GetProcessesByName(LazerInstall.PROCESS_NAME))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is string path)
                            paths.Add(path);
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                    }
                }
            }

            return paths;
        }

        [SupportedOSPlatform("windows")]
        private static IEnumerable<string> registry()
        {
            var values = new List<string?>();

            void read(RegistryKey hive, string key, string? name)
            {
                try
                {
                    using var opened = hive.OpenSubKey(key);
                    values.Add(opened?.GetValue(name ?? string.Empty) as string);
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                }
            }

            const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

            // Lazer's installer, and the links lazer makes for itself.
            read(Registry.CurrentUser, uninstall + "osulazer", "InstallLocation");
            read(Registry.ClassesRoot, @"osu\Shell\Open\Command", null);
            read(Registry.ClassesRoot, @"osump\Shell\Open\Command", null);

            // Stable's.
            read(Registry.ClassesRoot, @"osustable.File.osz\Shell\Open\Command", null);
            read(Registry.ClassesRoot, @"osu!\Shell\Open\Command", null);
            read(Registry.ClassesRoot, @"osu!\DefaultIcon", null);
            read(Registry.CurrentUser, uninstall + "osu!", "InstallLocation");
            read(Registry.CurrentUser, uninstall + "osu!", "DisplayIcon");
            read(Registry.LocalMachine, uninstall + "osu!", "InstallLocation");
            read(Registry.LocalMachine, uninstall + "osu!", "DisplayIcon");

            return values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!);
        }
    }
}
