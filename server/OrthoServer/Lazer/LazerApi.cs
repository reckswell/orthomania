using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using OrthoServer.Accounts;
using OrthoServer.Scoring;

namespace OrthoServer.Lazer
{
    /// <summary>
    /// The part of osu!'s web API that lazer uses, so the game signs in here with its own sign-in panel (Settings &gt; Online &gt;
    /// Server): OAuth password and refresh grants at <c>/oauth/token</c>, registration at <c>/users</c>, and under <c>/api/v2</c>
    /// the signed-in user, profiles with this server's pp, score lists, and empty answers for what this server does not have
    /// (friends, chat, notifications). Written from lazer's requests and response models, not from osu-web.
    /// </summary>
    public static class LazerApi
    {
        /// <summary>
        /// What lazer is told when it asks to submit a play of its own (osu!, taiko, catch, plain osu!mania): this server has no map
        /// files for those and rates maniaortho plays, which come with theirs.
        /// </summary>
        public const string SUBMISSION_REFUSED = "This server rates maniaortho plays only; this play is kept in your local scores.";

        private static readonly ConcurrentDictionary<string, byte> unknown_requests = new ConcurrentDictionary<string, byte>();

        public static void Map(WebApplication app, PlayService service)
        {
            var accounts = service.Accounts;

            app.MapPost("/oauth/token", async (HttpRequest request) =>
            {
                var form = request.HasFormContentType ? await request.ReadFormAsync().ConfigureAwait(false) : null;
                string? grant = form?["grant_type"];

                switch (grant)
                {
                    case "password":
                    {
                        var account = accounts.Authenticate(form!["username"], form["password"]);

                        if (account == null)
                            return oauthError("invalid_grant", "Wrong username or password (an account on this server, not your osu! one).");

                        return token(accounts.Issue(account.Id));
                    }

                    case "refresh_token":
                    {
                        var issued = accounts.Refresh(form!["refresh_token"]);
                        return issued != null ? token(issued) : oauthError("invalid_grant", "This sign-in has ended; sign in again.");
                    }

                    default:
                        return oauthError("unsupported_grant_type", "Only password and refresh_token grants are supported.");
                }
            });

            app.MapPost("/users", async (HttpRequest request) =>
            {
                var form = request.HasFormContentType ? await request.ReadFormAsync().ConfigureAwait(false) : null;
                var account = service.Register(form?["user[username]"], form?["user[user_email]"], form?["user[password]"], out var errors);

                if (account == null)
                    return lazerJson(new Dictionary<string, object?> { ["form_error"] = new Dictionary<string, object?> { ["user"] = errors } }, StatusCodes.Status422UnprocessableEntity);

                Console.WriteLine($"Account made: {account.Username} (id {account.Id}).");
                return lazerJson(LazerJson.CompactUser(account));
            });

            var api = app.MapGroup("/api/v2");

            api.MapDelete("/oauth/tokens/current", (HttpContext context) =>
            {
                accounts.Revoke(bearer(context.Request));
                return Results.NoContent();
            });

            api.MapGet("/me/{ruleset?}", (HttpContext context, string? ruleset) =>
                withUser(context, accounts, me => lazerJson(LazerJson.User(figures(service)[me.Id], ruleset))));

            api.MapGet("/me/beatmapset-favourites", (HttpContext context) =>
                withUser(context, accounts, _ => lazerJson(new Dictionary<string, object?> { ["beatmapset_ids"] = Array.Empty<int>() })));

            api.MapGet("/users/{lookup}/{ruleset?}", (HttpContext context, string lookup, string? ruleset, string? key) =>
                withUser(context, accounts, _ =>
                {
                    var account = find(accounts, lookup, key);

                    return account == null
                        ? notFound("No such user on this server.")
                        : lazerJson(LazerJson.User(figures(service)[account.Id], ruleset));
                }));

            api.MapGet("/users", (HttpContext context) =>
                withUser(context, accounts, _ =>
                {
                    var all = figures(service);
                    var ids = context.Request.Query["ids[]"].Select(id => long.TryParse(id, out long value) ? value : -1).ToHashSet();

                    return lazerJson(new Dictionary<string, object?>
                    {
                        ["users"] = all.Values.Where(f => ids.Contains(f.Account.Id)).Select(f => LazerJson.User(f, "mania")).ToArray(),
                    });
                }));

            api.MapGet("/users/{id:long}/scores/{type}", (HttpContext context, long id, string type, string? mode, int? limit, int? offset) =>
                withUser(context, accounts, _ =>
                {
                    if (!figures(service).TryGetValue(id, out var user))
                        return notFound("No such user on this server.");

                    if (!LazerJson.ShowsPlays(mode))
                        return lazerJson(Array.Empty<object>());

                    int skip = Math.Max(offset ?? 0, 0), take = Math.Clamp(limit ?? 5, 1, 100);

                    switch (type)
                    {
                        case "best":
                            return lazerJson(user.Profile.Top.Skip(skip).Take(take).Select(t => LazerJson.Score(t.Play, user.Account, t.Weight)).ToArray());

                        case "recent":
                            bool fails = context.Request.Query["include_fails"] == "1";
                            return lazerJson(user.Plays.Where(p => p.PlayedAt > DateTimeOffset.Now.AddDays(-1) && (fails || p.Passed))
                                                 .OrderByDescending(p => p.PlayedAt)
                                                 .Skip(skip).Take(take)
                                                 .Select(p => LazerJson.Score(p, user.Account)).ToArray());

                        default:
                            return lazerJson(Array.Empty<object>());
                    }
                }));

            // The profile's "most played beatmaps": maps by how often they were played here (literal, so it wins over {type} below).
            api.MapGet("/users/{id:long}/beatmapsets/most_played", (HttpContext context, long id, int? limit, int? offset) =>
                withUser(context, accounts, _ =>
                {
                    if (!figures(service).TryGetValue(id, out var user))
                        return notFound("No such user on this server.");

                    return lazerJson(user.Plays.GroupBy(p => p.BeatmapKey)
                                         .Select(g => (Count: g.Count(), Latest: g.MaxBy(p => p.Id)!))
                                         .OrderByDescending(m => m.Count).ThenByDescending(m => m.Latest.Id)
                                         .Skip(Math.Max(offset ?? 0, 0)).Take(Math.Clamp(limit ?? 5, 1, 100))
                                         .Select(m => new Dictionary<string, object?>
                                         {
                                             ["beatmap_id"] = m.Latest.BeatmapId,
                                             ["count"] = m.Count,
                                             ["beatmap"] = LazerJson.Beatmap(m.Latest),
                                             ["beatmapset"] = LazerJson.BeatmapSet(m.Latest),
                                         }).ToArray());
                }));

            // Lists this server has nothing for: lazer reads them after signing in and on the profile.
            foreach (string path in new[]
                     {
                         "/friends", "/blocks", "/users/{id}/beatmapsets/{type}", "/users/{id}/recent_activity", "/users/{id}/kudosu", "/chat/channels",
                     })
                api.MapGet(path, (HttpContext context) => withUser(context, accounts, _ => lazerJson(Array.Empty<object>())));

            api.MapGet("/chat/updates", (HttpContext context) => withUser(context, accounts, _ => lazerJson(new Dictionary<string, object?>
            {
                ["presence"] = Array.Empty<object>(),
                ["messages"] = Array.Empty<object>(),
                ["silences"] = Array.Empty<object>(),
            })));

            api.MapPost("/chat/ack", (HttpContext context) =>
                withUser(context, accounts, _ => lazerJson(new Dictionary<string, object?> { ["silences"] = Array.Empty<object>() })));

            api.MapGet("/notifications", (HttpContext context) => withUser(context, accounts, _ => lazerJson(new Dictionary<string, object?>
            {
                ["has_more"] = false,
                ["notifications"] = Array.Empty<object>(),
                ["notification_endpoint"] = $"{(context.Request.IsHttps ? "wss" : "ws")}://{context.Request.Host}/notifications",
            })));

            api.MapGet("/seasonal-backgrounds", () => lazerJson(new Dictionary<string, object?>
            {
                ["ends_at"] = DateTimeOffset.Now.AddDays(1),
                ["backgrounds"] = Array.Empty<object>(),
            }));

            api.MapPost("/beatmaps/{id}/solo/scores", (HttpContext context) => withUser(context, accounts, _ => lazerError(SUBMISSION_REFUSED, StatusCodes.Status403Forbidden)));

            // Lazer's live notifications (and chat) connect here. Nothing is ever pushed; the socket only stays open so the game
            // does not keep reconnecting.
            app.Map("/notifications", async (HttpContext context) =>
            {
                if (!context.WebSockets.IsWebSocketRequest || accounts.FindByAccessToken(bearer(context.Request)) == null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                using (var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false))
                    await drain(socket, context.RequestAborted).ConfigureAwait(false);
            });

            // Anything else lazer asks for: said once in the console, answered with an error lazer shows as a message.
            app.MapFallback((HttpContext context) =>
            {
                string what = $"{context.Request.Method} {context.Request.Path}";

                // Lazer's live hubs (multiplayer, spectating, who is online) are not served; it retries them quietly now and then.
                if (!context.Request.Path.StartsWithSegments("/signalr") && unknown_requests.TryAdd(what, 0))
                    Console.WriteLine($"Not served (this server does not do that): {what}");

                return lazerError("This server does not do that.", StatusCodes.Status404NotFound);
            });
        }

