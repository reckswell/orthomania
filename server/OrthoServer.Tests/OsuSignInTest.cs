using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using ManiaOrtho.ScoreServerContract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NUnit.Framework;
using OrthoServer.Accounts;
using OrthoServer.Scoring;

namespace OrthoServer.Tests
{
    /// <summary>
    /// Linking the game by signing in with osu!, on the real server, against a stand-in for osu!'s OAuth on this computer. The
    /// stand-in answers as osu!'s API documents it, and records every request the server makes to it.
    /// </summary>
    [TestFixture]
    public class OsuSignInTest
    {
        private const long client_id = 4242;
        private const string client_secret = "client-secret-for-tests";
        private const string osu_code = "code-from-osu";
        private const string osu_access_token = "osu-access-token-never-kept";
        private const string osu_refresh_token = "osu-refresh-token-never-kept";
        private const long osu_user_id = 12345678;

        private string dataPath = null!;
        private WebApplication app = null!;
        private WebApplication osu = null!;
        private HttpClient game = null!;
        private HttpClient browser = null!;
        private string address = null!;
        private string osuAddress = null!;

        private readonly ConcurrentQueue<string> osuRequests = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> revoked = new ConcurrentQueue<string>();
        private string osuUsername = "osu player";
        private bool osuRedirectsTokenRequests;

        [SetUp]
        public async Task SetUp()
        {
            dataPath = Path.Combine(Path.GetTempPath(), "ortho-server-test-" + Guid.NewGuid().ToString("N"));
            osuRequests.Clear();
            revoked.Clear();
            osuUsername = "osu player";
            osuRedirectsTokenRequests = false;

            osu = fakeOsu();
            await osu.StartAsync();
            osuAddress = addressOf(osu);

            app = ServerHost.Create(new ServerOptions { DataPath = dataPath, Port = 0, CheckLiveVersion = false, OsuSite = new Uri(osuAddress + "/") });
            await app.StartAsync();
            address = addressOf(app);

            game = new HttpClient { BaseAddress = new Uri(address) };
            game.DefaultRequestHeaders.Add(ScoreServerProtocol.CLIENT_HEADER, "1");

            // A browser, as far as the server can tell: follows nothing by itself, so each redirect can be checked.
            browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(address) };
        }

