using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManiaOrtho.ScoreServerContract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OrthoServer.Lazer;
using OrthoServer.Scoring;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;

namespace OrthoServer.Tests
{
    /// <summary>
    /// The part of osu!'s web API lazer signs in with, on the real server: every answer is read back with lazer's own response
    /// types (from the same NuGet osu.Game the server builds against), as the game reads it.
    /// </summary>
    [TestFixture]
    public class LazerApiTest
    {
        private const string password = "maniaortho-test";

        private string dataPath = null!;
        private WebApplication app = null!;
        private HttpClient http = null!;
        private string address = null!;

        [SetUp]
        public async Task SetUp()
        {
            dataPath = Path.Combine(Path.GetTempPath(), "ortho-server-test-" + Guid.NewGuid().ToString("N"));
            app = ServerHost.Create(new ServerOptions { DataPath = dataPath, Port = 0, CheckLiveVersion = false });
            await app.StartAsync();

            address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            http = new HttpClient { BaseAddress = new Uri(address) };
            // As lazer's requests (OsuWebRequest).
            http.DefaultRequestHeaders.UserAgent.ParseAdd("osu!");
        }

        [TearDown]
        public async Task TearDown()
        {
            http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dataPath, true);
        }

        /// <summary>
        /// What lazer's registration form sends (<c>RegistrationRequest</c>).
        /// </summary>
        private Task<HttpResponseMessage> register(string username, string email, string pass) => http.PostAsync("/users", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["user[username]"] = username,
            ["user[user_email]"] = email,
            ["user[password]"] = pass,
        }));

        /// <summary>
        /// What lazer's sign-in sends (<c>AccessTokenRequestPassword</c> / <c>AccessTokenRequestRefresh</c>).
        /// </summary>
        private Task<HttpResponseMessage> token(Dictionary<string, string> fields)
        {
            fields["client_id"] = "lazer-custom-server";
            fields["client_secret"] = "lazer-custom-server";
            fields["scope"] = "*";
            return http.PostAsync("/oauth/token", new FormUrlEncodedContent(fields));
        }

        private async Task<OAuthToken> signIn(string username = "Alpha", string pass = password)
        {
            var response = await token(new Dictionary<string, string> { ["grant_type"] = "password", ["username"] = username, ["password"] = pass });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            return JsonConvert.DeserializeObject<OAuthToken>(await response.Content.ReadAsStringAsync())!;
        }

        private async Task<T> get<T>(string path, OAuthToken signedIn)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);

            var response = await http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{path}: {body}");
            return JsonConvert.DeserializeObject<T>(body)!;
        }

        private async Task<PlayResult> sendPlay(byte[] map, OAuthToken? signedIn, string status = "Ranked", int beatmapId = 0, int keys = 4)
        {
            var response = await postPlay(map, signedIn?.AccessToken, status, beatmapId, keys);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            return JsonConvert.DeserializeObject<PlayResult>(await response.Content.ReadAsStringAsync())!;
        }

        private async Task<HttpResponseMessage> postPlay(byte[] map, string? accessToken, string status = "Ranked", int beatmapId = 0, int keys = 4)
        {
            var upload = TestPlays.Upload(map);
            upload.Mods.Clear();
            upload.Beatmap.Keys = keys;
            upload.Beatmap.Status = status;
            upload.Beatmap.OnlineId = beatmapId;

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/plays")
            {
                Content = new MultipartFormDataContent
                {
                    { new StringContent(JsonConvert.SerializeObject(upload), Encoding.UTF8, "application/json"), "play" },
                    { new ByteArrayContent(map), "beatmap", "map.osu" },
                },
            };

            request.Headers.Add(ScoreServerProtocol.CLIENT_HEADER, "maniaortho");

            if (accessToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            return await http.SendAsync(request);
        }

        [Test]
        public async Task TestRegisterThenSignIn()
        {
            var response = await register("Alpha", "alpha@example.com", password);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

            var signedIn = await signIn();

            Assert.That(signedIn.IsValid, Is.True);
            Assert.That(signedIn.RefreshToken, Is.Not.Empty);

            var me = await get<APIMe>("/api/v2/me/", signedIn);

            Assert.That(me.Username, Is.EqualTo("Alpha"));
            // Lazer reads user id 1 as nobody: no statistics, no avatar.
            Assert.That(me.Id, Is.GreaterThan(1));
            // No second step (e-mail or authenticator code): lazer goes straight online.
            Assert.That(me.SessionVerificationMethod, Is.Null);
            // Empty, so lazer shows its guest avatar instead of fetching one by id from osu!.
            Assert.That(me.AvatarUrl, Is.Empty);

            // The e-mail address signs in too, as on osu!.
            Assert.That((await signIn("alpha@example.com")).IsValid, Is.True);
        }

        [Test]
        public async Task TestRegistrationErrorsAreWhatTheFormShows()
        {
            var response = await register("A", "not an address", "short");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));

            // Exactly as APIAccess.CreateAccount reads them.
            var errors = JObject.Parse(await response.Content.ReadAsStringAsync()).SelectToken("form_error", true)!.ToObject<RegistrationRequest.RegistrationRequestErrors>()!;

            Assert.That(errors.User!.Username, Is.Not.Empty);
            Assert.That(errors.User.Email, Is.Not.Empty);
            Assert.That(errors.User.Password, Is.Not.Empty);

            await register("Alpha", string.Empty, password);
            var taken = JObject.Parse(await (await register("alpha", string.Empty, password)).Content.ReadAsStringAsync())
                               .SelectToken("form_error", true)!.ToObject<RegistrationRequest.RegistrationRequestErrors>()!;

            Assert.That(taken.User!.Username.Single(), Does.Contain("taken"));
        }

        [Test]
        public async Task TestWrongPasswordIsAMessageLazerShows()
        {
            await register("Alpha", string.Empty, password);

            var response = await token(new Dictionary<string, string> { ["grant_type"] = "password", ["username"] = "Alpha", ["password"] = "wrong password" });
            var error = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            // OAuth.cs shows "hint" (else "error") under the sign-in form.
            Assert.That((string?)error["hint"], Does.Contain("Wrong username or password"));
        }

        [Test]
        public async Task TestRefreshSwapsThePairAndEndsTheOldOne()
        {
            await register("Alpha", string.Empty, password);
            var first = await signIn();

            var refreshed = await token(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = first.RefreshToken });
            var second = JsonConvert.DeserializeObject<OAuthToken>(await refreshed.Content.ReadAsStringAsync())!;

            Assert.That(second.IsValid, Is.True);
            Assert.That(second.AccessToken, Is.Not.EqualTo(first.AccessToken));

            var again = await token(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = first.RefreshToken });
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/me/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            Assert.That((await http.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task TestSignedOutRequestsAreUnauthorised()
        {
            // Lazer signs out on a 401 from /me and asks to sign in again.
            Assert.That((await http.GetAsync("/api/v2/me/")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That((await http.GetAsync("/api/v2/friends")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task TestProfileHasTheServersPp()
        {
            await register("Alpha", string.Empty, password);
            var signedIn = await signIn();

            byte[] fourKey = TestPlays.Map(keys: 4);
            byte[] sevenKey = TestPlays.Map(keys: 7, notes: 500);

            await sendPlay(fourKey, signedIn, beatmapId: 101);
            var last = await sendPlay(sevenKey, signedIn, beatmapId: 102, keys: 7);

            var me = await get<APIMe>("/api/v2/me/mania", signedIn);
            var mania = await get<APIUser>($"/api/v2/users/{me.Id}/mania?key=id", signedIn);
            var osu = await get<APIUser>($"/api/v2/users/{me.Id}/osu?key=id", signedIn);

            Assert.That((double)mania.Statistics.PP!, Is.EqualTo(last.TotalPp).Within(1e-6));
            Assert.That(mania.Statistics.GlobalRank, Is.EqualTo(1));
            Assert.That(mania.Statistics.PlayCount, Is.EqualTo(2));
            Assert.That(mania.Statistics.Accuracy, Is.EqualTo(last.Accuracy).Within(1e-6));
            Assert.That(mania.Statistics.Variants.Select(v => v.VariantType.ToString()), Is.EquivalentTo(new[] { "FourKey", "SevenKey" }));
            Assert.That(mania.Statistics.Variants.All(v => v.PP > 0), Is.True);
            Assert.That(me.Statistics.PP, Is.EqualTo(mania.Statistics.PP));

            // maniaortho plays are osu!mania plays; the other rulesets have none.
            Assert.That((double)osu.Statistics.PP!, Is.Zero);
            Assert.That(osu.Statistics.GlobalRank, Is.Null);

            var best = await get<List<SoloScoreInfo>>($"/api/v2/users/{me.Id}/scores/best?mode=mania&limit=5", signedIn);

            Assert.That(best, Has.Count.EqualTo(2));
            Assert.That(best[0].PP, Is.GreaterThanOrEqualTo(best[1].PP!.Value));
            Assert.That(best.Select(s => s.Beatmap!.OnlineID), Is.EquivalentTo(new[] { 101, 102 }));
            Assert.That(best.All(s => s.RulesetID == 3 && s.Passed && s.Statistics.Count > 0), Is.True);
            Assert.That(best[0].Beatmap!.BeatmapSet!.Title, Does.StartWith("Synthetic"));

            var mostPlayed = await get<List<APIUserMostPlayedBeatmap>>($"/api/v2/users/{me.Id}/beatmapsets/most_played", signedIn);
            Assert.That(mostPlayed, Has.Count.EqualTo(2));

            var users = await get<GetUsersResponse>($"/api/v2/users?ids[]={me.Id}", signedIn);
            Assert.That(users.Users.Single().Username, Is.EqualTo("Alpha"));
        }

        [Test]
        public async Task TestPlaysGoToTheSignedInAccount()
        {
            byte[] map = TestPlays.Map();

            // Sent before any account: the first account takes it.
            await sendPlay(map, null, beatmapId: 1);

            await register("Alpha", string.Empty, password);
            await register("Second", string.Empty, password);

            var alpha = await signIn();
            var second = await signIn("Second");

            await sendPlay(TestPlays.Map(notes: 450), second, beatmapId: 2);
            // Without a sign-in: the first account's (the server's owner).
            await sendPlay(TestPlays.Map(notes: 500), null, beatmapId: 3);

            var alphaMe = await get<APIMe>("/api/v2/me/mania", alpha);
            var secondMe = await get<APIMe>("/api/v2/me/mania", second);

            Assert.That(alphaMe.Statistics.PlayCount, Is.EqualTo(2));
            Assert.That(secondMe.Statistics.PlayCount, Is.EqualTo(1));
            Assert.That(alphaMe.Statistics.GlobalRank, Is.Not.Null);
            Assert.That(secondMe.Statistics.GlobalRank, Is.Not.Null);
            Assert.That(new[] { alphaMe.Statistics.GlobalRank, secondMe.Statistics.GlobalRank }, Is.EquivalentTo(new int?[] { 1, 2 }));
        }

        /// <summary>
        /// A sign-in the server does not know (expired, signed out, or from before its data was reset) is refused rather than
        /// filed under the owner: the play belongs to whoever played it, and the game sends it again later.
        /// </summary>
        [Test]
        public async Task TestUnknownSignInIsNotFiledUnderTheOwner()
        {
            await register("Alpha", string.Empty, password);
            await register("Second", string.Empty, password);

            var alpha = await signIn();
            var second = await signIn("Second");

            using (var signOut = new HttpRequestMessage(HttpMethod.Delete, "/api/v2/oauth/tokens/current"))
            {
                signOut.Headers.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
                Assert.That((await http.SendAsync(signOut)).IsSuccessStatusCode, Is.True);
            }

            foreach (string stale in new[] { second.AccessToken, "not-a-token" })
            {
                var response = await postPlay(TestPlays.Map(), stale, beatmapId: 1);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), await response.Content.ReadAsStringAsync());
            }

            Assert.That((await get<APIMe>("/api/v2/me/mania", alpha)).Statistics.PlayCount, Is.Zero);
            Assert.That(app.Services.GetRequiredService<PlayService>().Store.All(), Is.Empty);
        }

        [Test]
        public async Task TestOsuSubmissionIsRefusedWithAReason()
        {
            await register("Alpha", string.Empty, password);
            var signedIn = await signIn();

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/beatmaps/123/solo/scores")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["beatmap_hash"] = "x", ["ruleset_id"] = "3" }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);

            var response = await http.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That((string?)JObject.Parse(await response.Content.ReadAsStringAsync())["error"], Is.EqualTo(LazerApi.SUBMISSION_REFUSED));
        }

        [Test]
        public async Task TestWebPagesCannotRegisterOrSignIn()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/users")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["user[username]"] = "Mallory", ["user[password]"] = password }),
            };
            request.Headers.Add("Origin", "https://example.com");

            Assert.That((await http.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "password", ["username"] = "x", ["password"] = "y" }),
            };
            tokenRequest.Headers.Add("Sec-Fetch-Site", "cross-site");

            Assert.That((await http.SendAsync(tokenRequest)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task TestNotificationSocketNeedsASignIn()
        {
            await register("Alpha", string.Empty, password);
            var signedIn = await signIn();

            var bundle = await get<APINotificationsBundle>("/api/v2/notifications", signedIn);
            Assert.That(bundle.Endpoint, Is.EqualTo($"ws://{http.BaseAddress!.Authority}/notifications"));

            using (var anonymous = new ClientWebSocket())
                Assert.ThrowsAsync<WebSocketException>(() => anonymous.ConnectAsync(new Uri(bundle.Endpoint), CancellationToken.None));

            using (var socket = new ClientWebSocket())
            {
                socket.Options.SetRequestHeader("Authorization", $"Bearer {signedIn.AccessToken}");
                await socket.ConnectAsync(new Uri(bundle.Endpoint), CancellationToken.None);

                Assert.That(socket.State, Is.EqualTo(WebSocketState.Open));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }

        [Test]
        public async Task TestUnknownRequestsAreErrorsLazerCanRead()
        {
            await register("Alpha", string.Empty, password);
            var signedIn = await signIn();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/rankings/mania/performance");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);

            var response = await http.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((string?)JObject.Parse(await response.Content.ReadAsStringAsync())["error"], Is.Not.Empty);
        }

        [TestCase(0, 1, 0)]
        [TestCase(15_000, 1, 50)]
        [TestCase(30_000, 2, 0)]
        [TestCase(30_000_000_000, 100, 3)]
        public void TestLevel(long totalScore, int level, int progress)
        {
            var (actualLevel, actualProgress) = LazerJson.Level(totalScore);

            Assert.That(actualLevel, Is.EqualTo(level));
            Assert.That(actualProgress, Is.EqualTo(progress));
        }
    }
}