        /// <summary>
        /// The account a request's bearer token signs in to.
        /// </summary>
        public static Account? SignedIn(HttpRequest request, AccountStore accounts) => accounts.FindByAccessToken(bearer(request));

        /// <summary>
        /// Whether a request carries a sign-in at all (valid or not).
        /// </summary>
        public static bool CarriesSignIn(HttpRequest request) => bearer(request) != null;

        private static string? bearer(HttpRequest request)
        {
            string? header = request.Headers.Authorization;
            return header != null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
        }

        private static IResult withUser(HttpContext context, AccountStore accounts, Func<Account, IResult> answer)
        {
            var account = SignedIn(context.Request, accounts);

            // osu!'s answer to a missing or expired token; lazer signs out and asks to sign in again.
            return account == null ? lazerJson(new Dictionary<string, object?> { ["authentication"] = "basic" }, StatusCodes.Status401Unauthorized) : answer(account);
        }

        private static Account? find(AccountStore accounts, string lookup, string? key)
        {
            if (key != "username" && long.TryParse(lookup, out long id))
                return accounts.Find(id);

            return accounts.FindByUsername(lookup.TrimStart('@'));
        }

        private static Dictionary<long, LazerJson.UserFigures> figures(PlayService service) => LazerJson.Figures(service.Accounts.All(), service.Store.All());

