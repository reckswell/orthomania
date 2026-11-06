using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Data
{
    /// <summary>
    /// orthomania's game data folder: what lazer's <c>storage.ini</c> points at when the patcher starts it. Only lazer writes in it,
    /// apart from the few things here: a note for people who open it, the database backups taken before a new lazer first opens it,
    /// and moving screenshots out to osu!(stable).
    /// </summary>
    public sealed partial class DataFolder
    {
        public const string DATABASE = "client.realm";
        public const string NOTE = "ortho-data.txt";

        public string Path { get; }

        public DataFolder(string path)
        {
            Path = PathUtils.Normalise(path);
        }

        public string Database => System.IO.Path.Combine(Path, DATABASE);

        public string Screenshots => System.IO.Path.Combine(Path, "screenshots");

        public bool HasDatabase => File.Exists(Database);

        /// <summary>
        /// Creates the folder. osu! asks what to do with an empty custom data folder on its first start, so a note is left in it.
        /// </summary>
        public void Prepare()
        {
            Directory.CreateDirectory(Path);

            string note = System.IO.Path.Combine(Path, NOTE);

            if (!File.Exists(note))
            {
                File.WriteAllText(note, "Game data of orthomania (orthomania.exe starts osu!(lazer) on it).\r\n"
                                        + "Plain osu!(lazer) keeps its own data elsewhere and never opens this folder.\r\n"
                                        + "Imported maps and skins here may be hard links to osu!(stable)'s files: deleting this folder frees no "
                                        + "space for those and leaves stable's files as they are.\r\n");
            }
        }

        /// <summary>
        /// A development build of osu! (the old launcher's) names its database after its schema, <c>client_52.realm</c>, where a
        /// release reads <c>client.realm</c>. When only the former is here, copies the newest one to <c>client.realm</c> so a release
        /// opens the same maps, scores and settings. The original stays.
        /// </summary>
        /// <returns>The file copied from, or null when nothing needed copying.</returns>
        public string? AdoptDevelopmentDatabase()
        {
            if (HasDatabase || !Directory.Exists(Path))
                return null;

            var newest = Directory.GetFiles(Path, "client_*.realm")
                                  .Select(f => (File: f, Match: development_database().Match(System.IO.Path.GetFileName(f))))
                                  .Where(m => m.Match.Success)
                                  .OrderByDescending(m => int.Parse(m.Match.Groups[1].Value, CultureInfo.InvariantCulture))
                                  .Select(m => m.File)
                                  .FirstOrDefault();

            if (newest == null)
                return null;

            string temp = Database + ".tmp";
            File.Copy(newest, temp, overwrite: true);
            File.Move(temp, Database);
            return newest;
        }

        [GeneratedRegex(@"^client_(\d+)\.realm$")]
        private static partial Regex development_database();

        /// <summary>
        /// Copies the database to <paramref name="backups"/>, named after the lazer release about to open it, before that release
        /// first does (it may upgrade the database past what earlier releases open). Keeps the newest <paramref name="keep"/> backups.
        /// </summary>
        /// <returns>The backup, or null when there is no database yet.</returns>
        public string? BackupDatabase(string backups, LazerVersion openingRelease, int keep = 5)
        {
            if (!HasDatabase)
                return null;

            Directory.CreateDirectory(backups);

            string name = $"client-before-{openingRelease.Number}-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.realm";
            string target = System.IO.Path.Combine(backups, name);
            string temp = target + ".tmp";

            File.Copy(Database, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);

            foreach (string old in Directory.GetFiles(backups, "client-before-*.realm").OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep))
                File.Delete(old);

            return target;
        }

        /// <summary>
        /// Moves screenshots into osu!(stable)'s Screenshots folder, never replacing a file there. Moved rather than linked: a
        /// junction to stable's folder would be followed by tools that delete recursively.
        /// </summary>
        /// <returns>How many were moved. Nothing moves when either folder is missing.</returns>
        public int MoveScreenshots(string stableFolder)
        {
            string to = System.IO.Path.Combine(stableFolder, "Screenshots");

            if (!Directory.Exists(Screenshots) || !Directory.Exists(to))
                return 0;

            int moved = 0;

            foreach (string file in Directory.GetFiles(Screenshots))
            {
                string target = System.IO.Path.Combine(to, System.IO.Path.GetFileName(file));

                if (File.Exists(target))
                    continue;

                try
                {
                    File.Move(file, target);
                    moved++;
                }
                catch (IOException)
                {
                    // Still being written, or held by another program: it moves next time.
                }
            }

            return moved;
        }

        /// <summary>
        /// Where plain lazer keeps its data: <c>%APPDATA%\osu</c>, and the custom folder its <c>storage.ini</c> there names, if any.
        /// orthomania's data must be neither.
        /// </summary>
        public static IReadOnlyList<string> PlainLazerDataFolders(string? appData = null)
        {
            string standard = System.IO.Path.Combine(appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osu");
            var folders = new List<string> { PathUtils.Normalise(standard) };

            string? custom = ReadStorageIni(System.IO.Path.Combine(standard, "storage.ini"));

            if (custom != null)
                folders.Add(PathUtils.Normalise(custom));

            return folders;
        }

        /// <summary>
        /// The <c>FullPath</c> a <c>storage.ini</c> names, or null.
        /// </summary>
        public static string? ReadStorageIni(string path)
        {
            if (!File.Exists(path))
                return null;

            foreach (string line in File.ReadLines(path))
            {
                int equals = line.IndexOf('=');

                if (equals < 0 || !line[..equals].Trim().Equals("FullPath", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line[(equals + 1)..].Trim();
                return value.Length > 0 ? value : null;
            }

            return null;
        }
    }
}
