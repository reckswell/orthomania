using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Orthomania.Patcher.Launch;
using Orthomania.Patcher.Settings;

namespace Orthomania.Patcher.Tests.Launch
{
    /// <summary>
    /// The development server starter, with a shell standing in for the server and a test listener answering its health route.
    /// </summary>
    [TestFixture]
    public class DevServerRunnerTest
    {
        private TempFolder temp = null!;
        private List<string> log = null!;

        [SetUp]
        public void SetUp()
        {
            temp = new TempFolder();
            log = new List<string>();
        }

        [TearDown]
        public void TearDown() => temp.Dispose();

        private string outputLog => temp.Combine("logs", "server.log");

        private DevServerRunner createRunner(int processId = 4242)
            => new DevServerRunner(s => { lock (log) log.Add(s); }, outputLog, temp.Combine("server-data"), temp.Combine("server"), processId: processId);

        [Test]
        public void TestDefaultCommandLine()
        {
            Directory.CreateDirectory(temp.Combine("server"));
            File.WriteAllText(temp.Combine("server", "OrthoServer.dll"), "");

            var info = createRunner().CreateStartInfo(new DevServerSettings(), new Uri(DevServerSettings.DEFAULT_HEALTH_URL));

            Assert.That(info.FileName, Is.EqualTo("dotnet"));
            Assert.That(info.ArgumentList, Is.EqualTo(new[]
            {
                temp.Combine("server", "OrthoServer.dll"), "serve", "--port", "7727", "--data", temp.Combine("server-data"), "--parent-pid", "4242",
            }));
            Assert.That(info.WorkingDirectory, Is.EqualTo(temp.Combine("server")));
            Assert.That(info.UseShellExecute, Is.False);
        }

        [Test]
        public void TestPrefersAPublishedServer()
        {
            Directory.CreateDirectory(temp.Combine("server"));
            File.WriteAllText(temp.Combine("server", "OrthoServer.dll"), "");
            File.WriteAllText(temp.Combine("server", "OrthoServer.exe"), "");

            var info = createRunner().CreateStartInfo(new DevServerSettings(), new Uri(DevServerSettings.DEFAULT_HEALTH_URL));

            Assert.That(info.FileName, Is.EqualTo(temp.Combine("server", "OrthoServer.exe")));
            Assert.That(info.ArgumentList.First(), Is.EqualTo("serve"));
        }

        [Test]
        public void TestNamedServerAndArguments()
        {
            string exe = temp.Combine("elsewhere", "OrthoServer.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");

            var settings = new DevServerSettings
            {
                Executable = exe,
                Arguments = new List<string> { "serve", "--port={port}", "--data", "{data}" },
                DataFolder = temp.Combine("mine", "..", "my-data"),
            };

            var info = createRunner().CreateStartInfo(settings, new Uri("http://localhost:9000/api/health"));

            Assert.That(info.FileName, Is.EqualTo(exe));
            Assert.That(info.ArgumentList, Is.EqualTo(new[] { "serve", "--port=9000", "--data", temp.Combine("my-data") }));
        }

        [Test]
        public void TestNoServerBuilt()
        {
            var e = Assert.Throws<DevServerException>(() => createRunner().CreateStartInfo(new DevServerSettings(), new Uri(DevServerSettings.DEFAULT_HEALTH_URL)));
            Assert.That(e!.Message, Does.Contain(temp.Combine("server")));

            var named = new DevServerSettings { Executable = temp.Combine("nowhere", "OrthoServer.exe") };
            e = Assert.Throws<DevServerException>(() => createRunner().CreateStartInfo(named, new Uri(DevServerSettings.DEFAULT_HEALTH_URL)));
            Assert.That(e!.Message, Does.Contain(temp.Combine("nowhere", "OrthoServer.exe")));
        }

        [TestCase("http://127.0.0.1:7727/api/health")]
        [TestCase("http://localhost:7727/api/health")]
        [TestCase("http://[::1]:7727/api/health")]
        public void TestLoopbackAddresses(string address) => Assert.That(DevServerRunner.LoopbackUri(address).IsLoopback, Is.True);

        [TestCase("http://192.168.1.20:7727/api/health")]
        [TestCase("https://example.com/api/health")]
        [TestCase("http://0.0.0.0:7727/api/health")]
        [TestCase("file:///C:/server")]
        [TestCase("not an address")]
        public void TestRefusesOtherAddresses(string address)
        {
            Assert.Throws<DevServerException>(() => DevServerRunner.LoopbackUri(address));

            var settings = new DevServerSettings { HealthUrl = address, Executable = shell() };
            Assert.ThrowsAsync<DevServerException>(() => createRunner().EnsureRunningAsync(settings, CancellationToken.None));
            Assert.That(File.Exists(outputLog), Is.False);
        }

