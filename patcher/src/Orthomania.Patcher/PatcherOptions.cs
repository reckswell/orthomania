using System;

namespace Orthomania.Patcher
{
    /// <summary>
    /// The patcher's command line. Normally there is none; the rest is for screenshots of each state and for testing.
    /// </summary>
    public sealed class PatcherOptions
    {
        /// <summary>
        /// Show one state without checking or changing anything (<c>--demo ready</c>).
        /// </summary>
        public string? Demo { get; private set; }

        /// <summary>
        /// Save a screenshot here once the window has settled, then close (<c>--screenshot file.png</c>).
        /// </summary>
        public string? Screenshot { get; private set; }

        /// <summary>
        /// Read Torus from this <c>osu.Game.Resources.dll</c> instead of the installed lazer's (<c>--lazer-resources path</c>).
        /// </summary>
        public string? LazerResources { get; private set; }

        public static PatcherOptions Parse(string[] args)
        {
            var options = new PatcherOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");

                switch (args[i])
                {
                    case "--demo":
                        options.Demo = value();
                        break;

                    case "--screenshot":
                        options.Screenshot = value();
                        break;

                    case "--lazer-resources":
                        options.LazerResources = value();
                        break;
                }
            }

            return options;
        }
    }
}
