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

        private static void progress(int done, int total)
        {
            if (done == total || done % 100 == 0)
                Console.WriteLine($"Rating plays again: {done}/{total}");
        }
    }
}
