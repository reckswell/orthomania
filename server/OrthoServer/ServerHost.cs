using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ManiaOrtho.ScoreServerContract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using OrthoServer.Accounts;
using OrthoServer.Live;
using OrthoServer.Scoring;
using OrthoServer.Storage;
using OrthoServer.Web;

namespace OrthoServer
{
    public class ServerOptions
    {
        /// <summary>
        /// Where plays, maps and replays are kept.
        /// </summary>
        public string DataPath { get; set; } = DefaultDataPath;

        /// <summary>
        /// The port on 127.0.0.1 (this computer only). 0 picks a free one.
        /// </summary>
        public int Port { get; set; } = DEFAULT_PORT;

        /// <summary>
        /// Whether to check which calculator version osu! runs live (on GitHub, at start and twice a day).
        /// </summary>
        public bool CheckLiveVersion { get; set; } = true;

        /// <summary>
        /// A process this server stops with, once it has ended (the patcher's dev option passes its own), or null to run until
        /// stopped.
        /// </summary>
        public int? ParentProcessId { get; set; }

        /// <summary>
        /// osu!, for signing in with it. Only tests point it elsewhere (a stand-in on this computer).
        /// </summary>
        internal Uri OsuSite { get; set; } = OsuSignIn.OSU;

        public const int DEFAULT_PORT = 7727;

