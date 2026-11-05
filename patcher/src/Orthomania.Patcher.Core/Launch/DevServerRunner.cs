using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Orthomania.Patcher.Settings;

namespace Orthomania.Patcher.Launch
{
    /// <summary>
    /// Starts the score server for development (<see cref="DevServerSettings"/>).
    /// </summary>
    public interface IDevServerRunner
    {
        /// <summary>
        /// Makes sure the server answers: reuses one already running, otherwise starts it and waits until it answers.
        /// </summary>
        /// <returns>The server this started, to stop after the game; null when one was already running.</returns>
        Task<IStartedServer?> EnsureRunningAsync(DevServerSettings settings, CancellationToken cancellationToken);
    }

    public interface IStartedServer : IAsyncDisposable
    {
        int ProcessId { get; }
    }

    public class DevServerException : Exception
    {
        public DevServerException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }

    public sealed class DevServerRunner : IDevServerRunner
    {
        public const string SERVER_NAME = "OrthoServer";

        private readonly HttpClient http;
        private readonly Action<string> log;
        private readonly string outputLog;
        private readonly string defaultData;
        private readonly string defaultFolder;
        private readonly int processId;

        /// <param name="log">The patcher's log.</param>
        /// <param name="outputLog">Where the server's own output goes.</param>
        /// <param name="defaultData">The server's data folder unless the settings name one.</param>
        /// <param name="defaultFolder">Where the server is looked for unless the settings name it. Null: <c>server</c> beside the patcher.</param>
        /// <param name="processId">The process the server outlives by at most a second. Null: the patcher's.</param>
        public DevServerRunner(Action<string> log, string outputLog, string defaultData, string? defaultFolder = null, HttpClient? http = null,
                               int? processId = null)
        {
            this.log = log;
            this.outputLog = outputLog;
            this.defaultData = defaultData;
            this.defaultFolder = defaultFolder ?? Path.Combine(AppContext.BaseDirectory, "server");
            this.processId = processId ?? Environment.ProcessId;
            this.http = http ?? new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
        }

        public async Task<IStartedServer?> EnsureRunningAsync(DevServerSettings settings, CancellationToken cancellationToken)
        {
            var health = LoopbackUri(settings.HealthUrl);

            if (await answersAsync(health, cancellationToken).ConfigureAwait(false))
            {
                log($"score server already running at {health}");
                return null;
            }

            var info = CreateStartInfo(settings, health);
            Directory.CreateDirectory(dataFolder(settings));
            log($"starting the score server: {info.FileName} {string.Join(' ', info.ArgumentList)}");

            Process process;

            try
            {
                process = Process.Start(info) ?? throw new DevServerException($"Could not start {info.FileName}.");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
            {
                throw new DevServerException($"Could not start the score server ({e.Message}).", e);
            }

            var started = new StartedServer(process, outputLog, log);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(1, settings.StartTimeoutSeconds));

            try
            {
                while (true)
                {
                    if (process.HasExited)
                    {
                        int code = process.ExitCode;
                        await started.DisposeAsync().ConfigureAwait(false);
                        throw new DevServerException($"The score server stopped before it answered (exit code {code}). Its output is in {outputLog}.");
                    }

                    if (await answersAsync(health, cancellationToken).ConfigureAwait(false))
                    {
                        log($"score server {process.Id} answering at {health}");
                        return started;
                    }

                    if (DateTime.UtcNow > deadline)
                    {
                        await started.DisposeAsync().ConfigureAwait(false);
                        throw new DevServerException($"The score server did not answer at {health} within {settings.StartTimeoutSeconds} s. Its output is in {outputLog}.");
                    }

                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                await started.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// The server's command line: the executable, or <c>dotnet</c> with the <c>.dll</c>, and the arguments filled in.
        /// </summary>
        internal ProcessStartInfo CreateStartInfo(DevServerSettings settings, Uri health)
        {
            string executable = findExecutable(settings);
            bool isDll = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

            var info = new ProcessStartInfo(isDll ? "dotnet" : executable)
            {
                WorkingDirectory = settings.WorkingDirectory != null ? Path.GetFullPath(expand(settings.WorkingDirectory)) : Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            if (isDll)
                info.ArgumentList.Add(executable);

            var values = new Dictionary<string, string>
            {
                ["{port}"] = health.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["{data}"] = dataFolder(settings),
                ["{pid}"] = processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

            foreach (string argument in settings.Arguments ?? DevServerSettings.DEFAULT_ARGUMENTS.ToList())
                info.ArgumentList.Add(values.Aggregate(expand(argument), (text, value) => text.Replace(value.Key, value.Value, StringComparison.Ordinal)));

            return info;
        }

        /// <summary>
        /// Always a full path: the server's own default is a folder relative to where it runs from.
        /// </summary>
        private string dataFolder(DevServerSettings settings) => Path.GetFullPath(settings.DataFolder != null ? expand(settings.DataFolder) : defaultData);

        private string findExecutable(DevServerSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Executable))
            {
                string named = Path.GetFullPath(expand(settings.Executable));

                if (!File.Exists(named))
                    throw new DevServerException($"The score server is not at {named}.");

                return named;
            }

            string[] candidates =
            {
                Path.Combine(defaultFolder, SERVER_NAME + ".exe"),
                Path.Combine(defaultFolder, SERVER_NAME),
                Path.Combine(defaultFolder, SERVER_NAME + ".dll"),
            };

            return candidates.FirstOrDefault(File.Exists)
                   ?? throw new DevServerException($"The score server is not in {defaultFolder}: put a build of it there, or set devServer.executable in the patcher's settings.json.");
        }

        private static string expand(string text) => Environment.ExpandEnvironmentVariables(text);

        /// <summary>
        /// Only an address on this computer: the patcher never starts or waits for a server anywhere else.
        /// </summary>
        internal static Uri LoopbackUri(string text)
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new DevServerException($"The score server's address ({text}) is not a web address.");

            bool loopback = uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip));

            if (!loopback)
                throw new DevServerException($"The score server's address ({text}) is not on this computer. The patcher only starts a server here.");

            return uri;
        }

        private async Task<bool> answersAsync(Uri health, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await http.GetAsync(health, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        /// <summary>
        /// Ending the server outright is safe: it writes each play in one step, and the game sends again what got no answer.
        /// </summary>
        private sealed class StartedServer : IStartedServer
        {
            private readonly Process process;
            private readonly Action<string> log;
            private readonly object writing = new object();
            private StreamWriter? output;

            public int ProcessId { get; }

            public StartedServer(Process process, string outputLog, Action<string> log)
            {
                this.process = process;
                this.log = log;
                ProcessId = process.Id;

                Directory.CreateDirectory(Path.GetDirectoryName(outputLog)!);
                output = new StreamWriter(new FileStream(outputLog, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };

                process.OutputDataReceived += (_, e) => write(e.Data);
                process.ErrorDataReceived += (_, e) => write(e.Data);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            private void write(string? line)
            {
                if (line == null)
                    return;

                lock (writing)
                    output?.WriteLine(line);
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);

                    // Also waits for the last of its output.
                    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);

                    log($"score server stopped (exit code {process.ExitCode})");
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
                {
                    log($"stopping the score server: {e.Message}");
                }
                finally
                {
                    lock (writing)
                    {
                        output?.Dispose();
                        output = null;
                    }

                    process.Dispose();
                }
            }
        }
    }
}
