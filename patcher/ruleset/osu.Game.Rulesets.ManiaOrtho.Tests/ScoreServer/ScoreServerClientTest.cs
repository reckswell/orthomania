// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Game.Online.API;
using osu.Game.Rulesets.ManiaOrtho.ScoreServer;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.ScoreServer
{
    /// <summary>
    /// Sending plays to the score server: never to osu!, queued while the server is away, and sent in order once it is back.
    /// Over real sockets to <see cref="FakeScoreServer"/>.
    /// </summary>
    [TestFixture]
    public partial class ScoreServerClientTest
    {
        private string folder = null!;
        private ScoreServerClient client = null!;

        private static readonly byte[] map = { 1, 2, 3, 4 };
        private static readonly byte[] replay = { 9, 8, 7 };

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "maniaortho-score-server-" + Guid.NewGuid().ToString("N"));
            client = new ScoreServerClient(new NativeStorage(folder));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        private static PlayUpload play(string scoreId, int minute = 0) => new PlayUpload
        {
            Client = ManiaOrthoRuleset.SHORT_NAME,
            ScoreId = scoreId,
            PlayedAt = new DateTimeOffset(2026, 11, 4, 20, minute, 0, TimeSpan.Zero),
            Passed = true,
            Beatmap = new BeatmapUpload { Md5 = "0123456789abcdef0123456789abcdef", Status = "Ranked", Keys = 4 },
            Mods = new List<APIMod> { new APIMod { Acronym = "DT" } },
            Statistics = new Dictionary<string, int> { ["Perfect"] = 10 },
        };

        [TestCase("https://osu.ppy.sh")]
        [TestCase("https://osu.ppy.sh/api/plays")]
        [TestCase("http://dev.ppy.sh/")]
        [TestCase("https://PPY.SH")]
        [TestCase("https://spectator.ppy.sh.")]
        public async Task TestOsuServersAreNeverContacted(string address)
        {
            var handler = new CountingHandler();
            var refusing = new ScoreServerClient(new NativeStorage(folder), new HttpClient(handler));

            var outcome = await refusing.SendAsync(address, play("a"), map, replay).ConfigureAwait(false);
            int flushed = await refusing.FlushAsync(address).ConfigureAwait(false);

            Assert.That(handler.Requests, Is.Zero);
            Assert.That(flushed, Is.Zero);
            Assert.That(outcome.Kind, Is.EqualTo(ScoreServerOutcomeKind.Queued));
            Assert.That(outcome.Message, Does.Contain("osu!'s own server"));
            Assert.That(refusing.QueuedCount, Is.EqualTo(1), "kept for when the address points at the player's own server");
        }

        /// <summary>
        /// A redirect could send the play on to any host, osu!'s included: it is reported, never followed.
        /// </summary>
        [TestCase(301)]
        [TestCase(302)]
        [TestCase(307)]
        [TestCase(308)]
        public async Task TestRedirectsAreNotFollowed(int status)
        {
            using var elsewhere = new FakeScoreServer();
            using var server = new FakeScoreServer();
            server.Respond = _ => (status, string.Empty);
            server.Location = elsewhere.Address + "/api/plays";

            var outcome = await client.SendAsync(server.Address, play("a"), map, replay).ConfigureAwait(false);

            Assert.That(server.Requests, Has.Count.EqualTo(1));
            Assert.That(elsewhere.Requests, Is.Empty);
            Assert.That(outcome.Kind, Is.EqualTo(ScoreServerOutcomeKind.Queued));
            Assert.That(outcome.Message, Does.Contain("redirects to " + elsewhere.Address));
            Assert.That(client.QueuedCount, Is.EqualTo(1));
        }

        [TestCase(404, "{}")]
        [TestCase(405, "")]
        [TestCase(200, "<html>a router's page</html>")]
        public async Task TestWrongAddressKeepsThePlays(int status, string body)
        {
            using var server = new FakeScoreServer();
            server.Respond = _ => (status, body);

            await client.SendAsync(FakeScoreServer.UnusedAddress(), play("first", 1), map, replay).ConfigureAwait(false);
            var outcome = await client.SendAsync(server.Address, play("second", 2), map, replay).ConfigureAwait(false);

            Assert.That(outcome.Kind, Is.EqualTo(ScoreServerOutcomeKind.Queued));
            Assert.That(client.QueuedCount, Is.EqualTo(2), "neither play is set aside");
            Assert.That(Directory.Exists(Path.Combine(folder, ScoreServerClient.REFUSED_DIRECTORY)), Is.False);
        }

        [Test]
        public async Task TestEveryRequestCarriesTheGamesHeader()
        {
            using var server = new FakeScoreServer();

            await client.SendAsync(server.Address, play("a"), map, replay).ConfigureAwait(false);

            Assert.That(server.Requests.Single().Headers.ContainsKey(ScoreServerProtocol.CLIENT_HEADER.ToLowerInvariant()), Is.True);
            Assert.That(server.Requests.Single().Headers.ContainsKey("authorization"), Is.False, "not linked: no token is sent");
        }

        /// <summary>
        /// Linked to the score server: plays carry the link's token and go to that account, the queued ones included. The token
        /// is asked for at each send, for the server it is sent to.
        /// </summary>
        [Test]
        public async Task TestPlaysCarryTheLinkToThatServer()
        {
            using var server = new FakeScoreServer();
            var asked = new List<Uri>();
            var signedIn = new ScoreServerClient(new NativeStorage(folder), signIn: uri =>
            {
                asked.Add(uri);
                return "token-for-this-server";
            });

            server.Respond = _ => (503, "{}");
            await signedIn.SendAsync(server.Address, play("a"), map, replay).ConfigureAwait(false);

            server.Respond = _ => (200, FakeScoreServer.ExampleResult);
            await signedIn.SendAsync(server.Address, play("b", 1), map, replay).ConfigureAwait(false);

            Assert.That(server.Requests.Select(r => r.Headers["authorization"]), Has.All.EqualTo("Bearer token-for-this-server"));
            Assert.That(server.Requests, Has.Count.EqualTo(3));
            Assert.That(asked.Select(u => u.Authority).Distinct(), Is.EquivalentTo(new[] { new Uri(server.Address).Authority }));
        }

        [TestCase("http://127.0.0.1:7727", "http://127.0.0.1:7727/", true)]
        [TestCase("http://127.0.0.1:7727", "http://127.0.0.1:7728/", false)]
        [TestCase("http://127.0.0.1:7727", "https://127.0.0.1:7727/", false)]
        [TestCase("http://127.0.0.1:7727", "http://localhost:7727/", false)]
        [TestCase("http://example.com", "http://EXAMPLE.com/scores/", true)]
        [TestCase("https://osu.ppy.sh", "https://osu.ppy.sh/", false)]
        public void TestLinkGoesOnlyToTheSameServer(string signedInTo, string scoreServer, bool same)
            => Assert.That(ScoreServerSignIn.SameServer(new Uri(signedInTo), new Uri(scoreServer)), Is.EqualTo(same));

        /// <summary>
        /// A link the server no longer accepts (unlinked on the server, unused too long, or its data was reset) keeps the play
        /// waiting for the next send rather than setting it aside: it is still that account's play.
        /// </summary>
        [Test]
        public async Task TestRefusedLinkKeepsThePlayWaiting()
        {
            using var server = new FakeScoreServer();
            server.Respond = _ => (401, "{\"error\":\"unlinked\"}");
            var signedIn = new ScoreServerClient(new NativeStorage(folder), signIn: _ => "stale-token");

            var waiting = await signedIn.SendAsync(server.Address, play("a"), map, replay).ConfigureAwait(false);

            Assert.That(waiting.Kind, Is.EqualTo(ScoreServerOutcomeKind.Queued));
            Assert.That(waiting.SetAside, Is.False);
            Assert.That(waiting.Message, Is.EqualTo("the score server did not accept the game's link: unlinked"));
            Assert.That(signedIn.QueuedCount, Is.EqualTo(1));

            server.Respond = _ => (200, FakeScoreServer.ExampleResult);
            var sent = await signedIn.SendAsync(server.Address, play("b", 1), map, replay).ConfigureAwait(false);

            Assert.That(sent.Kind, Is.EqualTo(ScoreServerOutcomeKind.Sent));
            Assert.That(sent.SentFromOutbox, Is.EqualTo(1));
            Assert.That(signedIn.QueuedCount, Is.Zero);
        }

        /// <summary>
        /// Written before it is sent: a game closed mid-send (or a crash) loses nothing.
        /// </summary>
        [Test]
        public async Task TestPlayIsInTheOutboxWhileItIsSent()
        {
            int queuedDuringRequest = -1;
            using var server = new FakeScoreServer();
            server.Respond = _ =>
            {
                queuedDuringRequest = client.QueuedCount;
                return (200, FakeScoreServer.ExampleResult);
            };

            var outcome = await client.SendAsync(server.Address, play("a"), map, replay).ConfigureAwait(false);

            Assert.That(queuedDuringRequest, Is.EqualTo(1));
            Assert.That(outcome.Kind, Is.EqualTo(ScoreServerOutcomeKind.Sent));
            Assert.That(client.QueuedCount, Is.Zero);
        }

        [TestCase("http://127.0.0.1:7727", true)]
        [TestCase("http://localhost:7727/", true)]
        [TestCase("https://scores.example.net/ortho/", true)]
        [TestCase("ppy.sh.example.net", false)] // no scheme
        [TestCase("http://notppy.sh", true)]
        [TestCase("ftp://127.0.0.1", false)]
        [TestCase("", false)]
        public void TestAddresses(string address, bool valid)
            => Assert.That(ScoreServerClient.ParseAddress(address, out _), valid ? Is.Not.Null : Is.Null);

        [Test]
        public async Task TestPlayIsSentAndRated()
        {
            using var server = new FakeScoreServer();

            var outcome = await client.SendAsync(server.Address, play("first"), map, replay).ConfigureAwait(false);

            Assert.That(outcome.Kind, Is.EqualTo(ScoreServerOutcomeKind.Sent), outcome.Message);
            Assert.That(outcome.Result!.Pp, Is.EqualTo(123.45));
            Assert.That(outcome.Result.TotalPp, Is.EqualTo(1234.56));

            var request = server.Requests.Single();

            Assert.That(request.RequestLine, Does.StartWith("POST /api/plays "));
            Assert.That(JsonConvert.DeserializeObject<PlayUpload>(request.PartText("play"))!.ScoreId, Is.EqualTo("first"));
            Assert.That(request.Parts["beatmap"], Is.EqualTo(map));
            Assert.That(request.Parts["replay"], Is.EqualTo(replay));
        }

        [Test]
        public async Task TestServerAtASubPath()
        {
            using var server = new FakeScoreServer();

            await client.SendAsync(server.Address + "/ortho", play("a"), map, null).ConfigureAwait(false);

            Assert.That(server.Requests.Single().RequestLine, Does.StartWith("POST /ortho/api/plays "));
            Assert.That(server.Requests.Single().Parts.ContainsKey("replay"), Is.False);
        }

        [Test]
        public async Task TestPlaysWaitForTheServerAndGoInOrder()
        {
            string away = FakeScoreServer.UnusedAddress();

            var first = await client.SendAsync(away, play("first", 1), map, replay).ConfigureAwait(false);
            var second = await client.SendAsync(away, play("second", 2), map, null).ConfigureAwait(false);

            Assert.That(first.Kind, Is.EqualTo(ScoreServerOutcomeKind.Queued));
            Assert.That(first.Message, Does.Contain("not reachable"));
            Assert.That(second.Kind, Is.EqualTo(ScoreServerOutcomeKind.Queued));
            Assert.That(second.Queued, Is.EqualTo(2));

            string queued = Directory.GetDirectories(Path.Combine(folder, ScoreServerClient.OUTBOX_DIRECTORY)).Order(StringComparer.Ordinal).First();
            Assert.That(Directory.GetFiles(queued).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { ScoreServerClient.PLAY_FILE, ScoreServerClient.BEATMAP_FILE, ScoreServerClient.REPLAY_FILE }));

            using var server = new FakeScoreServer();

            var third = await client.SendAsync(server.Address, play("third", 3), map, replay).ConfigureAwait(false);

            Assert.That(third.Kind, Is.EqualTo(ScoreServerOutcomeKind.Sent));
            Assert.That(third.SentFromOutbox, Is.EqualTo(2));
            Assert.That(third.Queued, Is.Zero);
            Assert.That(server.Requests.Select(r => JsonConvert.DeserializeObject<PlayUpload>(r.PartText("play"))!.ScoreId),
                Is.EqualTo(new[] { "first", "second", "third" }));
            Assert.That(server.Requests[0].Parts["replay"], Is.EqualTo(replay));
            Assert.That(server.Requests[1].Parts.ContainsKey("replay"), Is.False);
        }

        [Test]
        public async Task TestServerErrorQueuesButRefusalSetsAside()
        {
            using var server = new FakeScoreServer();
            server.Respond = _ => (500, "{\"error\":\"disk full\"}");

            var failed = await client.SendAsync(server.Address, play("a", 1), map, replay).ConfigureAwait(false);

            Assert.That(failed.Kind, Is.EqualTo(ScoreServerOutcomeKind.Queued));
            Assert.That(failed.Message, Is.EqualTo("the score server failed (500: disk full)"));
            Assert.That(client.QueuedCount, Is.EqualTo(1));

            // The queued play is refused when it is tried again; the new one too. Both are kept with the reason.
            server.Respond = _ => (400, "{\"error\":\"Beatmap checksum mismatch.\"}");

            var refused = await client.SendAsync(server.Address, play("b", 2), map, replay).ConfigureAwait(false);

            Assert.That(refused.Kind, Is.EqualTo(ScoreServerOutcomeKind.Refused));
            Assert.That(refused.SetAside, Is.True);
            Assert.That(refused.Message, Is.EqualTo("Beatmap checksum mismatch."));
            Assert.That(client.QueuedCount, Is.Zero);

            string[] kept = Directory.GetDirectories(Path.Combine(folder, ScoreServerClient.REFUSED_DIRECTORY));
            Assert.That(kept, Has.Length.EqualTo(2));
            Assert.That(kept.Select(k => File.ReadAllText(Path.Combine(k, ScoreServerClient.ERROR_FILE))), Has.All.EqualTo("Beatmap checksum mismatch."));
            Assert.That(kept.Select(k => File.Exists(Path.Combine(k, ScoreServerClient.PLAY_FILE))), Has.All.True);
        }

        /// <summary>
        /// A play the server keeps failing on must not hold up every play after it.
        /// </summary>
        [Test]
        public async Task TestPlayTheServerKeepsFailingOnIsSetAside()
        {
            using var server = new FakeScoreServer();
            server.Respond = _ => (500, "{}");

            await client.SendAsync(server.Address, play("stuck", 1), map, replay).ConfigureAwait(false);

            for (int i = 1; i < ScoreServerClient.MAX_SERVER_ERRORS - 1; i++)
                await client.FlushAsync(server.Address).ConfigureAwait(false);

            Assert.That(client.QueuedCount, Is.EqualTo(1));

            server.Respond = r => JsonConvert.DeserializeObject<PlayUpload>(r.PartText("play"))!.ScoreId == "stuck" ? (500, "{}") : (200, FakeScoreServer.ExampleResult);

            var next = await client.SendAsync(server.Address, play("next", 2), map, replay).ConfigureAwait(false);

            Assert.That(next.Kind, Is.EqualTo(ScoreServerOutcomeKind.Sent));
            Assert.That(client.QueuedCount, Is.Zero);

            string kept = Directory.GetDirectories(Path.Combine(folder, ScoreServerClient.REFUSED_DIRECTORY)).Single();
            Assert.That(await File.ReadAllTextAsync(Path.Combine(kept, ScoreServerClient.ERROR_FILE)).ConfigureAwait(false), Does.Contain($"({ScoreServerClient.MAX_SERVER_ERRORS} times)"));
        }

        [Test]
        public async Task TestQueuedPlaysCanBeSentAlone()
        {
            await client.SendAsync(FakeScoreServer.UnusedAddress(), play("a"), map, replay).ConfigureAwait(false);

            using var server = new FakeScoreServer();

            Assert.That(await client.FlushAsync(server.Address).ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(client.QueuedCount, Is.Zero);
        }

        private class CountingHandler : HttpMessageHandler
        {
            public int Requests;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Requests);
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }
    }
}
