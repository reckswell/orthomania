// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Game.Online.API;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.ManiaOrtho.ScoreServer;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.ScoreServer
{
    /// <summary>
    /// Linking the game to the score server by signing in with osu! (<see cref="ScoreServerLinking"/>), against a stand-in
    /// server: what the game asks, that the address it opens is the server's own, and that the link's token goes with plays
    /// to that server only. The server's side of the sign-in is tested in orthomania (<c>OsuSignInTest</c>).
    /// </summary>
    [TestFixture]
    public class ScoreServerLinkingTest
    {
        private string folder = null!;
        private ScoreServerConfigManager config = null!;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "maniaortho-score-server-" + Guid.NewGuid().ToString("N"));
            config = new ScoreServerConfigManager(null, new ManiaOrthoRuleset().RulesetInfo);
        }

        [TearDown]
        public void TearDown()
        {
            config.Dispose();

            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        [Test]
        public async Task TestLinkThenPlaysCarryItsToken()
        {
            using var server = new FakeScoreServer();
            int polls = 0;

            server.Respond = request => request.RequestLine switch
            {
                var line when line.StartsWith("POST /api/link/poll ", StringComparison.Ordinal)
                    => Interlocked.Increment(ref polls) == 1 ? (200, "{\"status\":\"pending\"}") : (200, linked()),
                var line when line.StartsWith("POST /api/link ", StringComparison.Ordinal) => (200, start(server.Address)),
                var line when line.StartsWith("DELETE /api/link ", StringComparison.Ordinal) => (204, string.Empty),
                _ => (200, FakeScoreServer.ExampleResult),
            };

            var address = new Uri(server.Address);
            var link = await ScoreServerLinking.StartAsync(address, CancellationToken.None).ConfigureAwait(false);
            Assert.That(link.UserCode, Is.EqualTo("BKMT-QZRW"));

            var status = await ScoreServerLinking.WaitAsync(address, link, CancellationToken.None).ConfigureAwait(false);
            Assert.That(status.Status, Is.EqualTo(LinkStatus.LINKED));
            Assert.That(polls, Is.EqualTo(2));

            var requests = server.Requests;
            Assert.That(requests.Select(r => r.Headers[ScoreServerProtocol.CLIENT_HEADER.ToLowerInvariant()]), Has.All.EqualTo(ManiaOrthoRuleset.SHORT_NAME));
            Assert.That(requests.Any(r => r.Headers.ContainsKey("authorization")), Is.False, "the game sent a sign-in while linking");
            Assert.That(Encoding.UTF8.GetString(requests[1].Body), Is.EqualTo("device_code=for-the-game-only"));

            ScoreServerLinking.Save(config, address, status);

            // The link's token goes to that server only.
            Assert.That(ScoreServerLinking.TokenFor(config, address), Is.EqualTo("ortho_token"));
            Assert.That(ScoreServerLinking.TokenFor(config, new Uri($"http://localhost:{address.Port}")), Is.Null);
            Assert.That(ScoreServerLinking.TokenFor(config, new Uri($"http://127.0.0.1:{address.Port + 1}")), Is.Null);
            Assert.That(ScoreServerLinking.TokenFor(config, new Uri($"https://127.0.0.1:{address.Port}")), Is.Null);

            var client = new ScoreServerClient(new NativeStorage(folder), signIn: uri => ScoreServerLinking.TokenFor(config, uri));
            byte[] map = Encoding.UTF8.GetBytes("osu file format v14");
            var outcome = await client.SendAsync(server.Address, new PlayUpload { Client = ManiaOrthoRuleset.SHORT_NAME, ScoreId = "a" }, map, null).ConfigureAwait(false);

            Assert.That(outcome.Kind, Is.EqualTo(ScoreServerOutcomeKind.Sent), outcome.Message);
            Assert.That(server.Requests.Last().Headers["authorization"], Is.EqualTo("Bearer ortho_token"));

            // Unlinking tells the server and forgets the token.
            await ScoreServerLinking.UnlinkAsync(ScoreServerLinking.Current(config)!).ConfigureAwait(false);
            ScoreServerLinking.Clear(config);

            Assert.That(server.Requests.Last().RequestLine, Does.StartWith("DELETE /api/link "));
            Assert.That(server.Requests.Last().Headers["authorization"], Is.EqualTo("Bearer ortho_token"));
            Assert.That(ScoreServerLinking.TokenFor(config, address), Is.Null);
        }

        [Test]
        public void TestNote()
        {
            var address = new Uri("http://127.0.0.1:7727");

            Assert.That(ScoreServerSignIn.Describe(config, null, "http://127.0.0.1:7727")?.Text.ToString(), Does.StartWith("Not linked"));
            Assert.That(ScoreServerSignIn.Describe(config, null, "not an address"), Is.Null);

            // Signed in to osu! in the game, not linked: still not linked (osu!'s sign-in is never sent to the score server).
            var osuSignIn = new DummyAPIAccess { SessionVerificationMethod = null };
            osuSignIn.Login("osu player", "password");
            Assert.That(ScoreServerSignIn.Describe(config, osuSignIn, "http://127.0.0.1:7727")?.Text.ToString(), Does.StartWith("Not linked"));

            ScoreServerLinking.Save(config, address, JsonConvert.DeserializeObject<LinkStatus>(linked())!);

            var mine = ScoreServerSignIn.Describe(config, null, "http://127.0.0.1:7727/");
            Assert.That(mine?.Type, Is.EqualTo(SettingsNote.Type.Informational));
            Assert.That(mine?.Text.ToString(), Is.EqualTo("Plays go to osu player's account (linked with osu!)."));

            var elsewhere = ScoreServerSignIn.Describe(config, null, "http://localhost:7727");
            Assert.That(elsewhere?.Type, Is.EqualTo(SettingsNote.Type.Warning));
            Assert.That(elsewhere?.Text.ToString(), Does.StartWith("The game is linked to 127.0.0.1:7727, not to this address"));

            // Signed in to osu! in the game as someone else.
            var api = new DummyAPIAccess { SessionVerificationMethod = null };
            api.Login("someone else", "password");

            var other = ScoreServerSignIn.Describe(config, api, "http://127.0.0.1:7727");
            Assert.That(other?.Type, Is.EqualTo(SettingsNote.Type.Warning));
            Assert.That(other?.Text.ToString(), Does.Contain("signed in to osu! as"));
        }

        /// <summary>
        /// The address the game opens in the browser must be the score server's own; the server sends the browser on to osu!.
        /// </summary>
        [TestCase("https://osu.ppy.sh/oauth/authorize?client_id=1")]
        [TestCase("http://127.0.0.1:1/auth/osu/start?link=BKMT-QZRW")]
        [TestCase("not an address")]
        public void TestAddressElsewhereIsRefused(string url)
        {
            using var server = new FakeScoreServer();
            server.Respond = _ => (200, JsonConvert.SerializeObject(new LinkStart { DeviceCode = "x", UserCode = "BKMT-QZRW", Url = url, ExpiresIn = 60, Interval = 1 }));

            Assert.ThrowsAsync<ScoreServerLinkException>(() => ScoreServerLinking.StartAsync(new Uri(server.Address), CancellationToken.None));
        }

        [Test]
        public void TestNeverLinksWithOsu()
        {
            using var http = new HttpClient(new RecordingHandler());

            Assert.ThrowsAsync<ScoreServerLinkException>(() => ScoreServerLinking.StartAsync(new Uri("https://osu.ppy.sh"), CancellationToken.None, http));
            Assert.ThrowsAsync<ScoreServerLinkException>(() => ScoreServerLinking.StartAsync(new Uri("https://lazer.ppy.sh."), CancellationToken.None, http));
            Assert.That(RecordingHandler.Sent, Is.Zero);
        }

        [Test]
        public void TestServerErrorIsShown()
        {
            using var server = new FakeScoreServer();
            server.Respond = _ => (503, "{\"error\":\"Sign-in with osu! is not set up on this server yet.\"}");

            var e = Assert.ThrowsAsync<ScoreServerLinkException>(() => ScoreServerLinking.StartAsync(new Uri(server.Address), CancellationToken.None));
            Assert.That(e?.Message, Is.EqualTo("Sign-in with osu! is not set up on this server yet."));
        }

        [Test]
        public void TestRedirectIsNotFollowed()
        {
            using var server = new FakeScoreServer();
            server.Location = "http://127.0.0.1:1/elsewhere";
            server.Respond = _ => (307, string.Empty);

            Assert.ThrowsAsync<ScoreServerLinkException>(() => ScoreServerLinking.StartAsync(new Uri(server.Address), CancellationToken.None));
            Assert.That(server.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TestDenied()
        {
            using var server = new FakeScoreServer();
            server.Respond = _ => (200, "{\"status\":\"denied\"}");

            var link = new LinkStart { DeviceCode = "x", UserCode = "BKMT-QZRW", Url = server.Address, ExpiresIn = 60, Interval = 1 };
            var status = await ScoreServerLinking.WaitAsync(new Uri(server.Address), link, CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.Status, Is.EqualTo(LinkStatus.DENIED));
            Assert.That(status.Token, Is.Null);
        }

        private static string start(string address) => JsonConvert.SerializeObject(new LinkStart
        {
            DeviceCode = "for-the-game-only",
            UserCode = "BKMT-QZRW",
            Url = $"{address}/auth/osu/start?link=BKMT-QZRW",
            ExpiresIn = 60,
            Interval = 1,
        });

        private static string linked() => JsonConvert.SerializeObject(new LinkStatus
        {
            Status = LinkStatus.LINKED,
            Token = "ortho_token",
            Username = "osu player",
            OsuUserId = 12345678,
        });

        private class RecordingHandler : HttpMessageHandler
        {
            public static int Sent;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Sent);
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }
    }
}
