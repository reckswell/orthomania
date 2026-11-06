using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OrthoServer.Accounts
{
    /// <summary>
    /// The OAuth application of this server on osu!, from <c>osu-oauth.json</c> in the data folder. The secret is the server
    /// owner's, never part of the repository.
    /// </summary>
    public class OsuOAuthConfig
    {
        [JsonProperty("client_id")]
        public long ClientId { get; set; }

        [JsonProperty("client_secret")]
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// The Application Callback URL registered on osu!, or null for <c>http://127.0.0.1:{port}/auth/osu/callback</c>.
        /// </summary>
        [JsonProperty("callback_url", NullValueHandling = NullValueHandling.Ignore)]
        public string? CallbackUrl { get; set; }
    }

    public class OsuSignInException : Exception
    {
        public OsuSignInException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Signing in with osu!, through osu!'s public OAuth (authorization code grant, <c>identify</c> scope only). The player types
    /// their password on osu.ppy.sh, never here. With the code osu! hands back, the server reads the public profile
    /// (<c>/api/v2/me</c>: id, username, picture, country), then revokes the token at once and keeps nothing of osu!'s: no
    /// password, no token, no e-mail address. Nothing else of osu!'s API is ever called.
    /// </summary>
    public class OsuSignIn : IDisposable
    {
        public const string CONFIG_FILE = "osu-oauth.json";

        public const string CALLBACK_PATH = "/auth/osu/callback";

        /// <summary>
        /// Where osu! lists its OAuth applications, to register this server's.
        /// </summary>
        public const string REGISTER_URL = "https://osu.ppy.sh/home/account/edit#new-oauth-application";

        /// <summary>
        /// osu!. Only the tests talk to anything else (a stand-in on this computer).
        /// </summary>
        public static readonly Uri OSU = new Uri("https://osu.ppy.sh/");

        private readonly string configPath;
        private readonly Uri site;
        private readonly HttpClient http;

        public OsuSignIn(string dataPath)
            : this(dataPath, OSU)
        {
        }

        internal OsuSignIn(string dataPath, Uri site)
        {
            configPath = Path.Combine(Path.GetFullPath(dataPath), CONFIG_FILE);
            this.site = site;

            // A redirect is never followed: the code and the token go to osu! and nowhere else.
            http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("orthomania-score-server");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public string ConfigPath => configPath;

        /// <summary>
        /// The application's settings, or null with what is missing or wrong.
        /// </summary>
        public OsuOAuthConfig? Config(out string? problem)
        {
            problem = null;

            if (!File.Exists(configPath))
            {
                problem = $"Sign-in with osu! is not set up on this server yet: run server/scripts/setup-osu-sign-in (it writes {configPath}).";
                return null;
            }

            try
            {
                var config = JsonConvert.DeserializeObject<OsuOAuthConfig>(File.ReadAllText(configPath));

                if (config == null || config.ClientId <= 0 || string.IsNullOrWhiteSpace(config.ClientSecret))
                    problem = $"{configPath} needs client_id and client_secret (run server/scripts/setup-osu-sign-in again).";
                else if (config.CallbackUrl != null && !IsValidCallback(config.CallbackUrl))
                    problem = $"callback_url in {configPath} must end in {CALLBACK_PATH}, over https or to this computer over http.";
                else
                    return config;
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                problem = $"{configPath} could not be read: {e.Message}";
            }

            return null;
        }

        public bool IsSetUp => Config(out _) != null;

        public static bool IsValidCallback(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
               && uri.AbsolutePath == CALLBACK_PATH && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.UserInfo);

        /// <summary>
        /// osu!'s page where the player signs in and allows this server to read their public profile.
        /// </summary>
        public Uri AuthorizeUrl(OsuOAuthConfig config, Uri callback, string state)
        {
            string query = string.Join('&',
                "client_id=" + config.ClientId,
                "redirect_uri=" + Uri.EscapeDataString(callback.AbsoluteUri),
                "response_type=code",
                "scope=identify",
                "state=" + Uri.EscapeDataString(state));

            return new Uri(site, "oauth/authorize?" + query);
        }

        /// <summary>
        /// Swaps the code osu! handed back for a token, reads the public profile with it, and revokes it.
        /// </summary>
        public async Task<OsuUser> CompleteAsync(OsuOAuthConfig config, Uri callback, string code, CancellationToken cancellation)
        {
            string accessToken = await exchange(config, callback, code, cancellation).ConfigureAwait(false);

            try
            {
                return await me(accessToken, cancellation).ConfigureAwait(false);
            }
            finally
            {
                await revoke(accessToken).ConfigureAwait(false);
            }
        }

        private async Task<string> exchange(OsuOAuthConfig config, Uri callback, string code, CancellationToken cancellation)
        {
            using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
                   {
                       ["client_id"] = config.ClientId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                       ["client_secret"] = config.ClientSecret,
                       ["code"] = code,
                       ["grant_type"] = "authorization_code",
                       ["redirect_uri"] = callback.AbsoluteUri,
                   }))
            {
                var answer = await send(HttpMethod.Post, "oauth/token", null, content, cancellation).ConfigureAwait(false);

                if (answer.Status != 200 || (string?)answer.Json?["access_token"] is not { Length: > 0 } token)
                {
                    string? reason = (string?)answer.Json?["error_description"] ?? (string?)answer.Json?["message"] ?? (string?)answer.Json?["error"];
                    throw new OsuSignInException($"osu! did not accept the sign-in ({answer.Status}{(reason != null ? ": " + reason : string.Empty)}).");
                }

                return token;
            }
        }

        private async Task<OsuUser> me(string accessToken, CancellationToken cancellation)
        {
            var answer = await send(HttpMethod.Get, "api/v2/me", accessToken, null, cancellation).ConfigureAwait(false);

            if (answer.Status != 200 || answer.Json?["id"]?.Type != JTokenType.Integer || (string?)answer.Json["username"] is not { Length: > 0 } username)
                throw new OsuSignInException($"osu! did not give the profile ({answer.Status}).");

            return new OsuUser((long)answer.Json["id"]!, username, (string?)answer.Json["avatar_url"] ?? string.Empty, (string?)answer.Json["country_code"] ?? string.Empty);
        }

        private async Task revoke(string accessToken)
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var answer = await send(HttpMethod.Delete, "api/v2/oauth/tokens/current", accessToken, null, timeout.Token).ConfigureAwait(false);

                    if (answer.Status is not (200 or 204))
                        Console.WriteLine($"osu! did not confirm revoking a sign-in's token ({answer.Status}); it expires by itself within a day.");
                }
            }
            catch (Exception e) when (e is OsuSignInException or OperationCanceledException)
            {
                Console.WriteLine($"Could not revoke a sign-in's token on osu! ({e.Message}); it expires by itself within a day.");
            }
        }

        private async Task<(int Status, JObject? Json)> send(HttpMethod method, string path, string? accessToken, HttpContent? content, CancellationToken cancellation)
        {
            using (var request = new HttpRequestMessage(method, new Uri(site, path)) { Content = content })
            {
                if (accessToken != null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                try
                {
                    using (var response = await http.SendAsync(request, cancellation).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                        JObject? json = null;

                        try
                        {
                            json = body.Length > 0 ? JObject.Parse(body) : null;
                        }
                        catch (JsonException)
                        {
                        }

                        return ((int)response.StatusCode, json);
                    }
                }
                catch (HttpRequestException e)
                {
                    throw new OsuSignInException($"Could not reach osu! ({e.Message}).");
                }
                catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new OsuSignInException("osu! did not answer in time.");
                }
            }
        }

        public void Dispose() => http.Dispose();
    }
}
