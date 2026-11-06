// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Text.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Rulesets.ManiaOrtho.Scoring;

namespace osu.Game.Rulesets.ManiaOrtho.Configuration
{
    /// <summary>
    /// What orthomania.exe told this build in <c>orthomania-game.json</c> beside the DLL: where orthomania's own data folder is, and
    /// whether the game may use the osu! sign-in osu! saved.
    /// <para>
    /// Started by orthomania.exe, everything of orthomania's own (its settings, the play log and replays, plays waiting for the score
    /// server, the score server sign-in) goes to <see cref="Folder"/>, never to osu!'s data, and osu! stays signed out unless
    /// <see cref="OsuSignIn"/>. Without the file (the tests, a build loaded by hand) orthomania keeps them in lazer's storage, as it
    /// did before orthomania.exe.
    /// </para>
    /// </summary>
    public sealed class OrthomaniaData
    {
        public const string FILE_NAME = "orthomania-game.json";

        /// <summary>
        /// The file's format, as orthomania.exe writes it.
        /// </summary>
        public const int FORMAT = 1;

        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static OrthomaniaData? current;
        private static bool read;
        private static readonly object reading = new object();

        /// <summary>
        /// orthomania's own data folder.
        /// </summary>
        public string Folder { get; }

        /// <summary>
        /// Whether the game may use, and so change, the osu! sign-in osu! saved. Off unless the person turned it on in orthomania.exe.
        /// </summary>
        public bool OsuSignIn { get; }

        public OrthomaniaData(string folder, bool osuSignIn)
        {
            Folder = Path.GetFullPath(folder);
            OsuSignIn = osuSignIn;
        }

        /// <summary>
        /// This build's file, read once; null when there is none.
        /// </summary>
        public static OrthomaniaData? Current
        {
            get
            {
                lock (reading)
                {
                    if (!read)
                    {
                        current = Read(Path.GetDirectoryName(typeof(OrthomaniaData).Assembly.Location));
                        read = true;
                    }

                    return current;
                }
            }
            internal set
            {
                lock (reading)
                {
                    current = value;
                    read = true;
                }
            }
        }

        /// <summary>
        /// orthomania's own storage: <see cref="Folder"/>, or lazer's when there is no <see cref="Current"/>.
        /// </summary>
        public static Storage StorageFor(Storage gameStorage) => Current is { } data ? new NativeStorage(data.Folder) : gameStorage;

        /// <summary>
        /// The play log's folder (<see cref="PlayLog.DIRECTORY"/>), with the replays and the score server's outbox in it.
        /// </summary>
        public static Storage PlayLogFor(Storage gameStorage) => StorageFor(gameStorage).GetStorageForDirectory(PlayLog.DIRECTORY);

        /// <summary>
        /// The file in <paramref name="folder"/>; null when there is none.
        /// </summary>
        /// <remarks>
        /// A file that is there but cannot be used still keeps orthomania's data out of osu!'s and osu! signed out, with orthomania's
        /// data in <see cref="FallbackFolder"/>: whatever wrote the file meant both.
        /// </remarks>
        public static OrthomaniaData? Read(string? folder)
        {
            if (string.IsNullOrEmpty(folder))
                return null;

            string path = Path.Combine(folder, FILE_NAME);

            if (!File.Exists(path))
                return null;

            try
            {
                var file = JsonSerializer.Deserialize<GameFile>(File.ReadAllText(path), json_options);

                if (file == null)
                    return fallback(path, "it is empty");

                if (file.Format != FORMAT)
                    return fallback(path, $"it is format {file.Format}, and this build reads format {FORMAT}");

                if (string.IsNullOrWhiteSpace(file.DataFolder) || !Path.IsPathFullyQualified(file.DataFolder))
                    return fallback(path, $"its data folder (\"{file.DataFolder}\") is not a full path");

                return new OrthomaniaData(file.DataFolder, file.OsuSignIn);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
            {
                return fallback(path, e.Message);
            }
        }

        /// <summary>
        /// Where orthomania.exe keeps orthomania's data when it is not told otherwise: <c>%APPDATA%\orthomania\data</c>.
        /// </summary>
        public static string FallbackFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "orthomania", "data");

        private static OrthomaniaData fallback(string path, string problem)
        {
            Logger.Log($"orthomania: {path} could not be used ({problem}); keeping orthomania's data in {FallbackFolder} and osu! signed out.",
                LoggingTarget.Runtime, LogLevel.Important);

            return new OrthomaniaData(FallbackFolder, false);
        }

        private sealed class GameFile
        {
            public int Format { get; set; }

            public string? DataFolder { get; set; }

            public bool OsuSignIn { get; set; }
        }
    }
}
