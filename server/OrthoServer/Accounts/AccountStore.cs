using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace OrthoServer.Accounts
{
    public class Account
    {
        public long Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public DateTimeOffset JoinedAt { get; set; }

        /// <summary>
        /// The osu! account this account signs in with.
        /// </summary>
        public long OsuUserId { get; set; }

        /// <summary>
        /// The osu! profile picture (public, from osu!), or empty.
        /// </summary>
        public string AvatarUrl { get; set; } = string.Empty;

        /// <summary>
        /// The country on the osu! profile (public, from osu!), or empty.
        /// </summary>
        public string CountryCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// The public part of an osu! profile, as osu!'s <c>/api/v2/me</c> answers it with the <c>identify</c> scope.
    /// </summary>
    public record OsuUser(long Id, string Username, string AvatarUrl, string CountryCode);

    /// <summary>
    /// Accounts on this server and the games linked to them, in <c>plays.db</c>. Every account comes from signing in with osu!
    /// and keeps only the osu! user id, name, picture and country (public). There are no passwords here, and the games' tokens
    /// are kept only as SHA-256 hashes, so the database alone links nothing.
    /// </summary>
    public class AccountStore
    {
        /// <summary>
        /// How long a game's link (<see cref="IssueGameToken"/>) lasts without sending a play.
        /// </summary>
        public static readonly TimeSpan GAME_TOKEN_UNUSED_LIFETIME = TimeSpan.FromDays(180);

        /// <summary>
        /// What every game token starts with, so it is recognisable as this server's (and never taken for one of osu!'s).
        /// </summary>
        public const string GAME_TOKEN_PREFIX = "ortho_";

        private readonly string connectionString;

        private readonly object gate = new object();

        public AccountStore(string dataPath)
        {
            Directory.CreateDirectory(dataPath);
            connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(Path.GetFullPath(dataPath), "plays.db") }.ToString();

            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                                      CREATE TABLE IF NOT EXISTS users (
                                          id INTEGER PRIMARY KEY AUTOINCREMENT,
                                          username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                                          joined_at TEXT NOT NULL
                                      );
                                      -- A game linked to an account by signing in with osu! (POST /api/link).
                                      CREATE TABLE IF NOT EXISTS game_tokens (
                                          token_hash TEXT PRIMARY KEY,
                                          user_id INTEGER NOT NULL REFERENCES users (id),
                                          created_at TEXT NOT NULL,
                                          last_used_at TEXT NOT NULL
                                      );
                                      """;
                command.ExecuteNonQuery();

                addColumn(connection, "osu_user_id", "INTEGER");
                addColumn(connection, "avatar_url", "TEXT NOT NULL DEFAULT ''");
                addColumn(connection, "country_code", "TEXT NOT NULL DEFAULT ''");

                command.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS users_by_osu_user_id ON users (osu_user_id) WHERE osu_user_id IS NOT NULL";
                command.ExecuteNonQuery();

                removePasswordAccounts(connection);
            }
        }

        /// <summary>
        /// The account an osu! sign-in belongs to, made the first time and kept up to date with the osu! name, picture and
        /// country after that. A name another account still holds here (someone who had it on osu! before, and has not signed
        /// in since being renamed) is not taken: the newcomer gets their osu! id after it.
        /// </summary>
        public Account SignInWithOsu(OsuUser user)
        {
            lock (gate)
            {
                var existing = queryOne("SELECT * FROM users WHERE osu_user_id = $value", user.Id);
                var holder = findByUsername(user.Username);
                string username = holder == null || holder.Id == existing?.Id ? user.Username : FormattableString.Invariant($"{user.Username} ({user.Id})");

                using (var connection = open())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = existing == null
                        ? """
                          INSERT INTO users (username, joined_at, osu_user_id, avatar_url, country_code)
                          VALUES ($username, $joined_at, $osu_user_id, $avatar_url, $country_code);
                          """
                        : "UPDATE users SET username = $username, avatar_url = $avatar_url, country_code = $country_code WHERE osu_user_id = $osu_user_id";
                    command.Parameters.AddWithValue("$username", username);
                    command.Parameters.AddWithValue("$joined_at", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$osu_user_id", user.Id);
                    command.Parameters.AddWithValue("$avatar_url", user.AvatarUrl);
                    command.Parameters.AddWithValue("$country_code", user.CountryCode);
                    command.ExecuteNonQuery();
                }

                return queryOne("SELECT * FROM users WHERE osu_user_id = $value", user.Id)!;
            }
        }

        /// <summary>
        /// A token for a game linked to an account: it sends it with each play. It lasts until the game unlinks, or until it has
        /// sent nothing for <see cref="GAME_TOKEN_UNUSED_LIFETIME"/>.
        /// </summary>
        public string IssueGameToken(long userId)
        {
            string token = GAME_TOKEN_PREFIX + newToken();
            string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            lock (gate)
            {
                using (var connection = open())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                                          DELETE FROM game_tokens WHERE last_used_at < $unused_since;
                                          INSERT INTO game_tokens (token_hash, user_id, created_at, last_used_at) VALUES ($hash, $user, $now, $now);
                                          """;
                    command.Parameters.AddWithValue("$unused_since", (DateTimeOffset.UtcNow - GAME_TOKEN_UNUSED_LIFETIME).ToString("O", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$hash", hashToken(token));
                    command.Parameters.AddWithValue("$user", userId);
                    command.Parameters.AddWithValue("$now", now);
                    command.ExecuteNonQuery();
                }
            }

            return token;
        }

        /// <summary>
        /// The account a game token is linked to, or null when it is unknown, unlinked or unused for too long. Counts as a use.
        /// </summary>
        public Account? FindByGameToken(string? token)
        {
            if (string.IsNullOrEmpty(token) || !token.StartsWith(GAME_TOKEN_PREFIX, StringComparison.Ordinal))
                return null;

            var now = DateTimeOffset.UtcNow;

            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                                      UPDATE game_tokens SET last_used_at = $now WHERE token_hash = $hash AND last_used_at >= $unused_since RETURNING user_id
                                      """;
                command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$hash", hashToken(token));
                command.Parameters.AddWithValue("$unused_since", (now - GAME_TOKEN_UNUSED_LIFETIME).ToString("O", CultureInfo.InvariantCulture));

                return command.ExecuteScalar() is long userId ? Find(userId) : null;
            }
        }

        /// <summary>
        /// Unlinks the game a token belongs to. Whether it was linked.
        /// </summary>
        public bool RevokeGameToken(string? token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM game_tokens WHERE token_hash = $hash";
                command.Parameters.AddWithValue("$hash", hashToken(token));
                return command.ExecuteNonQuery() > 0;
            }
        }

        public Account? Find(long id) => queryOne("SELECT * FROM users WHERE id = $value", id);

        public Account? FindByUsername(string username) => findByUsername(username.Trim());

        /// <summary>
        /// The first account made here: plays sent from a game that is not linked are theirs.
        /// </summary>
        public Account? Owner() => queryOne("SELECT * FROM users ORDER BY id LIMIT 1", null);

        public List<Account> All()
        {
            var accounts = new List<Account>();

            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM users ORDER BY id";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        accounts.Add(read(reader));
                }
            }

            return accounts;
        }

        private Account? findByUsername(string username) => queryOne("SELECT * FROM users WHERE username = $value COLLATE NOCASE", username);

        private Account? queryOne(string sql, object? value)
        {
            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;

                if (value != null)
                    command.Parameters.AddWithValue("$value", value);

                using (var reader = command.ExecuteReader())
                    return reader.Read() ? read(reader) : null;
            }
        }

        private static string newToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string hashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private SqliteConnection open()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        private static void addColumn(SqliteConnection connection, string name, string definition)
        {
            if (hasColumn(connection, name))
                return;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"ALTER TABLE users ADD COLUMN {name} {definition}";
                command.ExecuteNonQuery();
            }
        }

        private static bool hasColumn(SqliteConnection connection, string name)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = $name";
                command.Parameters.AddWithValue("$name", name);
                return (long)command.ExecuteScalar()! > 0;
            }
        }

        /// <summary>
        /// Before osu! sign-in, accounts could also be made here with a username and password. Those accounts go, with every
        /// password hash, e-mail address and sign-in token kept for them; their plays stay (<see cref="Scoring.PlayService"/>
        /// gives them to the first account). The file is then rebuilt with what was deleted overwritten, so none of it is left
        /// in the database's free space either.
        /// </summary>
        private static void removePasswordAccounts(SqliteConnection connection)
        {
            if (!hasColumn(connection, "password_hash"))
                return;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                                      PRAGMA secure_delete = ON;
                                      DELETE FROM users WHERE osu_user_id IS NULL;
                                      DROP TABLE IF EXISTS tokens;
                                      ALTER TABLE users DROP COLUMN password_hash;
                                      ALTER TABLE users DROP COLUMN email;
                                      """;
                command.ExecuteNonQuery();

                command.CommandText = "VACUUM";
                command.ExecuteNonQuery();
            }

            Console.WriteLine("Removed the accounts made here with a password (from before osu! sign-in); their plays are kept.");
        }

        private static Account read(SqliteDataReader reader)
        {
            return new Account
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Username = reader.GetString(reader.GetOrdinal("username")),
                JoinedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("joined_at")), CultureInfo.InvariantCulture),
                OsuUserId = reader.GetInt64(reader.GetOrdinal("osu_user_id")),
                AvatarUrl = reader.GetString(reader.GetOrdinal("avatar_url")),
                CountryCode = reader.GetString(reader.GetOrdinal("country_code")),
            };
        }
    }
}
