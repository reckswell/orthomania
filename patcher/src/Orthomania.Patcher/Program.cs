using System;
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
    }
}
