using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using osu.Game.Online.API;

namespace OrthoServer.Storage
{
    /// <summary>
    /// The server's data folder: <c>plays.db</c> (SQLite), <c>beatmaps/&lt;md5&gt;.osu</c> (each rated map, kept so plays can
    /// be rated again when osu!'s calculators change) and <c>replays/&lt;play id&gt;.osr</c>.
    /// </summary>
    public class PlayStore
    {
        public string DataPath { get; }

        private readonly string connectionString;

        static PlayStore()
        {
            SQLitePCL.Batteries_V2.Init();
        }

        public PlayStore(string dataPath)
        {
            DataPath = Path.GetFullPath(dataPath);
            Directory.CreateDirectory(DataPath);
            Directory.CreateDirectory(Path.Combine(DataPath, "beatmaps"));
            Directory.CreateDirectory(Path.Combine(DataPath, "replays"));

            connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(DataPath, "plays.db") }.ToString();

            using (var connection = open())
            {
                execute(connection, """
                                    CREATE TABLE IF NOT EXISTS plays (
                                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        client_score_id TEXT UNIQUE,
                                        played_at TEXT NOT NULL,
                                        received_at TEXT NOT NULL,
                                        passed INTEGER NOT NULL,
                                        rank TEXT,
                                        beatmap_md5 TEXT NOT NULL,
                                        beatmap_id INTEGER NOT NULL,
                                        beatmapset_id INTEGER NOT NULL,
                                        status TEXT NOT NULL,
                                        artist TEXT NOT NULL,
                                        title TEXT NOT NULL,
                                        version TEXT NOT NULL,
                                        creator TEXT NOT NULL,
                                        keys INTEGER NOT NULL,
                                        mods TEXT NOT NULL,
                                        statistics TEXT NOT NULL,
                                        maximum_statistics TEXT NOT NULL,
                                        max_combo INTEGER NOT NULL,
                                        accuracy REAL NOT NULL,
                                        total_score INTEGER NOT NULL,
                                        client_pp REAL,
                                        client_stars REAL,
                                        client_difficulty_version INTEGER,
                                        settings TEXT NOT NULL,
                                        replay_file TEXT,
                                        pp REAL,
                                        stars REAL,
                                        awards_pp INTEGER NOT NULL,
                                        reason TEXT,
                                        calculator_version TEXT NOT NULL
                                    );
                                    CREATE INDEX IF NOT EXISTS plays_beatmap ON plays (beatmap_id, beatmap_md5);
                                    """);

                // Added with accounts: older data folders get the column, their plays belong to nobody until the first account takes them.
                if (!hasColumn(connection, "plays", "user_id"))
                    execute(connection, "ALTER TABLE plays ADD COLUMN user_id INTEGER");
            }
        }

        public string BeatmapPath(string md5) => Path.Combine(DataPath, "beatmaps", md5 + ".osu");

        public string ReplayPath(long playId) => Path.Combine(DataPath, "replays", playId.ToString(CultureInfo.InvariantCulture) + ".osr");

        /// <summary>
        /// Keeps a map's file, once per checksum (the name is the checksum, so an existing file is the same file).
        /// </summary>
        public void SaveBeatmap(string md5, byte[] data)
        {
            string path = BeatmapPath(md5);

            if (!File.Exists(path))
                File.WriteAllBytes(path, data);
        }

        public byte[]? ReadBeatmap(string md5)
        {
            string path = BeatmapPath(md5);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public PlayRecord? FindByClientScoreId(string clientScoreId)
        {
            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plays WHERE client_score_id = $id";
                command.Parameters.AddWithValue("$id", clientScoreId);

                using (var reader = command.ExecuteReader())
                    return reader.Read() ? read(reader) : null;
            }
        }

