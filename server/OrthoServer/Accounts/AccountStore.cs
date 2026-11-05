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

        public string Email { get; set; } = string.Empty;

        public DateTimeOffset JoinedAt { get; set; }

        /// <summary>
        /// The osu! account this account signs in with, or null for an account with a password on this server.
        /// </summary>
        public long? OsuUserId { get; set; }

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
    /// A pair of tokens issued at sign-in, as osu!'s OAuth answers it.
    /// </summary>
    public record IssuedToken(string AccessToken, string RefreshToken, long ExpiresIn);

    /// <summary>
    /// Accounts on this server and their sign-in tokens, in <c>plays.db</c>. Account ids start at 2, as lazer reads 1 as nobody.
    /// An account either signs in with osu! (it keeps the osu! user id, name, picture and country: public, nothing else) or has
    /// a password on this server. Passwords are kept only as salted PBKDF2 hashes and tokens only as SHA-256 hashes, so the
    /// database alone signs nobody in.
    /// </summary>
    public class AccountStore
    {
        public const int MIN_PASSWORD_LENGTH = 8;

        /// <summary>
        /// How long an access token lasts; the game refreshes it before then with its refresh token.
        /// </summary>
        public static readonly TimeSpan ACCESS_TOKEN_LIFETIME = TimeSpan.FromDays(1);

        /// <summary>
        /// How long a sign-in lasts without the game starting (a refresh token unused this long is dropped).
        /// </summary>
        public static readonly TimeSpan REFRESH_TOKEN_LIFETIME = TimeSpan.FromDays(90);

        /// <summary>
        /// How long a game's link (<see cref="IssueGameToken"/>) lasts without sending a play.
        /// </summary>
        public static readonly TimeSpan GAME_TOKEN_UNUSED_LIFETIME = TimeSpan.FromDays(180);

        /// <summary>
        /// What every game token starts with, so it is recognisable as this server's (and never taken for one of osu!'s).
        /// </summary>
        public const string GAME_TOKEN_PREFIX = "ortho_";

        // OWASP's figure for PBKDF2-HMAC-SHA256 (2023). Each hash keeps its own count, so raising it later keeps old passwords working.
        private const int pbkdf2_iterations = 600_000;

        // Checked when no account matches, so a sign-in takes as long whether the name exists or not.
        private static readonly Lazy<string> no_account_hash = new Lazy<string>(() => HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))));

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
                                          email TEXT NOT NULL,
                                          password_hash TEXT NOT NULL,
                                          joined_at TEXT NOT NULL
                                      );
                                      CREATE TABLE IF NOT EXISTS tokens (
                                          access_hash TEXT PRIMARY KEY,
                                          refresh_hash TEXT NOT NULL UNIQUE,
                                          user_id INTEGER NOT NULL REFERENCES users (id),
                                          access_expires_at TEXT NOT NULL,
                                          refresh_expires_at TEXT NOT NULL
                                      );
                                      -- Account ids start at 2: lazer treats user id 1 as nobody (no statistics, no avatar).
                                      INSERT INTO sqlite_sequence (name, seq) SELECT 'users', 1 WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = 'users');
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
            }
        }

        /// <summary>
        /// The account an osu! sign-in belongs to, made the first time and kept up to date with the osu! name, picture and
        /// country after that. An account with a password is never taken over by an osu! account of the same name: the osu!
        /// account then gets its id after the name.
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
                          INSERT INTO users (username, email, password_hash, joined_at, osu_user_id, avatar_url, country_code)
                          VALUES ($username, '', '', $joined_at, $osu_user_id, $avatar_url, $country_code);
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

        /// <summary>
        /// The account a bearer token signs in to: a game's link, or a sign-in made at <c>/oauth/token</c>.
        /// </summary>
        public Account? FindBySignIn(string? token) => FindByGameToken(token) ?? FindByAccessToken(token);

        /// <summary>
        /// Makes an account, or says what is wrong with each field (osu!'s registration form names them
        /// <c>username</c>, <c>user_email</c> and <c>password</c>).
        /// </summary>
        public Account? Register(string? username, string? email, string? password, out Dictionary<string, List<string>> errors)
        {
            var found = errors = new Dictionary<string, List<string>>();
            username = username?.Trim() ?? string.Empty;
            email = email?.Trim() ?? string.Empty;
            password ??= string.Empty;

            void error(string field, string message)
            {
                if (!found.TryGetValue(field, out var list))
                    found[field] = list = new List<string>();

                list.Add(message);
            }

            if (username.Length < 3 || username.Length > 15)
                error("username", "Between 3 and 15 characters, please.");
            else if (!isValidUsername(username))
                error("username", "Letters, numbers, spaces, - _ [ ] only, and not starting or ending with a space.");

            if (email.Length > 0 && (!email.Contains('@') || email.Length > 254))
                error("user_email", "That does not look like an e-mail address (it is only kept here, nothing is sent to it).");

            if (password.Length < MIN_PASSWORD_LENGTH)
                error("password", $"At least {MIN_PASSWORD_LENGTH} characters, please.");
            else if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
                error("password", "The password cannot be the username.");

            lock (gate)
            {
                if (errors.Count == 0 && findByUsername(username) != null)
                    error("username", "That username is taken on this server.");

                if (errors.Count > 0)
                    return null;

                var account = new Account { Username = username, Email = email, JoinedAt = DateTimeOffset.Now };

                using (var connection = open())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                                          INSERT INTO users (username, email, password_hash, joined_at) VALUES ($username, $email, $hash, $joined_at);
                                          SELECT last_insert_rowid();
                                          """;
                    command.Parameters.AddWithValue("$username", account.Username);
                    command.Parameters.AddWithValue("$email", account.Email);
                    command.Parameters.AddWithValue("$hash", HashPassword(password));
                    command.Parameters.AddWithValue("$joined_at", account.JoinedAt.ToString("O", CultureInfo.InvariantCulture));
                    account.Id = (long)command.ExecuteScalar()!;
                }

                return account;
            }
        }

        /// <summary>
        /// The account a username (or e-mail address) and password sign in to.
        /// </summary>
        public Account? Authenticate(string? usernameOrEmail, string? password)
        {
            if (string.IsNullOrEmpty(usernameOrEmail) || string.IsNullOrEmpty(password))
                return null;

            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM users WHERE username = $name COLLATE NOCASE OR (email <> '' AND email = $name COLLATE NOCASE) ORDER BY id";
                command.Parameters.AddWithValue("$name", usernameOrEmail.Trim());

                using (var reader = command.ExecuteReader())
                {
                    bool any = false;

                    while (reader.Read())
                    {
                        any = true;

                        if (VerifyPassword(password, reader.GetString(reader.GetOrdinal("password_hash"))))
                            return read(reader);
                    }

                    if (!any)
                        VerifyPassword(password, no_account_hash.Value);
                }
            }

            return null;
        }

        public IssuedToken Issue(long userId)
        {
            string access = newToken();
            string refresh = newToken();
            var now = DateTimeOffset.UtcNow;

            lock (gate)
            {
                using (var connection = open())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                                          DELETE FROM tokens WHERE refresh_expires_at < $now;
                                          INSERT INTO tokens (access_hash, refresh_hash, user_id, access_expires_at, refresh_expires_at)
                                          VALUES ($access, $refresh, $user, $access_expires, $refresh_expires);
                                          """;
                    command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$access", hashToken(access));
                    command.Parameters.AddWithValue("$refresh", hashToken(refresh));
                    command.Parameters.AddWithValue("$user", userId);
                    command.Parameters.AddWithValue("$access_expires", (now + ACCESS_TOKEN_LIFETIME).ToString("O", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$refresh_expires", (now + REFRESH_TOKEN_LIFETIME).ToString("O", CultureInfo.InvariantCulture));
                    command.ExecuteNonQuery();
                }
            }

            return new IssuedToken(access, refresh, (long)ACCESS_TOKEN_LIFETIME.TotalSeconds);
        }

        /// <summary>
        /// Swaps a refresh token for a new pair (the old pair stops working), or null when it is unknown or expired.
        /// </summary>
        public IssuedToken? Refresh(string? refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return null;

            long userId;

            lock (gate)
            {
                using (var connection = open())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM tokens WHERE refresh_hash = $refresh AND refresh_expires_at >= $now RETURNING user_id";
                    command.Parameters.AddWithValue("$refresh", hashToken(refreshToken));
                    command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                    if (command.ExecuteScalar() is not long id)
                        return null;

                    userId = id;
                }
            }

            return Issue(userId);
        }

        /// <summary>
        /// The account an unexpired access token belongs to.
        /// </summary>
        public Account? FindByAccessToken(string? accessToken)
        {
            if (string.IsNullOrEmpty(accessToken))
                return null;

            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                                      SELECT users.* FROM tokens JOIN users ON users.id = tokens.user_id
                                      WHERE tokens.access_hash = $access AND tokens.access_expires_at >= $now
                                      """;
                command.Parameters.AddWithValue("$access", hashToken(accessToken));
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                using (var reader = command.ExecuteReader())
                    return reader.Read() ? read(reader) : null;
            }
        }

        /// <summary>
        /// Ends the sign-in an access token belongs to.
        /// </summary>
        public void Revoke(string? accessToken)
        {
            if (string.IsNullOrEmpty(accessToken))
                return;

            using (var connection = open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM tokens WHERE access_hash = $access";
                command.Parameters.AddWithValue("$access", hashToken(accessToken));
                command.ExecuteNonQuery();
            }
        }

        public Account? Find(long id) => queryOne("SELECT * FROM users WHERE id = $value", id);

        public Account? FindByUsername(string username) => findByUsername(username.Trim());

        /// <summary>
        /// The first account made here: plays sent without signing in are theirs.
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

        public static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, pbkdf2_iterations, HashAlgorithmName.SHA256, 32);
            return FormattableString.Invariant($"pbkdf2-sha256${pbkdf2_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
        }

        public static bool VerifyPassword(string password, string stored)
        {
            string[] parts = stored.Split('$');

            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations))
                return false;

            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static bool isValidUsername(string username)
        {
            if (username[0] == ' ' || username[^1] == ' ' || username.Contains("  ", StringComparison.Ordinal))
                return false;

            foreach (char c in username)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != ' ' && c != '-' && c != '_' && c != '[' && c != ']')
                    return false;
            }

            return true;
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
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = $name";
                command.Parameters.AddWithValue("$name", name);

                if ((long)command.ExecuteScalar()! > 0)
                    return;

                command.CommandText = $"ALTER TABLE users ADD COLUMN {name} {definition}";
                command.ExecuteNonQuery();
            }
        }

        private static Account read(SqliteDataReader reader)
        {
            int osuUserId = reader.GetOrdinal("osu_user_id");

            return new Account
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Username = reader.GetString(reader.GetOrdinal("username")),
                Email = reader.GetString(reader.GetOrdinal("email")),
                JoinedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("joined_at")), CultureInfo.InvariantCulture),
                OsuUserId = reader.IsDBNull(osuUserId) ? null : reader.GetInt64(osuUserId),
                AvatarUrl = reader.GetString(reader.GetOrdinal("avatar_url")),
                CountryCode = reader.GetString(reader.GetOrdinal("country_code")),
            };
        }
    }
}
