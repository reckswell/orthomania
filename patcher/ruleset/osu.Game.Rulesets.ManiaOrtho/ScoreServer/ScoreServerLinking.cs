// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;

namespace osu.Game.Rulesets.ManiaOrtho.ScoreServer
{
    /// <summary>
    /// Links the game to the player's account on the score server by signing in with osu!, in the browser, on osu!'s own page:
    /// the game asks the server for a link (<c>POST /api/link</c>), opens the address it gets, and asks until the player has
    /// signed in (<c>POST /api/link/poll</c>). It then holds the server's own token for this game and sends it with each play,
    /// to that server only. The game never sees the player's osu! password nor any osu! token, and sends none of its own.
    /// </summary>
    public static class ScoreServerLinking
    {
        private static readonly HttpClient shared_http = ScoreServerClient.CreateHttpClient();

        /// <summary>
        /// The link the game holds, if any.
        /// </summary>
        public static ScoreServerLink? Current(ScoreServerConfigManager? config)
        {
            if (config == null)
                return null;

            string token = config.Get<string>(ScoreServerSetting.LinkToken);

            if (string.IsNullOrEmpty(token) || !Uri.TryCreate(config.Get<string>(ScoreServerSetting.LinkServer), UriKind.Absolute, out var server))
                return null;

            return new ScoreServerLink(server, token, config.Get<string>(ScoreServerSetting.LinkUsername), config.Get<int>(ScoreServerSetting.LinkOsuUserId));
        }

        /// <summary>
        /// The link's token, when <paramref name="server"/> is the very server the game is linked to (same scheme, host and port).
        /// </summary>
        public static string? TokenFor(ScoreServerConfigManager? config, Uri server)
            => Current(config) is { } link && ScoreServerSignIn.SameServer(link.Server, server) ? link.Token : null;

        /// <summary>
        /// Keeps a link. The token is set last (and cleared first), so whatever watches it sees the whole link.
        /// </summary>
        public static void Save(ScoreServerConfigManager config, Uri server, LinkStatus linked)
        {
            config.SetValue(ScoreServerSetting.LinkServer, server.GetLeftPart(UriPartial.Authority));
            config.SetValue(ScoreServerSetting.LinkUsername, linked.Username ?? string.Empty);
            config.SetValue(ScoreServerSetting.LinkOsuUserId, linked.OsuUserId is long id && id <= int.MaxValue ? (int)id : 0);
            config.SetValue(ScoreServerSetting.LinkToken, linked.Token ?? string.Empty);
        }

        public static void Clear(ScoreServerConfigManager config)
        {
            config.SetValue(ScoreServerSetting.LinkToken, string.Empty);
            config.SetValue(ScoreServerSetting.LinkServer, string.Empty);
            config.SetValue(ScoreServerSetting.LinkUsername, string.Empty);
            config.SetValue(ScoreServerSetting.LinkOsuUserId, 0);
        }

        /// <summary>
        /// Asks the server for a link. Its address, to open in the browser, is checked to be on that same server.
        /// </summary>
        public static async Task<LinkStart> StartAsync(Uri server, CancellationToken cancellation, HttpClient? http = null)
        {
            var start = await sendAsync<LinkStart>(server, "/api/link", null, null, cancellation, http).ConfigureAwait(false);

            if (string.IsNullOrEmpty(start.DeviceCode) || !Uri.TryCreate(start.Url, UriKind.Absolute, out var url) || !ScoreServerSignIn.SameServer(server, url))
                throw new ScoreServerLinkException("The score server's answer points somewhere else; is the score server address right?");

            return start;
        }

        /// <summary>
        /// Asks every <see cref="LinkStart.Interval"/> seconds until the player has signed in, or said no, or the link expired.
        /// </summary>
        public static async Task<LinkStatus> WaitAsync(Uri server, LinkStart start, CancellationToken cancellation, HttpClient? http = null)
        {
            var interval = TimeSpan.FromSeconds(Math.Clamp(start.Interval, 1, 30));
            var until = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Clamp(start.ExpiresIn, 1, 3600));
            var poll = new Dictionary<string, string> { ["device_code"] = start.DeviceCode };

            while (DateTimeOffset.UtcNow < until)
            {
                await Task.Delay(interval, cancellation).ConfigureAwait(false);

                var status = await sendAsync<LinkStatus>(server, "/api/link/poll", new FormUrlEncodedContent(poll), null, cancellation, http).ConfigureAwait(false);

                if (status.Status == LinkStatus.LINKED && string.IsNullOrEmpty(status.Token))
                    throw new ScoreServerLinkException("The score server said linked but gave no token.");

                if (status.Status != LinkStatus.PENDING)
                    return status;
            }

            return new LinkStatus { Status = LinkStatus.EXPIRED };
        }

        /// <summary>
        /// Tells the server to end the link. The game forgets it either way.
        /// </summary>
        public static async Task UnlinkAsync(ScoreServerLink link, HttpClient? http = null)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                try
                {
                    await sendAsync<object>(link.Server, "/api/link", null, link.Token, timeout.Token, http, HttpMethod.Delete).ConfigureAwait(false);
                }
                catch (Exception e) when (e is ScoreServerLinkException or OperationCanceledException)
                {
                    // The server may be off or reset; the link ends on its side after 180 days unused.
                }
            }
        }

        private static async Task<T> sendAsync<T>(Uri server, string path, HttpContent? content, string? token, CancellationToken cancellation, HttpClient? http,
                                                  HttpMethod? method = null)
            where T : class
        {
            if (ScoreServerClient.IsOsuHost(server.Host) || ScoreServerClient.IsOsuHost(server.IdnHost))
                throw new ScoreServerLinkException($"{server.Host} is osu!'s own server; the game links only to your own score server.");

            using (var request = new HttpRequestMessage(method ?? HttpMethod.Post, server.GetLeftPart(UriPartial.Authority) + path) { Content = content })
            {
                request.Headers.Add(ScoreServerProtocol.CLIENT_HEADER, ManiaOrthoRuleset.SHORT_NAME);

                if (token != null)
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                try
                {
                    using (var response = await (http ?? shared_http).SendAsync(request, cancellation).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                        int status = (int)response.StatusCode;

                        // Not followed (CreateHttpClient): the score server never redirects.
                        if (status >= 300 && status < 400)
                            throw new ScoreServerLinkException($"{server.Authority} redirects elsewhere, which is not followed; is the score server address right?");

                        if (!response.IsSuccessStatusCode)
                            throw new ScoreServerLinkException(errorMessage(body) ?? $"The score server answered {status}.");

                        if (typeof(T) == typeof(object))
                            return (T)new object();

                        try
                        {
                            return JsonConvert.DeserializeObject<T>(body) ?? throw new ScoreServerLinkException("The score server gave an empty answer.");
                        }
                        catch (JsonException)
                        {
                            throw new ScoreServerLinkException($"{server.Authority} did not answer like a score server; is something else using that port?");
                        }
                    }
                }
                catch (HttpRequestException e)
                {
                    throw new ScoreServerLinkException($"The score server is not reachable at {server.Authority} ({e.Message}). Is it running?");
                }
                catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new ScoreServerLinkException($"No answer from {server.Authority}.");
                }
            }
        }

        private static string? errorMessage(string body)
        {
            try
            {
                return JsonConvert.DeserializeAnonymousType(body, new { error = (string?)null })?.error;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The game's link to an account on a score server.
    /// </summary>
    public record ScoreServerLink(Uri Server, string Token, string Username, int OsuUserId);

    public class ScoreServerLinkException : Exception
    {
        public ScoreServerLinkException(string message)
            : base(message)
        {
        }
    }
}
