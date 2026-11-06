using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace OrthoServer.Tests
{
    /// <summary>
    /// <c>--parent-pid</c>: the server stops once the process that started it has ended.
    /// </summary>
    [TestFixture]
    public class ParentProcessWatchTest
    {
        private string dataPath = null!;
        private TimeSpan interval;

        [SetUp]
        public void SetUp()
        {
            dataPath = Path.Combine(Path.GetTempPath(), "ortho-server-test-" + Guid.NewGuid().ToString("N"));
            interval = ParentProcessWatch.Interval;
            ParentProcessWatch.Interval = TimeSpan.FromMilliseconds(100);
        }

        [TearDown]
        public void TearDown()
        {
            ParentProcessWatch.Interval = interval;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (Directory.Exists(dataPath))
                Directory.Delete(dataPath, true);
        }

        [Test]
        public async Task TestStopsAfterParentEnds()
        {
            using (var parent = startSleeper())
            {
                await using (var app = ServerHost.Create(new ServerOptions { DataPath = dataPath, Port = 0, CheckLiveVersion = false, ParentProcessId = parent.Id }))
                {
                    await app.StartAsync();
                    await Task.Delay(500);
                    Assert.That(app.Lifetime.ApplicationStopping.IsCancellationRequested, Is.False, "stopped while its parent runs");

                    parent.Kill();
                    await waitForStop(app);
                    await app.StopAsync();
                }
            }
        }

        [Test]
        public async Task TestStopsAtOnceWithoutParent()
        {
            int ended;

            using (var parent = startSleeper())
            {
                ended = parent.Id;
                parent.Kill();
                await parent.WaitForExitAsync();
            }

            await using (var app = ServerHost.Create(new ServerOptions { DataPath = dataPath, Port = 0, CheckLiveVersion = false, ParentProcessId = ended }))
            {
                await app.StartAsync();
                await waitForStop(app);
                await app.StopAsync();
            }
        }

        private static async Task waitForStop(WebApplication app)
        {
            var stopped = Task.Delay(Timeout.Infinite, app.Lifetime.ApplicationStopping);
            var finished = await Task.WhenAny(stopped, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(finished, Is.SameAs(stopped), "still running 10 s after its parent ended");
        }

        private static Process startSleeper() => Process.Start(OperatingSystem.IsWindows()
            ? new ProcessStartInfo("powershell", "-NoProfile -Command Start-Sleep -Seconds 60") { CreateNoWindow = true }
            : new ProcessStartInfo("sleep", "60"))!;
    }
}
