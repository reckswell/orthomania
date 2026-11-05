using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Folders
{
    /// <summary>
    /// One lazer release as orthomania runs it: hard links to that release's official files, plus orthomania's own files
    /// (<see cref="VersionFolders"/>), described by <see cref="MARKER"/>.
    /// </summary>
    public sealed class VersionFolder
    {
        /// <summary>
        /// The folder's record of what it holds. Written last, so a folder without it was never finished.
        /// </summary>
        public const string MARKER = "orthomania-folder.json";

        public string Directory { get; }

        public FolderRecord Record { get; }

        public LazerVersion Lazer => LazerVersion.Parse(Record.Lazer);

        public string GameExecutable => Path.Combine(Directory, LazerInstall.GAME_EXECUTABLE);

        public VersionFolder(string directory, FolderRecord record)
        {
            Directory = directory;
            Record = record;
        }

        public static VersionFolder? Open(string directory)
        {
            string marker = Path.Combine(directory, MARKER);

            if (!File.Exists(marker))
                return null;

            try
            {
                var record = JsonSerializer.Deserialize<FolderRecord>(File.ReadAllText(marker), FolderRecord.JSON_OPTIONS);

                if (record == null || record.Format != FolderRecord.CURRENT_FORMAT || !LazerVersion.TryParse(record.Lazer, out _))
                    return null;

                return new VersionFolder(directory, record);
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                return null;
            }
        }

        public override string ToString() => $"osu! {Record.Lazer} with orthomania {Record.Build}";
    }

    public sealed class FolderRecord
    {
        /// <summary>
        /// 2: lazer keeps its data where plain lazer does, and orthomania's settings go in <see cref="GameSettings.FILE_NAME"/>. A folder
        /// of format 1 (with a <c>storage.ini</c> pointing osu! at a data folder of its own) is not opened, so it is built again.
        /// </summary>
        public const int CURRENT_FORMAT = 2;

        internal static readonly JsonSerializerOptions JSON_OPTIONS = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        public int Format { get; set; } = CURRENT_FORMAT;

        /// <summary>
        /// The lazer release whose files are linked here.
        /// </summary>
        public string Lazer { get; set; } = string.Empty;

        /// <summary>
        /// The orthomania build beside them.
        /// </summary>
        public string Build { get; set; } = string.Empty;

        /// <summary>
        /// orthomania's own data folder, as <see cref="GameSettings.FILE_NAME"/> names it.
        /// </summary>
        public string DataFolder { get; set; } = string.Empty;

        /// <summary>
        /// Whether <see cref="GameSettings.FILE_NAME"/> lets the game use the osu! sign-in.
        /// </summary>
        public bool OsuSignIn { get; set; }

        /// <summary>
        /// Whether lazer's files are hard links (false: copies, when links were refused).
        /// </summary>
        public bool Linked { get; set; }

        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Lazer's files, by path relative to the folder, with their sizes.
        /// </summary>
        public List<FolderEntry> LazerFiles { get; set; } = new List<FolderEntry>();

        /// <summary>
        /// orthomania's own files (<see cref="GameSettings.FILE_NAME"/> and the build's files), with their hashes.
        /// </summary>
        public List<OwnFile> OwnFiles { get; set; } = new List<OwnFile>();
    }

    public sealed record FolderEntry(string Path, long Size);

    public sealed record OwnFile(string Name, string Sha256, long Size);
}
