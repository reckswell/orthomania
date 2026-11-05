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
    }

    /// <summary>
    /// A pair of tokens issued at sign-in, as osu!'s OAuth answers it.
    /// </summary>
    public record IssuedToken(string AccessToken, string RefreshToken, long ExpiresIn);

    /// <summary>
    /// Accounts on this server and their sign-in tokens, in <c>plays.db</c>. Account ids start at 2, as lazer reads 1 as nobody. Passwords are kept only as salted PBKDF2 hashes
    /// and tokens only as SHA-256 hashes, so the database alone signs nobody in.
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
                                      """;
                command.ExecuteNonQuery();
            }
        }

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

        private static Account read(SqliteDataReader reader) => new Account
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Username = reader.GetString(reader.GetOrdinal("username")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            JoinedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("joined_at")), CultureInfo.InvariantCulture),
        };
    }
}