        [Test]
        public async Task TestUsesARunningServer()
        {
            using var health = new FakeHealth(failFirst: 0);

            var started = await createRunner().EnsureRunningAsync(new DevServerSettings { HealthUrl = health.Url }, CancellationToken.None);

            Assert.That(started, Is.Null);
            Assert.That(health.Requests, Is.EqualTo(1));
            Assert.That(log.Single(), Does.Contain("already running"));
        }

        [Test]
        public async Task TestStartsWaitsAndStops()
        {
            using var health = new FakeHealth(failFirst: 2);
            var settings = new DevServerSettings { HealthUrl = health.Url, Executable = shell(), Arguments = script("echo started {port} {pid}", sleep: true) };

            var started = await createRunner().EnsureRunningAsync(settings, CancellationToken.None);

            Assert.That(started, Is.Not.Null);
            Assert.That(health.Requests, Is.EqualTo(3));
            Assert.That(isRunning(started!.ProcessId), Is.True);
            Assert.That(Directory.Exists(temp.Combine("server-data")), Is.True);

            await started.DisposeAsync();

            Assert.That(isRunning(started.ProcessId), Is.False);
            Assert.That(File.ReadAllText(outputLog), Does.Contain($"started {health.Port} 4242"));
            Assert.That(log.Last(), Does.StartWith("score server stopped"));
        }

        [Test]
        public void TestServerThatStopsAtOnce()
        {
            using var health = new FakeHealth(failFirst: int.MaxValue);
            var settings = new DevServerSettings { HealthUrl = health.Url, Executable = shell(), Arguments = script("echo no database&& exit 3", sleep: false) };

            var e = Assert.ThrowsAsync<DevServerException>(() => createRunner().EnsureRunningAsync(settings, CancellationToken.None));

            Assert.That(e!.Message, Does.Contain("exit code 3"));
            Assert.That(File.ReadAllText(outputLog), Does.Contain("no database"));
        }

        [Test]
        public void TestServerThatNeverAnswers()
        {
            using var health = new FakeHealth(failFirst: int.MaxValue);
            var settings = new DevServerSettings { HealthUrl = health.Url, Executable = shell(), Arguments = script("echo waiting", sleep: true), StartTimeoutSeconds = 1 };

            var e = Assert.ThrowsAsync<DevServerException>(() => createRunner().EnsureRunningAsync(settings, CancellationToken.None));

            Assert.That(e!.Message, Does.Contain("did not answer"));
            Assert.That(log.Last(), Does.StartWith("score server stopped"));
        }

        private static bool isRunning(int id)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string shell() => OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh";

        /// <summary>
        /// A stand-in server: runs <paramref name="command"/>, then stays up a minute when <paramref name="sleep"/>.
        /// </summary>
        private static List<string> script(string command, bool sleep)
        {
            if (OperatingSystem.IsWindows())
                return new List<string> { "/d", "/c", sleep ? $"{command}&& ping -n 60 127.0.0.1 >nul" : command };

            return new List<string> { "-c", sleep ? $"{command} && sleep 60" : command };
        }

        /// <summary>
        /// Answers every request on a loopback port: 503 for the first <c>failFirst</c>, then 200.
        /// </summary>
        private sealed class FakeHealth : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly int failFirst;
            private readonly CancellationTokenSource stop = new CancellationTokenSource();
            private int requests;

            public int Port { get; }

            public string Url => $"http://127.0.0.1:{Port}/api/health";

            public int Requests => Volatile.Read(ref requests);

            public FakeHealth(int failFirst)
            {
                this.failFirst = failFirst;
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _ = Task.Run(serveAsync);
            }

            private async Task serveAsync()
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient client;

                    try
                    {
                        client = await listener.AcceptTcpClientAsync(stop.Token);
                    }
                    catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
                    {
                        return;
                    }

                    using (client)
                    {
                        var stream = client.GetStream();
                        var buffer = new byte[4096];
                        var request = new StringBuilder();

                        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                        {
                            int read = await stream.ReadAsync(buffer);
                            if (read == 0)
                                break;

                            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        }

                        int number = Interlocked.Increment(ref requests);
                        string status = number <= failFirst ? "503 Service Unavailable" : "200 OK";
                        const string body = "{\"status\":\"ok\"}";

                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
                    }
                }
            }

            public void Dispose()
            {
                stop.Cancel();
                listener.Stop();
            }
        }
    }
}
