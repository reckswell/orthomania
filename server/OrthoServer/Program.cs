using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using OrthoServer.Accounts;
using OrthoServer.Live;
using OrthoServer.Scoring;
using OrthoServer.Storage;

namespace OrthoServer
{
    public static class Program
    {
        private static readonly string usage = $"""
                                     maniaortho score server: keeps your maniaortho plays and rates them with osu!'s live mania calculators.

                                     Usage:
                                       OrthoServer [serve] [--data <folder>] [--port <port>] [--no-live-check] [--parent-pid <pid>]
                                           Serve the profile page and the upload endpoint on http://127.0.0.1:<port> (default 7727).
                                           Plays rated by another calculator version are rated again first. With --parent-pid,
                                           stop once that process has ended. Data defaults to {ServerOptions.DefaultDataPath}.
                                       OrthoServer recalculate [--data <folder>]
                                           Rate every stored play again.
                                       OrthoServer setup-osu-sign-in [--data <folder>] [--port <port>]
                                           Set up signing in with osu!: asks for the client id and secret of an OAuth application you
                                           register on osu!, and keeps them in the data folder.
                                       OrthoServer check-live [--write <calculator-version.txt>]
                                           Print the calculator version osu! runs live; with --write, store it in the file
                                           (rebuild and restart the server afterwards).
                                     """;

        public static async Task<int> Main(string[] args)
        {
            string command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "serve";
            var options = new ServerOptions();
            string? writePath = null;

            for (int i = command == "serve" && (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal)) ? 0 : 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--data" when i + 1 < args.Length:
                        options.DataPath = args[++i];
                        break;

                    case "--port" when i + 1 < args.Length:
                        options.Port = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;

                    case "--no-live-check":
                        options.CheckLiveVersion = false;
                        break;

                    case "--parent-pid" when i + 1 < args.Length:
                        options.ParentProcessId = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;

                    case "--write" when i + 1 < args.Length:
                        writePath = args[++i];
                        break;

                    default:
                        Console.Error.WriteLine($"Unknown argument \"{args[i]}\".\n\n{usage}");
                        return 2;
                }
            }

            switch (command)
            {
                case "serve":
                    return await serve(options).ConfigureAwait(false);

                case "recalculate":
                {
                    var service = new PlayService(new PlayStore(options.DataPath), new AccountStore(options.DataPath));
                    var (rated, failed) = service.Recalculate(onlyOutdated: false, progress);
                    Console.WriteLine($"Rated {rated} plays with {PlayCalculator.CalculatorVersion}; {failed} could not be rated.");
                    return failed == 0 ? 0 : 1;
                }

                case "check-live":
                    return await checkLive(writePath).ConfigureAwait(false);

                case "setup-osu-sign-in":
                    return setupOsuSignIn(options);

                case "help":
                case "--help":
                    Console.WriteLine(usage);
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command \"{command}\".\n\n{usage}");
                    return 2;
            }
        }

        private static async Task<int> serve(ServerOptions options)
        {
            var app = ServerHost.Create(options);
            var service = (PlayService)app.Services.GetService(typeof(PlayService))!;

            var (rated, failed) = service.Recalculate(onlyOutdated: true, progress);

            if (rated + failed > 0)
                Console.WriteLine($"Rated {rated} plays again with {PlayCalculator.CalculatorVersion}; {failed} could not be rated.");

            Console.WriteLine($"maniaortho score server: http://127.0.0.1:{options.Port}/ (calculators {PlayCalculator.CalculatorVersion}, data in {service.Store.DataPath})");

            await app.RunAsync().ConfigureAwait(false);
            return 0;
        }

        private static async Task<int> checkLive(string? writePath)
        {
            using (var http = LiveCheck.CreateHttpClient())
            {
                var live = await LiveCalculatorVersion.FetchAsync(http).ConfigureAwait(false);
                bool differs = LiveCalculatorVersion.Differs(PlayCalculator.CalculatorVersion, live.CalculatorVersion);

                Console.WriteLine($"osu!'s score processor {live.ReleaseTag} rates scores with {live.CalculatorVersion}; this server uses {PlayCalculator.CalculatorVersion}.");

                if (writePath != null && differs)
                {
                    await File.WriteAllTextAsync(writePath, live.CalculatorVersion + "\n").ConfigureAwait(false);
                    Console.WriteLine($"Wrote {live.CalculatorVersion} to {writePath}. Rebuild and restart the server to rate every play with it.");
                }

                return 0;
            }
        }

        private static int setupOsuSignIn(ServerOptions options)
        {
            string path = Path.Combine(Path.GetFullPath(options.DataPath), OsuSignIn.CONFIG_FILE);
            string callback = FormattableString.Invariant($"http://127.0.0.1:{options.Port}{OsuSignIn.CALLBACK_PATH}");

            Console.WriteLine($"""
                               Signing in with osu! needs an OAuth application of your own on osu! (once):

                                 1. Open {OsuSignIn.REGISTER_URL}
                                    and press "New OAuth Application".
                                 2. Name it (for example "orthomania score server") and set its Application Callback URL to
                                    {callback}
                                 3. Register it, then copy its Client ID and Client Secret here.

                               Players then sign in on osu!'s own page; this server only reads their public profile (name,
                               picture, country) and never sees a password. The secret stays in {path}, on this computer only.
                               Keep it to yourself, and reset the client secret on osu! if it ever leaks.

                               """);

            Console.Write("Client ID: ");
            string? id = Console.ReadLine()?.Trim();

            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long clientId) || clientId <= 0)
            {
                Console.Error.WriteLine("The client id is a number, as osu! shows it. Nothing was saved.");
                return 1;
            }

            Console.Write("Client Secret (not shown as you type): ");
            string secret = readHidden().Trim();

            if (secret.Length == 0)
            {
                Console.Error.WriteLine("No secret given. Nothing was saved.");
                return 1;
            }

            var config = new OsuOAuthConfig
            {
                ClientId = clientId,
                ClientSecret = secret,
                CallbackUrl = options.Port == ServerOptions.DEFAULT_PORT ? null : callback,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented) + "\n");

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Console.WriteLine($"Saved to {path}. The server uses it from now on (no restart needed): link the game in its settings.");
            return 0;
        }

        private static string readHidden()
        {
            if (Console.IsInputRedirected)
                return Console.ReadLine() ?? string.Empty;

            var text = new System.Text.StringBuilder();

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                    break;

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (text.Length > 0)
                        text.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                    text.Append(key.KeyChar);
            }

            Console.WriteLine();
            return text.ToString();
        }

        private static void progress(int done, int total)
        {
            if (done == total || done % 100 == 0)
                Console.WriteLine($"Rating plays again: {done}/{total}");
        }
    }
}