        private static IResult token(IssuedToken issued) => lazerJson(new Dictionary<string, object?>
        {
            ["token_type"] = "Bearer",
            ["expires_in"] = issued.ExpiresIn,
            ["access_token"] = issued.AccessToken,
            ["refresh_token"] = issued.RefreshToken,
        });

        private static IResult oauthError(string error, string hint) => lazerJson(new Dictionary<string, object?>
        {
            ["error"] = error,
            ["error_description"] = hint,
            ["hint"] = hint,
            ["message"] = hint,
        }, StatusCodes.Status400BadRequest);

        private static IResult notFound(string message) => lazerError(message, StatusCodes.Status404NotFound);

        private static IResult lazerError(string message, int status) => lazerJson(new Dictionary<string, object?> { ["error"] = message }, status);

        private static IResult lazerJson(object value, int status = StatusCodes.Status200OK)
            => Results.Content(JsonConvert.SerializeObject(value), "application/json; charset=utf-8", statusCode: status);

        private static async Task drain(WebSocket socket, CancellationToken cancellation)
        {
            byte[] buffer = new byte[4096];

            try
            {
                while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
                {
                    var received = await socket.ReceiveAsync(buffer, cancellation).ConfigureAwait(false);

                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException)
            {
            }
        }
    }
}