        /// <summary>
        /// Stores a new play and sets its <see cref="PlayRecord.Id"/>.
        /// </summary>
        public void Insert(PlayRecord play)
        {
            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                                      INSERT INTO plays (client_score_id, user_id, played_at, received_at, passed, rank, beatmap_md5, beatmap_id, beatmapset_id,
                                          status, artist, title, version, creator, keys, mods, statistics, maximum_statistics, max_combo, accuracy,
                                          total_score, client_pp, client_stars, client_difficulty_version, settings, replay_file, pp, stars, awards_pp,
                                          reason, calculator_version)
                                      VALUES ($client_score_id, $user_id, $played_at, $received_at, $passed, $rank, $beatmap_md5, $beatmap_id, $beatmapset_id,
                                          $status, $artist, $title, $version, $creator, $keys, $mods, $statistics, $maximum_statistics, $max_combo, $accuracy,
                                          $total_score, $client_pp, $client_stars, $client_difficulty_version, $settings, $replay_file, $pp, $stars, $awards_pp,
                                          $reason, $calculator_version);
                                      SELECT last_insert_rowid();
                                      """;
                bind(command, play);
                play.Id = (long)command.ExecuteScalar()!;
            }
        }

        public void SetReplayFile(long playId, string replayFile)
        {
            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE plays SET replay_file = $replay_file WHERE id = $id";
                command.Parameters.AddWithValue("$replay_file", replayFile);
                command.Parameters.AddWithValue("$id", playId);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Writes a play's rating (after the calculators changed).
        /// </summary>
        public void UpdateRating(PlayRecord play)
        {
            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                                      UPDATE plays SET pp = $pp, stars = $stars, awards_pp = $awards_pp, reason = $reason, calculator_version = $calculator_version
                                      WHERE id = $id
                                      """;
                command.Parameters.AddWithValue("$pp", (object?)play.Pp ?? DBNull.Value);
                command.Parameters.AddWithValue("$stars", (object?)play.Stars ?? DBNull.Value);
                command.Parameters.AddWithValue("$awards_pp", play.AwardsPp ? 1 : 0);
                command.Parameters.AddWithValue("$reason", (object?)play.Reason ?? DBNull.Value);
                command.Parameters.AddWithValue("$calculator_version", play.CalculatorVersion);
                command.Parameters.AddWithValue("$id", play.Id);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Every play, oldest first.
        /// </summary>
        public List<PlayRecord> All()
        {
            var plays = new List<PlayRecord>();

            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plays ORDER BY id";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        plays.Add(read(reader));
                }
            }

