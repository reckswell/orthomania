// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.ManiaOrtho.ScoreServer
{
    /// <summary>
    /// Sends plays to the player's own score server (orthomania's <c>server/</c>). Every play is first written to
    /// <see cref="OUTBOX_DIRECTORY"/> and leaves it only once the server stored it or refused it for good, so a server that is
    /// away, a wrong address or the game closing mid-send loses nothing; plays go out oldest first.
    /// It never sends anything to osu!'s servers: an address on <c>ppy.sh</c> is never contacted, and
    /// redirects are never followed, so no answer can send a play on to another host.
    /// </summary>
    public class ScoreServerClient
    {
        /// <summary>
        /// Plays waiting for the server, in the play log's folder: one folder each, named so they sort oldest first.
        /// </summary>
        public const string OUTBOX_DIRECTORY = "outbox";

        /// <summary>
        /// Plays the server refused (for example a map file that no longer matches), each kept with the reason in
        /// <see cref="ERROR_FILE"/>. Moving one back to the outbox sends it again.
        /// </summary>
        public const string REFUSED_DIRECTORY = "outbox refused";

        public const string PLAY_FILE = "play.json";
        public const string BEATMAP_FILE = "beatmap.osu";
        public const string REPLAY_FILE = "replay.osr";
        public const string ERROR_FILE = "error.txt";

        /// <summary>
        /// Counts the server's own errors (5xx) for a queued play.
        /// </summary>
        private const string attempts_file = "attempts.txt";

        /// <summary>
        /// A play the server keeps failing on (5xx) is set aside after this many tries, so it cannot hold up the plays behind it.
        /// </summary>
        public const int MAX_SERVER_ERRORS = 5;

        private static readonly HttpClient shared_http = CreateHttpClient();

        /// <summary>
        /// One send at a time, so queued plays reach the server in the order they were made.
        /// </summary>
        private static readonly SemaphoreSlim send_lock = new SemaphoreSlim(1, 1);

        private readonly string outbox;
        private readonly string refused;
        private readonly HttpClient http;
        private readonly Func<Uri, string?>? signIn;

        /// <param name="playLog">The play log's folder (<c>maniaortho</c> in the game's data).</param>
        /// <param name="http">For tests; the game shares one client from <see cref="CreateHttpClient"/>.</param>
        /// <param name="signIn">The token for a server, if the game is linked to that very server
        /// (<see cref="ScoreServerLinking.TokenFor"/>): plays then go to that account.
        /// Asked at each send.</param>
        public ScoreServerClient(Storage playLog, HttpClient? http = null, Func<Uri, string?>? signIn = null)
        {
            outbox = playLog.GetFullPath(OUTBOX_DIRECTORY);
            refused = playLog.GetFullPath(REFUSED_DIRECTORY);
            this.http = http ?? shared_http;
            this.signIn = signIn;
        }

        /// <summary>
        /// A client that never follows a redirect: an answer pointing elsewhere is reported, not obeyed.
        /// </summary>
        public static HttpClient CreateHttpClient() => new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        /// <summary>
        /// Whether <paramref name="host"/> is osu!'s (<c>ppy.sh</c> or any of its subdomains).
        /// </summary>
        public static bool IsOsuHost(string host)
        {
            host = host.TrimEnd('.').ToLowerInvariant();
            return host == "ppy.sh" || host.EndsWith(".ppy.sh", StringComparison.Ordinal);
        }

        /// <summary>
        /// The server's base address, or null and why not.
        /// </summary>
        public static Uri? ParseAddress(string? address, out string? problem)
        {
            problem = null;

            if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                problem = $"\"{address}\" is not a web address (it should look like {ScoreServerConfigManager.DEFAULT_ADDRESS})";
                return null;
            }

            // The name as typed and as it is looked up (an internationalised name resolves by its ASCII form).
            if (IsOsuHost(uri.Host) || IsOsuHost(uri.IdnHost))
            {
                problem = $"{uri.Host} is osu!'s own server; maniaortho plays only go to your own score server";
                return null;
            }

            return uri;
        }

        /// <summary>
        /// Number of plays waiting in the outbox.
        /// </summary>
        public int QueuedCount => Directory.Exists(outbox) ? Directory.GetDirectories(outbox).Count(f => File.Exists(Path.Combine(f, PLAY_FILE))) : 0;

        /// <summary>
        /// Queues <paramref name="play"/>, then sends the outbox in order.
        /// </summary>
        public async Task<ScoreServerOutcome> SendAsync(string? address, PlayUpload play, byte[] beatmap, byte[]? replay)
        {
            await send_lock.WaitAsync().ConfigureAwait(false);

            try
            {
                string playJson = JsonConvert.SerializeObject(play);
                string? folder = null;

                try
                {
                    folder = enqueue(play, playJson, beatmap, replay);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Still worth sending now; it just cannot wait if that fails.
                    Logger.Error(e, "This play could not be saved to the score server's outbox");
                }

                var server = ParseAddress(address, out string? problem);

                if (server == null)
                    return waiting(problem!, 0);

                var flushed = await flushAsync(server).ConfigureAwait(false);

                if (folder != null)
                {
                    if (flushed.Outcomes.TryGetValue(Path.GetFileName(folder), out var outcome))
                        return outcome with { SentFromOutbox = flushed.Sent - (outcome.Kind == ScoreServerOutcomeKind.Sent ? 1 : 0), Queued = QueuedCount };

                    // Behind a play that has to wait: this one waits too, for the same reason.
                    return waiting(flushed.Blocked ?? "earlier plays are still waiting", flushed.Sent);
                }

                var direct = flushed.Blocked != null
                    ? new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, flushed.Blocked)
                    : await postAsync(server, playJson, beatmap, replay).ConfigureAwait(false);

                return direct with { SentFromOutbox = flushed.Sent, Queued = QueuedCount };
            }
            finally
            {
                send_lock.Release();
            }

            ScoreServerOutcome waiting(string reason, int sent)
                => new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, reason) { SentFromOutbox = sent, Queued = QueuedCount };
        }

        /// <summary>
        /// Sends the queued plays, oldest first, until one has to wait.
        /// </summary>
        /// <returns>How many were sent.</returns>
        public async Task<int> FlushAsync(string? address)
        {
            var server = ParseAddress(address, out _);

            if (server == null)
                return 0;

            await send_lock.WaitAsync().ConfigureAwait(false);

            try
            {
                return (await flushAsync(server).ConfigureAwait(false)).Sent;
            }
            finally
            {
                send_lock.Release();
            }
        }

        private async Task<FlushResult> flushAsync(Uri server)
        {
            var result = new FlushResult();

            if (!Directory.Exists(outbox))
                return result;

            foreach (string folder in Directory.GetDirectories(outbox).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(folder);
                string playFile = Path.Combine(folder, PLAY_FILE);
                string beatmapFile = Path.Combine(folder, BEATMAP_FILE);
                string replayFile = Path.Combine(folder, REPLAY_FILE);

                // Written last: without it the folder is still being written, or was cut off.
                if (!File.Exists(playFile))
                    continue;

                ScoreServerOutcome outcome;

                try
                {
                    if (!File.Exists(beatmapFile))
                        outcome = new ScoreServerOutcome(ScoreServerOutcomeKind.Refused, null, "the queued play has no map file");
                    else
                    {
                        outcome = await postAsync(server, await File.ReadAllTextAsync(playFile).ConfigureAwait(false), await File.ReadAllBytesAsync(beatmapFile).ConfigureAwait(false),
                            File.Exists(replayFile) ? await File.ReadAllBytesAsync(replayFile).ConfigureAwait(false) : null).ConfigureAwait(false);
                    }

                    if (outcome.Kind == ScoreServerOutcomeKind.Queued && outcome.ServerError && countServerError(folder) >= MAX_SERVER_ERRORS)
                        outcome = outcome with { Kind = ScoreServerOutcomeKind.Refused, Message = $"{outcome.Message} ({MAX_SERVER_ERRORS} times)" };
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A file held by another program: try again next time, after the plays behind it.
                    Logger.Error(e, $"A queued play ({name}) could not be read");
                    continue;
                }

                result.Outcomes[name] = outcome;

                switch (outcome.Kind)
                {
                    case ScoreServerOutcomeKind.Queued:
                        result.Blocked = outcome.Message;
                        return result;

                    case ScoreServerOutcomeKind.Sent:
                        result.Sent++;
                        // If this fails the play is sent again next time; the server stores a play once (score_id).
                        tryIo(() => Directory.Delete(folder, true), $"A sent play ({name}) could not be removed from the outbox");
                        break;

                    default:
                        if (tryIo(() => setAside(folder, outcome.Message), $"A refused play ({name}) could not be moved to \"{REFUSED_DIRECTORY}\""))
                            result.Outcomes[name] = outcome with { SetAside = true };
                        break;
                }
            }

            return result;
        }

        private async Task<ScoreServerOutcome> postAsync(Uri server, string playJson, byte[] beatmap, byte[]? replay)
        {
            string address = server.AbsoluteUri.TrimEnd('/');

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, address + "/api/plays");
                using var form = createForm(playJson, beatmap, replay);

                request.Content = form;
                request.Headers.Add(ScoreServerProtocol.CLIENT_HEADER, ManiaOrthoRuleset.SHORT_NAME);

                if (signIn?.Invoke(server) is string token)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await http.SendAsync(request).ConfigureAwait(false);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                int status = (int)response.StatusCode;

                // Not followed (CreateHttpClient): a score server never redirects, so the address points somewhere else.
                if (status >= 300 && status < 400)
                    return wrongAddress($"{address} redirects to {response.Headers.Location?.ToString() ?? "another address"}, which is not followed");

                if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.MethodNotAllowed)
                    return wrongAddress($"{address} answered {status}: is the score server address right?");

                if (response.IsSuccessStatusCode)
                {
                    PlayResult? result = null;

                    try
                    {
                        result = JsonConvert.DeserializeObject<PlayResult>(body);
                    }
                    catch (JsonException)
                    {
                    }

                    return result == null
                        ? wrongAddress($"{address} did not answer like a score server: is something else using that port?")
                        : new ScoreServerOutcome(ScoreServerOutcomeKind.Sent, result, null);
                }

                // The server no longer knows the game's link (unlinked there, unused too long, or its data was reset): the play
                // belongs to that account, so it waits for the next send (linked again, or not linked at all) instead of being set aside.
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, $"the score server did not accept the game's link{detail(body)}");

                // An error on the server's side may pass (a restart, a full disk): try again later. A refusal will not.
                if (status >= 500)
                    return new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, $"the score server failed ({status}{detail(body)})") { ServerError = true };

                return new ScoreServerOutcome(ScoreServerOutcomeKind.Refused, null, errorMessage(body) ?? $"the score server answered {status}");
            }
            catch (HttpRequestException e)
            {
                return new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, $"not reachable at {server.Authority} ({e.Message})");
            }
            catch (TaskCanceledException)
            {
                return new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, $"no answer from {server.Authority}");
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
                return new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, $"the connection to {server.Authority} failed ({e.Message})");
            }

            static ScoreServerOutcome wrongAddress(string message) => new ScoreServerOutcome(ScoreServerOutcomeKind.Queued, null, message);

            static MultipartFormDataContent createForm(string playJson, byte[] beatmap, byte[]? replay)
            {
                var form = new MultipartFormDataContent
                {
                    { new StringContent(playJson, Encoding.UTF8, "application/json"), "play" },
                    { new ByteArrayContent(beatmap), "beatmap", BEATMAP_FILE },
                };

                if (replay != null)
                    form.Add(new ByteArrayContent(replay), "replay", REPLAY_FILE);

                return form;
            }

            static string detail(string body) => errorMessage(body) is string error ? $": {error}" : string.Empty;
        }

        /// <returns>The play's folder in the outbox.</returns>
        private string enqueue(PlayUpload play, string playJson, byte[] beatmap, byte[]? replay)
        {
            string name = $"{play.PlayedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)} {play.ScoreId}";
            string folder = Path.Combine(outbox, string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, BEATMAP_FILE), beatmap);

            if (replay != null)
                File.WriteAllBytes(Path.Combine(folder, REPLAY_FILE), replay);

            // Last, so a folder with a play file is complete.
            File.WriteAllText(Path.Combine(folder, PLAY_FILE), playJson);

            return folder;
        }

        private void setAside(string folder, string? reason)
        {
            Directory.CreateDirectory(refused);

            string target = Path.Combine(refused, Path.GetFileName(folder));

            // A play moved back to the outbox by hand and refused again replaces its earlier copy.
            if (Directory.Exists(target))
                Directory.Delete(target, true);

            Directory.Move(folder, target);
            File.WriteAllText(Path.Combine(target, ERROR_FILE), reason ?? string.Empty);
        }

        private static int countServerError(string folder)
        {
            string file = Path.Combine(folder, attempts_file);
            int count = File.Exists(file) && int.TryParse(File.ReadAllText(file), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stored) ? stored + 1 : 1;

            File.WriteAllText(file, count.ToString(CultureInfo.InvariantCulture));
            return count;
        }

        private static bool tryIo(Action action, string failure)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Logger.Error(e, failure);
                return false;
            }
        }

        private static string? errorMessage(string body)
        {
            try
            {
                return JObject.Parse(body)["error"]?.ToString();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private class FlushResult
        {
            public int Sent;

            /// <summary>
            /// Why sending stopped, if a play has to wait.
            /// </summary>
            public string? Blocked;

            /// <summary>
            /// What happened to each play tried, by folder name.
            /// </summary>
            public readonly Dictionary<string, ScoreServerOutcome> Outcomes = new Dictionary<string, ScoreServerOutcome>();
        }
    }

    public enum ScoreServerOutcomeKind
    {
        /// <summary>
        /// The server stored the play (or had it already).
        /// </summary>
        Sent,

        /// <summary>
        /// The play waits in the outbox: the server is away, failed, or the address does not lead to it.
        /// </summary>
        Queued,

        /// <summary>
        /// The server refused the play; it is kept in <see cref="ScoreServerClient.REFUSED_DIRECTORY"/> with the reason.
        /// </summary>
        Refused,
    }

    /// <param name="Kind">What happened to the play.</param>
    /// <param name="Result">The server's rating, when <see cref="ScoreServerOutcomeKind.Sent"/>.</param>
    /// <param name="Message">Why it waits or was refused.</param>
    public record ScoreServerOutcome(ScoreServerOutcomeKind Kind, PlayResult? Result, string? Message)
    {
        /// <summary>
        /// Queued plays sent before this one.
        /// </summary>
        public int SentFromOutbox { get; init; }

        /// <summary>
        /// Plays still waiting afterwards (this one included, if it waits).
        /// </summary>
        public int Queued { get; init; }

        /// <summary>
        /// It waits because the server failed on it (5xx), which counts towards <see cref="ScoreServerClient.MAX_SERVER_ERRORS"/>.
        /// </summary>
        public bool ServerError { get; init; }

        /// <summary>
        /// Refused and kept in <see cref="ScoreServerClient.REFUSED_DIRECTORY"/>.
        /// </summary>
        public bool SetAside { get; init; }
    }
}
