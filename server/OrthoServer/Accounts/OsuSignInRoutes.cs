using System;
using System.Collections.Generic;
using ManiaOrtho.ScoreServerContract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using OrthoServer.Scoring;
using OrthoServer.Web;

namespace OrthoServer.Accounts
{
    /// <summary>
    /// Linking the game to an account by signing in with osu!, like a TV app signs in through a phone:
    /// <list type="number">
    /// <item><c>POST /api/link</c> (the game): a device code for the game and an address with a user code.</item>
    /// <item><c>GET /auth/osu/start?link=</c> (the browser, opened by the game): on to osu!'s own sign-in page.</item>
    /// <item><c>GET /auth/osu/callback</c> (the browser, sent back by osu!): the server reads the public profile with osu!'s
    /// code, revokes osu!'s token and makes or updates the account.</item>
    /// <item><c>POST /api/link/poll</c> (the game, meanwhile): once signed in, this server's token for the game.</item>
    /// </list>
    /// <c>DELETE /api/link</c> unlinks the game whose token it carries.
    /// </summary>
    public static class OsuSignInRoutes
    {
        public const string START_PATH = "/auth/osu/start";

        private const string browser_cookie = "orthomania_osu_sign_in";

        private static readonly JsonSerializerSettings json_settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        public static void Map(WebApplication app, PlayService service, OsuSignIn osu, SignInLinks links)
        {
            var accounts = service.Accounts;

            app.MapPost("/api/link", (HttpContext context) =>
            {
                if (osu.Config(out string? problem) is not { } config)
                    return Results.Json(new { error = problem }, statusCode: StatusCodes.Status503ServiceUnavailable);

                if (links.Create() is not var (deviceCode, userCode))
                    return Results.Json(new { error = "Too many sign-ins are waiting; try again in a few minutes." }, statusCode: StatusCodes.Status429TooManyRequests);

                return json(new LinkStart
                {
                    DeviceCode = deviceCode,
                    UserCode = userCode,
                    Url = new Uri(callback(config, context), START_PATH + "?link=" + Uri.EscapeDataString(userCode)).AbsoluteUri,
                    ExpiresIn = (int)SignInLinks.LIFETIME.TotalSeconds,
                    Interval = SignInLinks.POLL_INTERVAL,
                });
            });

            app.MapPost("/api/link/poll", async (HttpRequest request) =>
            {
                string? deviceCode = request.HasFormContentType ? (await request.ReadFormAsync().ConfigureAwait(false))["device_code"].ToString() : null;
                var (state, accountId) = links.Poll(deviceCode);

                switch (state)
                {
                    case LinkState.Waiting:
                        return json(new LinkStatus { Status = LinkStatus.PENDING });

                    case LinkState.Linked when accountId is long id && accounts.Find(id) is { } account:
                        return json(new LinkStatus
                        {
                            Status = LinkStatus.LINKED,
                            Token = accounts.IssueGameToken(id),
                            Username = account.Username,
                            OsuUserId = account.OsuUserId,
                        });

                    case LinkState.Denied:
                        return json(new LinkStatus { Status = LinkStatus.DENIED });

                    default:
                        return json(new LinkStatus { Status = LinkStatus.EXPIRED });
                }
            });

            app.MapDelete("/api/link", (HttpRequest request) =>
                accounts.RevokeGameToken(BearerToken.Of(request)) ? Results.NoContent() : Results.Json(new { error = "This game is not linked." }, statusCode: StatusCodes.Status404NotFound));

            app.MapGet(START_PATH, (HttpContext context, string? link) =>
            {
                if (osu.Config(out string? problem) is not { } config)
                    return page(StatusCodes.Status503ServiceUnavailable, "Sign-in with osu! is not set up", problem!);

                var back = callback(config, context);

                // The cookie below must reach the address osu! sends the browser back to.
                if (!string.Equals(context.Request.Host.Value, back.Authority, StringComparison.OrdinalIgnoreCase))
                    return Results.Redirect(new Uri(back, START_PATH + context.Request.QueryString).AbsoluteUri);

                if (links.BeginSignIn(link) is not var (state, secret))
                    return page(StatusCodes.Status400BadRequest, "This sign-in has expired", "It was used already, or is more than 10 minutes old. Start again from the game.");

                context.Response.Cookies.Append(browser_cookie, secret, cookie(back));
                context.Response.Headers.CacheControl = "no-store";
                return Results.Redirect(osu.AuthorizeUrl(config, back, state).AbsoluteUri);
            });

            app.MapGet(OsuSignIn.CALLBACK_PATH, async (HttpContext context, string? code, string? state, string? error) =>
            {
                string? secret = context.Request.Cookies[browser_cookie];
                context.Response.Headers.CacheControl = "no-store";

                if (osu.Config(out string? problem) is not { } config)
                    return page(StatusCodes.Status503ServiceUnavailable, "Sign-in with osu! is not set up", problem!);

                var back = callback(config, context);
                context.Response.Cookies.Delete(browser_cookie, cookie(back));

                if (links.TakeSignIn(state, secret) is not { } userCode)
                {
                    return page(StatusCodes.Status400BadRequest, "This sign-in did not start here",
                        "It was started in another browser, was used already, or is more than 10 minutes old. Start again from the game.");
                }

                if (error != null || string.IsNullOrEmpty(code))
                {
                    links.Deny(userCode);
                    return page(StatusCodes.Status200OK, "Not signed in", "You did not allow it on osu!, so the game is not linked. You can start again from the game at any time.");
                }

                try
                {
                    var user = await osu.CompleteAsync(config, back, code, context.RequestAborted).ConfigureAwait(false);
                    var account = service.SignInWithOsu(user);
                    links.Complete(userCode, account.Id);

                    return Results.Content(Page.Message($"Signed in as {account.Username}", new[]
                    {
                        "The game showing this code now sends your plays to your account here. You can close this tab.",
                        "This server read only your public osu! profile (name, picture, country) and has already given osu!'s sign-in back. "
                        + "It never sees your osu! password.",
                    }, userCode), "text/html; charset=utf-8");
                }
                catch (OsuSignInException e)
                {
                    return page(StatusCodes.Status502BadGateway, "Sign-in with osu! did not work", e.Message, ("Try again", new Uri(back, START_PATH + "?link=" + Uri.EscapeDataString(userCode)).AbsoluteUri));
                }
            });
        }

        /// <summary>
        /// Where osu! sends the browser back to: the registered callback, or this server on 127.0.0.1.
        /// </summary>
        private static Uri callback(OsuOAuthConfig config, HttpContext context)
            => new Uri(config.CallbackUrl ?? FormattableString.Invariant($"http://127.0.0.1:{context.Connection.LocalPort}{OsuSignIn.CALLBACK_PATH}"));

        private static CookieOptions cookie(Uri back) => new CookieOptions
        {
            HttpOnly = true,
            // Sent on osu!'s redirect back (a top-level navigation), never with a request another site's page makes.
            SameSite = SameSiteMode.Lax,
            Secure = back.Scheme == Uri.UriSchemeHttps,
            Path = "/auth/osu",
            MaxAge = SignInLinks.LIFETIME,
        };

        private static IResult page(int status, string title, string text, (string, string)? link = null)
            => Results.Content(Page.Message(title, new[] { text }, link: link), "text/html; charset=utf-8", statusCode: status);

        private static IResult json(object value) => Results.Content(JsonConvert.SerializeObject(value, json_settings), "application/json; charset=utf-8");
    }
}
