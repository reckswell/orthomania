using System;
using System.Diagnostics;
using osu.Framework;
using osu.Framework.Platform;

namespace Orthomania.Patcher
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var options = PatcherOptions.Parse(args);
            bool demo = options.Demo != null;

            // Started by the patcher this one replaced: it holds the one-patcher lock until it has closed.
            if (options.After is int previous)
                waitForExit(previous);

            using GameHost host = Host.GetSuitableDesktopHost("orthomania-patcher", new HostOptions
            {
                FriendlyGameName = "orthomania",
                // One patcher at a time; a demo never counts.
                IPCPipeName = demo ? null : "orthomania-patcher",
            });

            if (!demo && !host.IsPrimaryInstance)
                return 0;

            host.Run(new PatcherGame(options));
            return 0;
        }

        private static void waitForExit(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.WaitForExit(TimeSpan.FromSeconds(30));
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already closed.
            }
        }
    }
}
