using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace OrthoServer
{
    /// <summary>
    /// Stops the server once the process that started it has ended (<c>--parent-pid</c>), so a server the patcher started for
    /// development never outlives it, even when the patcher is killed and cannot stop it. Stopping this way lets a play being
    /// stored finish first.
    /// </summary>
    public static class ParentProcessWatch
    {
        internal static TimeSpan Interval = TimeSpan.FromSeconds(1);

        public static void Start(int processId, IHostApplicationLifetime lifetime)
        {
            var parent = find(processId);

            if (parent == null || hasEnded(parent, processId))
            {
                stop(processId, lifetime);
                parent?.Dispose();
                return;
            }

            var stopping = lifetime.ApplicationStopping;

            _ = Task.Run(async () =>
            {
                using (parent)
                {
                    while (!stopping.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(Interval, stopping).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        if (hasEnded(parent, processId))
                        {
                            stop(processId, lifetime);
                            return;
                        }
                    }
                }
            }, stopping);
        }

        private static void stop(int processId, IHostApplicationLifetime lifetime)
        {
            Console.WriteLine($"The process that started this server ({processId}) has ended; stopping.");
            lifetime.StopApplication();
        }

        private static Process? find(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool hasEnded(Process parent, int processId)
        {
            try
            {
                return parent.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (Win32Exception)
            {
                // Not allowed to ask (another user's process): whether a process of that id still exists.
                using (var again = find(processId))
                    return again == null;
            }
        }
    }
}