        /// <summary>
        /// <c>%LOCALAPPDATA%\orthomania\server</c> (<c>~/.local/share/orthomania/server</c> on Linux), the folder the patcher's dev
        /// option starts the server with.
        /// </summary>
        public static string DefaultDataPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "orthomania", "server");
    }

    /// <summary>
    /// The web server: the profile page and the JSON endpoints the game uploads plays to. It listens on 127.0.0.1 only.
    /// </summary>
    public static class ServerHost
    {
        /// <summary>
        /// This server's version (the repository's <c>Version</c>).
        /// </summary>
        public static readonly string Version =
            typeof(ServerHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

        public static WebApplication Create(ServerOptions options)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });

            builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            var service = new PlayService(new PlayStore(options.DataPath), new AccountStore(options.DataPath));
            var live = new LiveCheck(options.CheckLiveVersion);
            var osu = new OsuSignIn(options.DataPath, options.OsuSite);
            var links = new SignInLinks();

            builder.Services.AddSingleton(service);
            builder.Services.AddSingleton(live);
            builder.Services.AddSingleton(links);

            var app = builder.Build();

            app.Lifetime.ApplicationStopped.Register(osu.Dispose);

            app.Lifetime.ApplicationStarted.Register(() =>
            {
                live.Start(app.Lifetime.ApplicationStopping);

                if (options.ParentProcessId is int parent)
                    ParentProcessWatch.Start(parent, app.Lifetime);
            });

            // Only the game changes anything. A web page's request carries Origin or Sec-Fetch-Site (the game's never do), and a
            // page cannot add the X-ManiaOrtho header the game's requests carry without a preflight, which nothing here allows.
            app.Use(async (context, next) =>
            {
                var request = context.Request;
                bool changes = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
                bool fromPage = request.Headers.ContainsKey("Origin") || request.Headers.ContainsKey("Sec-Fetch-Site");

                if (changes && (fromPage || !request.Headers.ContainsKey(ScoreServerProtocol.CLIENT_HEADER)))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { error = fromPage
                                     ? "Web pages cannot change anything on this server."
                                     : $"Requests that change anything need the {ScoreServerProtocol.CLIENT_HEADER} header." })
                                 .ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });

            app.MapGet("/", () =>
            {
                var owner = service.Accounts.Owner();
                var recent = service.Store.ForUser(owner?.Id).OrderByDescending(p => p.Id).Take(50).ToList();
                string page = ProfilePage.Render(service.Profile(owner?.Id), recent, PlayCalculator.CalculatorVersion, live.NewerVersion, owner?.Username, osu.IsSetUp);
                return Results.Content(page, "text/html; charset=utf-8");
            });

            // For whoever starts the server (the patcher's dev option): answers as soon as the server listens, and reads nothing.
            app.MapGet("/api/health", () => json(new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["version"] = Version,
                ["calculator_version"] = PlayCalculator.CalculatorVersion,
            }));

            app.MapGet("/api/status", () => json(new Dictionary<string, object?>
            {
                ["calculator_version"] = PlayCalculator.CalculatorVersion,
                ["live_calculator_version"] = live.Latest?.CalculatorVersion,
                ["live_release_tag"] = live.Latest?.ReleaseTag,
                ["live_checked_at"] = live.CheckedAt,
                ["plays"] = service.Store.All().Count,
                ["osu_sign_in"] = osu.IsSetUp,
            }));

            app.MapGet("/api/profile", () =>
            {
                var profile = service.Profile(service.Accounts.Owner()?.Id);

                return json(new Dictionary<string, object?>
                {
                    ["total_pp"] = profile.TotalPp,
                    ["bonus_pp"] = profile.BonusPp,
                    ["accuracy"] = profile.Accuracy,
                    ["counted_scores"] = profile.CountedScores,
                    ["calculator_version"] = PlayCalculator.CalculatorVersion,
                    ["top"] = profile.Top.Select(t => new Dictionary<string, object?>
                    {
                        ["play_id"] = t.Play.Id,
                        ["beatmap_md5"] = t.Play.BeatmapMd5,
                        ["beatmap_id"] = t.Play.BeatmapId,
                        ["artist"] = t.Play.Artist,
                        ["title"] = t.Play.Title,
                        ["version"] = t.Play.Version,
                        ["mods"] = t.Play.Mods,
                        ["accuracy"] = t.Play.Accuracy,
                        ["pp"] = t.Play.Pp,
                        ["weight"] = t.Weight,
                        ["played_at"] = t.Play.PlayedAt,
                    }),
                });
            });

            app.MapGet("/api/plays", (int? limit) => json(service.Store.All().OrderByDescending(p => p.Id).Take(Math.Clamp(limit ?? 50, 1, 1000))));

            app.MapPost("/api/plays", async (HttpRequest request) =>
            {
                string? token = BearerToken.Of(request);
                var signedIn = service.Accounts.FindByGameToken(token);

                // A link this server does not know (unlinked, unused for too long, or from before its data was reset) must not file
                // the play under another account: the game keeps it and sends it again once it is linked again.
                if (signedIn == null && token != null)
                {
                    return Results.Json(new { error = "The game's link is not valid on this server (unlinked or expired); the play is sent again later." },
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                if (!request.HasFormContentType)
                    return Results.BadRequest(new { error = "Send the play as multipart/form-data: play (JSON), beatmap (.osu) and optionally replay (.osr)." });

                var form = await request.ReadFormAsync().ConfigureAwait(false);

                string? playJson = form["play"];
                var beatmapFile = form.Files["beatmap"];

                if (string.IsNullOrEmpty(playJson) || beatmapFile == null)
                    return Results.BadRequest(new { error = "An upload needs a \"play\" field and a \"beatmap\" file." });

                try
                {
                    var upload = JsonConvert.DeserializeObject<PlayUpload>(playJson) ?? throw new InvalidDataException("The play is empty.");
                    byte[] osuFile = await readAll(beatmapFile).ConfigureAwait(false);
                    byte[]? replay = form.Files["replay"] is IFormFile replayFile ? await readAll(replayFile).ConfigureAwait(false) : null;

                    var result = service.Submit(upload, osuFile, replay, signedIn);
                    result.LiveCalculatorVersion = live.NewerVersion;
                    return json(result);
                }
                catch (Exception e) when (e is InvalidDataException or JsonException or FormatException)
                {
                    return Results.BadRequest(new { error = e.Message });
                }
            });

            app.MapPost("/api/recalculate", () =>
            {
                var (rated, failed) = service.Recalculate(onlyOutdated: false);
                return json(new Dictionary<string, object?> { ["rated"] = rated, ["failed"] = failed, ["calculator_version"] = PlayCalculator.CalculatorVersion });
            });

            OsuSignInRoutes.Map(app, service, osu, links);

            return app;
        }

        private static readonly JsonSerializerSettings json_settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        };

        private static IResult json(object value) => Results.Content(JsonConvert.SerializeObject(value, json_settings), "application/json; charset=utf-8");

        private static async Task<byte[]> readAll(IFormFile file)
        {
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream).ConfigureAwait(false);
                return stream.ToArray();
            }
        }
    }

    /// <summary>
    /// Keeps the latest answer of <see cref="LiveCalculatorVersion"/>. A failed check (no network) keeps the previous answer.
    /// </summary>
    public class LiveCheck
    {
        private readonly bool enabled;

        public LiveCalculatorVersion.Result? Latest { get; private set; }

        public DateTimeOffset? CheckedAt { get; private set; }

        /// <summary>
        /// The live calculator version when it differs from this server's, else null.
        /// </summary>
        public string? NewerVersion => Latest != null && LiveCalculatorVersion.Differs(PlayCalculator.CalculatorVersion, Latest.CalculatorVersion) ? Latest.CalculatorVersion : null;

        public LiveCheck(bool enabled)
        {
            this.enabled = enabled;
        }

        public void Start(CancellationToken stopping)
        {
            if (!enabled)
                return;

            _ = Task.Run(async () =>
            {
                using (var http = CreateHttpClient())
                {
                    while (!stopping.IsCancellationRequested)
                    {
                        try
                        {
                            Latest = await LiveCalculatorVersion.FetchAsync(http, stopping).ConfigureAwait(false);
                            CheckedAt = DateTimeOffset.Now;

                            if (NewerVersion != null)
                                Console.WriteLine($"osu! now rates scores with {NewerVersion} (score processor {Latest.ReleaseTag}); this server uses {PlayCalculator.CalculatorVersion}. Run server/scripts/server-update.");
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            Console.WriteLine($"Could not check osu!'s live calculator version: {e.Message}");
                        }

                        try
                        {
                            await Task.Delay(TimeSpan.FromHours(12), stopping).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                }
            }, stopping);
        }

        public static HttpClient CreateHttpClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("maniaortho-score-server");
            return http;
        }
    }
}