        [TearDown]
        public async Task TearDown()
        {
            game.Dispose();
            browser.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            await osu.StopAsync();
            await osu.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dataPath, true);
        }

        [Test]
        public async Task TestLinkBySigningInWithOsu()
        {
            configureOsu();

            var link = await startLink();
            Assert.That(link.Url, Is.EqualTo($"{address}/auth/osu/start?link={link.UserCode}"));
            Assert.That(link.UserCode, Does.Match("^[A-Z]{4}-[A-Z]{4}$"));
            Assert.That((await poll(link)).Status, Is.EqualTo(LinkStatus.PENDING));

            var (authorize, cookie) = await openInBrowser(link);

            // osu!'s own sign-in page, asking for the public profile only.
            Assert.That(authorize.GetLeftPart(UriPartial.Path), Is.EqualTo(osuAddress + "/oauth/authorize"));
            var query = HttpUtility.ParseQueryString(authorize.Query);
            Assert.That(query["client_id"], Is.EqualTo(client_id.ToString()));
            Assert.That(query["redirect_uri"], Is.EqualTo(address + "/auth/osu/callback"));
            Assert.That(query["response_type"], Is.EqualTo("code"));
            Assert.That(query["scope"], Is.EqualTo("identify"));
            Assert.That(query["state"], Is.Not.Empty);
            Assert.That(osuRequests, Is.Empty, "the server called osu! before the player signed in");

            var page = await callback(query["state"]!, cookie, osu_code);
            string html = await page.Content.ReadAsStringAsync();
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK), html);
            Assert.That(html, Does.Contain("Signed in as osu player").And.Contain(link.UserCode));

            // Exactly: the code for a token, the public profile, and the token given back.
            Assert.That(osuRequests, Is.EqualTo(new[] { "POST /oauth/token", "GET /api/v2/me", "DELETE /api/v2/oauth/tokens/current" }));
            Assert.That(revoked, Is.EqualTo(new[] { osu_access_token }));

            var linked = await poll(link);
            Assert.That(linked.Status, Is.EqualTo(LinkStatus.LINKED));
            Assert.That(linked.Token, Does.StartWith(AccountStore.GAME_TOKEN_PREFIX));
            Assert.That(linked.Username, Is.EqualTo("osu player"));
            Assert.That(linked.OsuUserId, Is.EqualTo(osu_user_id));
            Assert.That((await poll(link)).Status, Is.EqualTo(LinkStatus.EXPIRED), "the token was given twice");

            var account = accounts.All().Single();
            Assert.That(account.OsuUserId, Is.EqualTo(osu_user_id));
            Assert.That(account.CountryCode, Is.EqualTo("NZ"));
            Assert.That(account.AvatarUrl, Is.EqualTo("https://a.ppy.sh/12345678"));
            Assert.That(account.Email, Is.Empty);
            Assert.That(accounts.Authenticate("osu player", "anything"), Is.Null, "an osu! account has no password here");

            // The game's plays now go to that account.
            byte[] map = TestPlays.Map();
            var upload = await post(TestPlays.Upload(map), map, linked.Token);
            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), await upload.Content.ReadAsStringAsync());
            Assert.That(app.Services.GetRequiredService<PlayService>().Store.ForUser(account.Id), Has.Count.EqualTo(1));

            // Nothing of osu!'s is kept, and this server's own token only as a hash.
            string stored = everythingStored();
            Assert.That(stored, Does.Not.Contain(osu_access_token).And.Not.Contain(osu_refresh_token).And.Not.Contain(osu_code));
            Assert.That(stored, Does.Not.Contain(linked.Token!));
            Assert.That(stored, Does.Not.Contain("hidden@example.com"));

            // Unlinking ends the token.
            using (var unlink = new HttpRequestMessage(HttpMethod.Delete, "/api/link"))
            {
                unlink.Headers.Authorization = new AuthenticationHeaderValue("Bearer", linked.Token);
                Assert.That((await game.SendAsync(unlink)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            }

            byte[] other = TestPlays.Map(notes: 300);
            Assert.That((await post(TestPlays.Upload(other), other, linked.Token)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task TestSignInAgainKeepsTheAccount()
        {
            configureOsu();

            await linkOnce();
            osuUsername = "renamed on osu";
            await linkOnce();

            var account = accounts.All().Single();
            Assert.That(account.Username, Is.EqualTo("renamed on osu"));
            Assert.That(account.OsuUserId, Is.EqualTo(osu_user_id));
        }

        [Test]
        public async Task TestPasswordAccountIsNotTakenOver()
        {
            configureOsu();
            Assert.That(accounts.Register("osu player", "", "a password here", out _), Is.Not.Null);

            await linkOnce();

            var all = accounts.All();
            Assert.That(all, Has.Count.EqualTo(2));
            Assert.That(all[0].OsuUserId, Is.Null);
            Assert.That(all[0].Username, Is.EqualTo("osu player"));
            Assert.That(all[1].OsuUserId, Is.EqualTo(osu_user_id));
            Assert.That(all[1].Username, Is.EqualTo($"osu player ({osu_user_id})"));
        }

        [Test]
        public async Task TestNotSetUp()
        {
            var response = await game.PostAsync("/api/link", null);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("setup-osu-sign-in"));

            var page = await browser.GetAsync("/auth/osu/start?link=BBBB-CCCC");
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(osuRequests, Is.Empty);
        }

        [Test]
        public async Task TestWebPageCannotStartALink()
        {
            configureOsu();

            using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/link"))
            {
                request.Headers.Add("Origin", "https://example.com");
                Assert.That((await game.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            }

            // Without the game's header either.
            Assert.That((await browser.PostAsync("/api/link", null)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task TestSignInFromAnotherBrowserIsRefused()
        {
            configureOsu();

            var link = await startLink();
            var (authorize, _) = await openInBrowser(link);
            string state = HttpUtility.ParseQueryString(authorize.Query)["state"]!;

            // osu! sends some other browser back with this state: it lacks the cookie of the one that started.
            var page = await callback(state, "orthomania_osu_sign_in=not-the-right-one", osu_code);
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(osuRequests, Is.Empty, "the code was used");
            Assert.That((await poll(link)).Status, Is.EqualTo(LinkStatus.PENDING));
        }

        [Test]
        public async Task TestStateIsUsedOnce()
        {
            configureOsu();

            var link = await startLink();
            var (authorize, cookie) = await openInBrowser(link);
            string state = HttpUtility.ParseQueryString(authorize.Query)["state"]!;

            Assert.That((await callback(state, cookie, osu_code)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await callback(state, cookie, osu_code)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(osuRequests.Count(r => r == "POST /oauth/token"), Is.EqualTo(1));
        }

        [Test]
        public async Task TestDeniedOnOsu()
        {
            configureOsu();

            var link = await startLink();
            var (authorize, cookie) = await openInBrowser(link);
            string state = HttpUtility.ParseQueryString(authorize.Query)["state"]!;

            var page = await browser.SendAsync(withCookie(new HttpRequestMessage(HttpMethod.Get, $"/auth/osu/callback?error=access_denied&state={Uri.EscapeDataString(state)}"), cookie));
            Assert.That(await page.Content.ReadAsStringAsync(), Does.Contain("Not signed in"));
            Assert.That((await poll(link)).Status, Is.EqualTo(LinkStatus.DENIED));
            Assert.That(osuRequests, Is.Empty);
        }

        [Test]
        public async Task TestRedirectFromOsuIsNotFollowed()
        {
            configureOsu();
            osuRedirectsTokenRequests = true;

            var link = await startLink();
            var (authorize, cookie) = await openInBrowser(link);

            var page = await callback(HttpUtility.ParseQueryString(authorize.Query)["state"]!, cookie, osu_code);
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(osuRequests, Is.EqualTo(new[] { "POST /oauth/token" }), "the redirect was followed");
            Assert.That((await poll(link)).Status, Is.EqualTo(LinkStatus.PENDING), "a failed sign-in can be tried again");
        }

        [Test]
        public async Task TestOtherHostIsSentToTheCallbackHost()
        {
            configureOsu();
            var link = await startLink();
            int port = new Uri(address).Port;

            using (var request = new HttpRequestMessage(HttpMethod.Get, $"/auth/osu/start?link={link.UserCode}"))
            {
                request.Headers.Host = $"localhost:{port}";
                var response = await browser.SendAsync(request);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                Assert.That(response.Headers.Location!.AbsoluteUri, Is.EqualTo($"http://127.0.0.1:{port}/auth/osu/start?link={link.UserCode}"));
                Assert.That(response.Headers.Contains("Set-Cookie"), Is.False);
            }
        }

        [Test]
        public void TestLinksExpire()
        {
            var links = new SignInLinks();
            var now = DateTimeOffset.UtcNow;
            links.Now = () => now;

            var (deviceCode, userCode) = links.Create()!.Value;
            Assert.That(links.IsWaiting(userCode), Is.True);
            Assert.That(links.IsWaiting(userCode.ToLowerInvariant().Replace("-", " ")), Is.True, "codes are read loosely");

            now += SignInLinks.LIFETIME;
            Assert.That(links.IsWaiting(userCode), Is.False);
            Assert.That(links.BeginSignIn(userCode), Is.Null);
            Assert.That(links.Poll(deviceCode).State, Is.EqualTo(LinkState.Expired));
        }

        [Test]
        public void TestCallbackSetting()
        {
            Assert.That(OsuSignIn.IsValidCallback("http://127.0.0.1:7727/auth/osu/callback"), Is.True);
            Assert.That(OsuSignIn.IsValidCallback("https://scores.example.com/auth/osu/callback"), Is.True);
            Assert.That(OsuSignIn.IsValidCallback("http://scores.example.com/auth/osu/callback"), Is.False, "plain http off this computer");
            Assert.That(OsuSignIn.IsValidCallback("http://127.0.0.1:7727/elsewhere"), Is.False);
        }

        private AccountStore accounts => app.Services.GetRequiredService<PlayService>().Accounts;

        private void configureOsu() => File.WriteAllText(Path.Combine(dataPath, OsuSignIn.CONFIG_FILE),
            JsonConvert.SerializeObject(new OsuOAuthConfig { ClientId = client_id, ClientSecret = client_secret }));

        private async Task<LinkStart> startLink()
        {
            var response = await game.PostAsync("/api/link", null);
            string body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            return JsonConvert.DeserializeObject<LinkStart>(body)!;
        }

        private async Task<LinkStatus> poll(LinkStart link)
        {
            var response = await game.PostAsync("/api/link/poll", new FormUrlEncodedContent(new Dictionary<string, string> { ["device_code"] = link.DeviceCode }));
            return JsonConvert.DeserializeObject<LinkStatus>(await response.Content.ReadAsStringAsync())!;
        }

        /// <summary>
        /// Opens the link's address as a browser would: where it is sent on to, and the cookie it is given.
        /// </summary>
        private async Task<(Uri Authorize, string Cookie)> openInBrowser(LinkStart link)
        {
            var response = await browser.GetAsync(link.Url);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await response.Content.ReadAsStringAsync());

            string setCookie = response.Headers.GetValues("Set-Cookie").Single();
            Assert.That(setCookie, Does.Contain("httponly").IgnoreCase.And.Contain("samesite=lax").IgnoreCase);

            return (response.Headers.Location!, setCookie.Split(';')[0]);
        }

        private Task<HttpResponseMessage> callback(string state, string cookie, string code)
            => browser.SendAsync(withCookie(new HttpRequestMessage(HttpMethod.Get, $"/auth/osu/callback?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}"), cookie));

        private async Task linkOnce()
        {
            var link = await startLink();
            var (authorize, cookie) = await openInBrowser(link);
            Assert.That((await callback(HttpUtility.ParseQueryString(authorize.Query)["state"]!, cookie, osu_code)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await poll(link)).Status, Is.EqualTo(LinkStatus.LINKED));
        }

        private static HttpRequestMessage withCookie(HttpRequestMessage request, string cookie)
        {
            request.Headers.Add("Cookie", cookie);
            return request;
        }

        private Task<HttpResponseMessage> post(PlayUpload upload, byte[] map, string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/plays")
            {
                Content = new MultipartFormDataContent
                {
                    { new StringContent(JsonConvert.SerializeObject(upload), Encoding.UTF8, "application/json"), "play" },
                    { new ByteArrayContent(map), "beatmap", "map.osu" },
                },
            };

            if (token != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return game.SendAsync(request);
        }

        /// <summary>
        /// Every file in the data folder, as text.
        /// </summary>
        private string everythingStored()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            return string.Concat(Directory.GetFiles(dataPath, "*", SearchOption.AllDirectories)
                                          .Where(f => Path.GetFileName(f) != OsuSignIn.CONFIG_FILE)
                                          .Select(f => Encoding.Latin1.GetString(File.ReadAllBytes(f))));
        }

        private static string addressOf(WebApplication application)
            => application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        /// <summary>
        /// osu!'s OAuth endpoints and <c>/me</c>, as osu!'s API documentation describes them.
        /// </summary>
        private WebApplication fakeOsu()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            var fake = builder.Build();

            fake.Use(async (context, next) =>
            {
                osuRequests.Enqueue($"{context.Request.Method} {context.Request.Path}");
                await next();
            });

            fake.MapPost("/oauth/token", async (HttpRequest request) =>
            {
                if (osuRedirectsTokenRequests)
                    return Results.Redirect($"{osuAddress}/elsewhere");

                var form = await request.ReadFormAsync();

                bool valid = form["client_id"] == client_id.ToString() && form["client_secret"] == client_secret && form["grant_type"] == "authorization_code"
                             && form["code"] == osu_code && form["redirect_uri"] == address + "/auth/osu/callback";

                if (!valid)
                    return Results.Json(new { error = "invalid_grant", error_description = "The code or the client is wrong." }, statusCode: 400);

                return Results.Json(new { token_type = "Bearer", expires_in = 86400, access_token = osu_access_token, refresh_token = osu_refresh_token });
            });

            fake.MapGet("/api/v2/me", (HttpRequest request) =>
            {
                if (request.Headers.Authorization != $"Bearer {osu_access_token}")
                    return Results.Json(new { authentication = "basic" }, statusCode: 401);

                return Results.Json(new Dictionary<string, object?>
                {
                    ["id"] = osu_user_id,
                    ["username"] = osuUsername,
                    ["avatar_url"] = "https://a.ppy.sh/12345678",
                    ["country_code"] = "NZ",
                    ["is_restricted"] = false,
                    // Not in an identify answer; here to show the server keeps only what it needs.
                    ["email"] = "hidden@example.com",
                });
            });

            fake.MapDelete("/api/v2/oauth/tokens/current", (HttpRequest request) =>
            {
                revoked.Enqueue(request.Headers.Authorization.ToString()["Bearer ".Length..]);
                return Results.NoContent();
            });

            fake.MapFallback(() => Results.NotFound());

            return fake;
        }
    }
}
