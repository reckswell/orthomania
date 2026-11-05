using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ManiaOrtho.ScoreServerContract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OrthoServer.Tests
{
    /// <summary>
    /// The real server on a free port with an empty data folder.
    /// </summary>
    [TestFixture]
    public class ServerApiTest
    {
        private string dataPath = null!;
        private WebApplication app = null!;
        private HttpClient http = null!;

        [SetUp]
        public async Task SetUp()
        {
            dataPath = Path.Combine(Path.GetTempPath(), "ortho-server-test-" + Guid.NewGuid().ToString("N"));
            app = ServerHost.Create(new ServerOptions { DataPath = dataPath, Port = 0, CheckLiveVersion = false });
            await app.StartAsync();

            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            http = new HttpClient { BaseAddress = new Uri(address) };
            http.DefaultRequestHeaders.Add(ScoreServerProtocol.CLIENT_HEADER, "1");
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

        private Task<HttpResponseMessage> post(PlayUpload upload, byte[] map, byte[]? replay = null)
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(JsonConvert.SerializeObject(upload), Encoding.UTF8, "application/json"), "play" },
                { new ByteArrayContent(map), "beatmap", "map.osu" },
            };

            if (replay != null)
                form.Add(new ByteArrayContent(replay), "replay", "play.osr");

            return http.PostAsync("/api/plays", form);
        }

        [Test]
        public async Task TestUploadIsRatedAndCounts()
        {
            byte[] map = TestPlays.Map();
            var response = await post(TestPlays.Upload(map), map, new byte[] { 1, 2, 3 });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

            var result = JsonConvert.DeserializeObject<PlayResult>(await response.Content.ReadAsStringAsync())!;

            Assert.That(result.AwardsPp, Is.True, result.Reason);
            Assert.That(result.Pp, Is.GreaterThan(0));
            Assert.That(result.TopPosition, Is.EqualTo(1));
            Assert.That(result.TotalPpBefore, Is.Zero);
            // One play: its pp plus the bonus for one score.
            Assert.That(result.TotalPp, Is.EqualTo(result.Pp!.Value + (417.0 - 1.0 / 3.0) * 0.005).Within(1e-6));

            var profile = JObject.Parse(await http.GetStringAsync("/api/profile"));
            Assert.That((double)profile["total_pp"]!, Is.EqualTo(result.TotalPp).Within(1e-9));
            Assert.That(profile["top"]!.Count(), Is.EqualTo(1));

            Assert.That(File.Exists(Path.Combine(dataPath, "beatmaps", TestPlays.Upload(map).Beatmap.Md5 + ".osu")), Is.True);
            Assert.That(File.ReadAllBytes(Path.Combine(dataPath, "replays", result.PlayId + ".osr")), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public async Task TestTheSamePlaySentTwiceIsStoredOnce()
        {
            byte[] map = TestPlays.Map();
            var upload = TestPlays.Upload(map, scoreId: "same-play");

            var first = JsonConvert.DeserializeObject<PlayResult>(await (await post(upload, map)).Content.ReadAsStringAsync())!;
            var second = JsonConvert.DeserializeObject<PlayResult>(await (await post(upload, map)).Content.ReadAsStringAsync())!;

            Assert.That(second.PlayId, Is.EqualTo(first.PlayId));
            Assert.That(second.AlreadyStored, Is.True);
            Assert.That(JArray.Parse(await http.GetStringAsync("/api/plays")), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TestMapThatDoesNotMatchThePlayIsRefused()
        {
            byte[] map = TestPlays.Map();
            var upload = TestPlays.Upload(TestPlays.Map(notes: 401));

            var response = await post(upload, map);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("checksum"));
        }

        [Test]
        public async Task TestPlaysFromOtherRulesetsAreRefused()
        {
            byte[] map = TestPlays.Map();
            var upload = TestPlays.Upload(map);
            upload.Client = "mania";

            Assert.That((await post(upload, map)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task TestPageShowsTheProfile()
        {
            byte[] map = TestPlays.Map();
            await post(TestPlays.Upload(map), map);

            string page = await http.GetStringAsync("/");

            Assert.That(page, Does.Contain("maniaortho scores"));
            Assert.That(page, Does.Contain("Synthetic 4K"));
            Assert.That(page, Does.Contain("Not connected to osu!"));
        }

        [Test]
        public async Task TestPostsWithoutTheGamesHeaderAreRefused()
        {
            byte[] map = TestPlays.Map();
            http.DefaultRequestHeaders.Remove(ScoreServerProtocol.CLIENT_HEADER);

            Assert.That((await post(TestPlays.Upload(map), map)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That((await http.PostAsync("/api/recalculate", null)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(JArray.Parse(await http.GetStringAsync("/api/plays")), Is.Empty);
        }

        [Test]
        public async Task TestListensOnThisComputerOnly()
        {
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Assert.That(new Uri(address).Host, Is.EqualTo("127.0.0.1"));
            Assert.That((await http.GetAsync("/api/status")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
