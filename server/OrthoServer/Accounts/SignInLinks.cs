using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OrthoServer.Accounts
{
    /// <summary>
    /// Games waiting to be linked to an account, and the osu! sign-ins under way for them, kept in memory for
    /// <see cref="LIFETIME"/> (a restart only means starting again from the game).
    /// <para>
    /// A link has two codes. The device code stays in the game, which alone collects the sign-in with it. The user code is in
    /// the address the game opens and on the page the sign-in ends on, so the player can see the two belong together. Each
    /// sign-in for a link has its own state, used once, and is bound to the browser that started it by a cookie, so a sign-in
    /// started elsewhere cannot finish on someone else's link.
    /// </para>
    /// </summary>
    public class SignInLinks
    {
        public static readonly TimeSpan LIFETIME = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Seconds the game waits between polls.
        /// </summary>
        public const int POLL_INTERVAL = 2;

        private const int max_waiting = 32;

        // No vowels (no words) and nothing that reads as another letter or a digit.
        private const string user_code_letters = "BCDFGHJKLMNPQRSTVWXZ";

        private readonly object gate = new object();
        private readonly Dictionary<string, Link> byDevice = new Dictionary<string, Link>();
        private readonly Dictionary<string, Link> byUserCode = new Dictionary<string, Link>();
        private readonly Dictionary<string, SignIn> signIns = new Dictionary<string, SignIn>();

        internal Func<DateTimeOffset> Now = () => DateTimeOffset.UtcNow;

        /// <summary>
        /// A new link, or null while too many are waiting.
        /// </summary>
        public (string DeviceCode, string UserCode)? Create()
        {
            lock (gate)
            {
                dropExpired();

                if (byDevice.Count >= max_waiting)
                    return null;

                string deviceCode = randomToken();
                string userCode;

                do
                    userCode = newUserCode();
                while (byUserCode.ContainsKey(userCode));

                var link = new Link(hash(deviceCode), userCode, Now() + LIFETIME);
                byDevice[link.DeviceHash] = link;
                byUserCode[userCode] = link;

                return (deviceCode, Format(userCode));
            }
        }

        /// <summary>
        /// Whether a user code belongs to a link still waiting for its sign-in.
        /// </summary>
        public bool IsWaiting(string? userCode)
        {
            lock (gate)
                return find(userCode) is { State: LinkState.Waiting };
        }

        /// <summary>
        /// Starts an osu! sign-in for a waiting link: the state to send to osu! and the secret for the browser's cookie.
        /// </summary>
        public (string State, string BrowserSecret)? BeginSignIn(string? userCode)
        {
            lock (gate)
            {
                dropExpired();

                if (find(userCode) is not { State: LinkState.Waiting } link)
                    return null;

                string state = randomToken();
                string secret = randomToken();
                signIns[state] = new SignIn(link.UserCode, hash(secret), Now() + LIFETIME);

                return (state, secret);
            }
        }

        /// <summary>
        /// The user code an osu! sign-in was started for, if the state is known, unexpired and unused and the browser holds its
        /// secret. A state is used up by this, whatever the answer.
        /// </summary>
        public string? TakeSignIn(string? state, string? browserSecret)
        {
            if (string.IsNullOrEmpty(state))
                return null;

            lock (gate)
            {
                dropExpired();

                if (!signIns.Remove(state, out var signIn) || string.IsNullOrEmpty(browserSecret))
                    return null;

                bool sameBrowser = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(signIn.BrowserHash), Encoding.ASCII.GetBytes(hash(browserSecret)));

                return sameBrowser && find(signIn.UserCode) is { State: LinkState.Waiting } ? Format(signIn.UserCode) : null;
            }
        }

        public void Complete(string userCode, long accountId) => settle(userCode, LinkState.Linked, accountId);

        public void Deny(string userCode) => settle(userCode, LinkState.Denied, null);

        /// <summary>
        /// Where a link stands, for the game polling with its device code. A linked or denied link is answered once and then
        /// forgotten.
        /// </summary>
        public (LinkState State, long? AccountId) Poll(string? deviceCode)
        {
            if (string.IsNullOrEmpty(deviceCode))
                return (LinkState.Expired, null);

            lock (gate)
            {
                dropExpired();

                if (!byDevice.TryGetValue(hash(deviceCode), out var link))
                    return (LinkState.Expired, null);

                if (link.State != LinkState.Waiting)
                    forget(link);

                return (link.State, link.AccountId);
            }
        }

        /// <summary>
        /// A user code as shown: two groups of four letters.
        /// </summary>
        public static string Format(string userCode) => userCode.Length == 8 ? $"{userCode[..4]}-{userCode[4..]}" : userCode;

        private void settle(string userCode, LinkState state, long? accountId)
        {
            lock (gate)
            {
                if (find(userCode) is not { State: LinkState.Waiting } link)
                    return;

                link.State = state;
                link.AccountId = accountId;

                // Its other sign-ins (another tab) can no longer finish.
                foreach (string other in signIns.Where(s => s.Value.UserCode == link.UserCode).Select(s => s.Key).ToList())
                    signIns.Remove(other);
            }
        }

        private Link? find(string? userCode)
        {
            if (userCode == null)
                return null;

            string normal = new string(userCode.Where(char.IsAsciiLetter).Select(char.ToUpperInvariant).ToArray());
            return byUserCode.TryGetValue(normal, out var link) && link.ExpiresAt > Now() ? link : null;
        }

        private void dropExpired()
        {
            var now = Now();

            foreach (var link in byDevice.Values.Where(l => l.ExpiresAt <= now).ToList())
                forget(link);

            foreach (string state in signIns.Where(s => s.Value.ExpiresAt <= now).Select(s => s.Key).ToList())
                signIns.Remove(state);
        }

        private void forget(Link link)
        {
            byDevice.Remove(link.DeviceHash);
            byUserCode.Remove(link.UserCode);
        }

        private static string newUserCode()
        {
            char[] code = new char[8];

            for (int i = 0; i < code.Length; i++)
                code[i] = user_code_letters[RandomNumberGenerator.GetInt32(user_code_letters.Length)];

            return new string(code);
        }

        private static string randomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private class Link
        {
            public readonly string DeviceHash;
            public readonly string UserCode;
            public readonly DateTimeOffset ExpiresAt;

            public LinkState State = LinkState.Waiting;
            public long? AccountId;

            public Link(string deviceHash, string userCode, DateTimeOffset expiresAt)
            {
                DeviceHash = deviceHash;
                UserCode = userCode;
                ExpiresAt = expiresAt;
            }
        }

        private record SignIn(string UserCode, string BrowserHash, DateTimeOffset ExpiresAt);
    }

    public enum LinkState
    {
        Waiting,
        Linked,
        Denied,
        Expired,
    }
}
