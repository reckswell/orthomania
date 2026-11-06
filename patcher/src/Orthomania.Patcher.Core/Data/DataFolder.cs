using System;
using System.IO;
using System.Linq;

namespace Orthomania.Patcher.Data
{
    /// <summary>
    /// orthomania's own data folder: what the game side of orthomania keeps for itself (its settings, its play log and replays, plays
    /// waiting for the score server, its score server sign-in). osu!'s own data (maps, skins, scores, settings and the osu! sign-in)
    /// is not here: lazer started by orthomania uses osu!'s data folder exactly as plain lazer does, so a change to osu! itself is
    /// there whichever way osu! was started. The patcher never opens osu!'s data folder; the only file it writes here is a note.
    /// </summary>
    public sealed class DataFolder
    {
        public const string NOTE = "ortho-data.txt";

        /// <summary>
        /// Files only osu!'s own data folder has: its database (<c>client.realm</c>, or <c>client_52.realm</c> named after its schema)
        /// and its settings.
        /// </summary>
        private static readonly string[] osu_data_patterns = { "client*.realm", "game.ini", "game.dev.ini" };

        public string Path { get; }

        public DataFolder(string path)
        {
            Path = PathUtils.Normalise(path);
        }

        /// <summary>
        /// Creates the folder, with a note for people who open it.
        /// </summary>
        public void Prepare()
        {
            Directory.CreateDirectory(Path);

            string note = System.IO.Path.Combine(Path, NOTE);

            if (!File.Exists(note))
            {
                File.WriteAllText(note, "orthomania's own data: its settings, play log, replays and score server sign-in.\r\n"
                                        + "osu!'s data (maps, skins, scores, settings, the osu! sign-in) is not here: osu! keeps it in its own data folder, "
                                        + "the same whether osu! is started plainly or by orthomania.exe.\r\n"
                                        + "orthomania.exe never opens osu!'s data folder.\r\n");
            }
        }

        /// <summary>
        /// Whether this folder holds osu!'s own data (a database or osu!'s settings), as the folder of an earlier orthomania did, or
        /// osu!'s data folder itself does. Only names in this folder are looked at; nothing is opened.
        /// </summary>
        /// <param name="found">The first such file, by name.</param>
        public bool HoldsOsuData(out string? found)
        {
            found = null;

            if (!Directory.Exists(Path))
                return false;

            found = osu_data_patterns.SelectMany(pattern => Directory.EnumerateFiles(Path, pattern, SearchOption.TopDirectoryOnly))
                                     .Select(System.IO.Path.GetFileName)
                                     .FirstOrDefault();

            return found != null;
        }

        /// <summary>
        /// Where osu!(lazer) keeps its data unless it was moved: <c>%APPDATA%\osu</c>. Only the path: the folder is never opened, so
        /// a data folder osu! was moved to (its own <c>storage.ini</c>) is not known here, and <see cref="HoldsOsuData"/> catches that.
        /// </summary>
        public static string OsuDataFolder(string? appData = null)
            => PathUtils.Normalise(System.IO.Path.Combine(appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osu"));
    }
}