            return plays;
        }

        /// <summary>
        /// Every play of one account, oldest first; with null, the plays that belong to nobody (sent before any account existed).
        /// </summary>
        public List<PlayRecord> ForUser(long? userId) => All().FindAll(p => p.UserId == userId);

        /// <summary>
        /// Gives an account every play that belongs to nobody (plays sent before the first account was made).
        /// </summary>
        public int AdoptUnowned(long userId)
        {
            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE plays SET user_id = $user_id WHERE user_id IS NULL";
                command.Parameters.AddWithValue("$user_id", userId);
                return command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Makes every play of an account that no longer exists belong to nobody. Plays and accounts share <c>plays.db</c>.
        /// </summary>
        public int DisownRemovedAccounts()
        {
            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE plays SET user_id = NULL WHERE user_id IS NOT NULL AND user_id NOT IN (SELECT id FROM users)";
                return command.ExecuteNonQuery();
            }
        }

        private static bool hasColumn(SqliteConnection connection, string table, string column)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column";
                command.Parameters.AddWithValue("$column", column);
                return (long)command.ExecuteScalar()! > 0;
            }
        }

        private SqliteConnection open()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        private static void execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static void bind(SqliteCommand command, PlayRecord play)
        {
            void add(string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

            add("$client_score_id", play.ClientScoreId);
            add("$user_id", play.UserId);
            add("$played_at", play.PlayedAt.ToString("O", CultureInfo.InvariantCulture));
            add("$received_at", play.ReceivedAt.ToString("O", CultureInfo.InvariantCulture));
            add("$passed", play.Passed ? 1 : 0);
            add("$rank", play.Rank);
            add("$beatmap_md5", play.BeatmapMd5);
            add("$beatmap_id", play.BeatmapId);
            add("$beatmapset_id", play.BeatmapSetId);
            add("$status", play.Status);
            add("$artist", play.Artist);
            add("$title", play.Title);
            add("$version", play.Version);
            add("$creator", play.Creator);
            add("$keys", play.Keys);
            add("$mods", JsonConvert.SerializeObject(play.Mods));
            add("$statistics", JsonConvert.SerializeObject(play.Statistics));
            add("$maximum_statistics", JsonConvert.SerializeObject(play.MaximumStatistics));
            add("$max_combo", play.MaxCombo);
            add("$accuracy", play.Accuracy);
            add("$total_score", play.TotalScore);
            add("$client_pp", play.ClientPp);
            add("$client_stars", play.ClientStars);
            add("$client_difficulty_version", play.ClientDifficultyVersion);
            add("$settings", JsonConvert.SerializeObject(play.Settings));
            add("$replay_file", play.ReplayFile);
            add("$pp", play.Pp);
            add("$stars", play.Stars);
            add("$awards_pp", play.AwardsPp ? 1 : 0);
            add("$reason", play.Reason);
            add("$calculator_version", play.CalculatorVersion);
        }

        private static PlayRecord read(SqliteDataReader reader)
        {
            string text(string column) => reader.GetString(reader.GetOrdinal(column));
            string? nullableText(string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));
            double? nullableReal(string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetDouble(reader.GetOrdinal(column));
            long integer(string column) => reader.GetInt64(reader.GetOrdinal(column));

            return new PlayRecord
            {
                Id = integer("id"),
                ClientScoreId = nullableText("client_score_id"),
                UserId = reader.IsDBNull(reader.GetOrdinal("user_id")) ? null : integer("user_id"),
                PlayedAt = DateTimeOffset.Parse(text("played_at"), CultureInfo.InvariantCulture),
                ReceivedAt = DateTimeOffset.Parse(text("received_at"), CultureInfo.InvariantCulture),
                Passed = integer("passed") != 0,
                Rank = nullableText("rank"),
                BeatmapMd5 = text("beatmap_md5"),
                BeatmapId = (int)integer("beatmap_id"),
                BeatmapSetId = (int)integer("beatmapset_id"),
                Status = text("status"),
                Artist = text("artist"),
                Title = text("title"),
                Version = text("version"),
                Creator = text("creator"),
                Keys = (int)integer("keys"),
                Mods = JsonConvert.DeserializeObject<List<APIMod>>(text("mods")) ?? new List<APIMod>(),
                Statistics = JsonConvert.DeserializeObject<Dictionary<string, int>>(text("statistics")) ?? new Dictionary<string, int>(),
                MaximumStatistics = JsonConvert.DeserializeObject<Dictionary<string, int>>(text("maximum_statistics")) ?? new Dictionary<string, int>(),
                MaxCombo = (int)integer("max_combo"),
                Accuracy = reader.GetDouble(reader.GetOrdinal("accuracy")),
                TotalScore = integer("total_score"),
                ClientPp = nullableReal("client_pp"),
                ClientStars = nullableReal("client_stars"),
                ClientDifficultyVersion = reader.IsDBNull(reader.GetOrdinal("client_difficulty_version")) ? null : (int)integer("client_difficulty_version"),
                Settings = JsonConvert.DeserializeObject<Dictionary<string, string?>>(text("settings")) ?? new Dictionary<string, string?>(),
                ReplayFile = nullableText("replay_file"),
                Pp = nullableReal("pp"),
                Stars = nullableReal("stars"),
                AwardsPp = integer("awards_pp") != 0,
                Reason = nullableText("reason"),
                CalculatorVersion = text("calculator_version"),
            };
        }
    }
}
