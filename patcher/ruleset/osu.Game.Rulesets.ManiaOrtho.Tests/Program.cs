// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework;
using osu.Framework.Platform;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Tests;

namespace osu.Game.Rulesets.ManiaOrtho.Tests
{
    public static class Program
    {
        /// <summary>
        /// With no arguments, opens the visual test browser (as for every other ruleset test project).
        /// With <c>playtest ...</c>, runs <see cref="PlaytestRunner"/> unattended and exits.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "playtest")
                return PlaytestRunner.Run(args.Skip(1).ToArray());

            using (DesktopGameHost host = Host.GetSuitableDesktopHost(@"osu-development"))
            {
                host.Run(new OsuTestBrowser());
                return 0;
            }
        }
    }
}
