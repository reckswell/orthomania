using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Launch
{
    /// <summary>
    /// Finds running copies of osu!(lazer), wherever they were started from. Only one lazer runs at a time: a second one hands its
    /// arguments to the first and quits. So the patcher waits until none runs before it changes files or starts one of its own.
    /// osu!(stable), also <c>osu!.exe</c>, does not count.
    /// </summary>
    public static class RunningGames
    {
        public sealed record RunningGame(int ProcessId, string? Executable);

        public static IReadOnlyList<RunningGame> FindLazer()
        {
            var found = new List<RunningGame>();

            foreach (var process in Process.GetProcessesByName(LazerInstall.PROCESS_NAME))
            {
                using (process)
                {
                    string? executable = null;

                    try
                    {
                        executable = process.MainModule?.FileName;
                    }
                    catch (Exception)
                    {
                        // Another user's or an elevated process: its path is hidden. Count it, to be safe.
                    }

                    if (executable == null || IsLazerExecutable(executable))
                        found.Add(new RunningGame(process.Id, executable));
                }
            }

            return found;
        }

        /// <summary>
        /// Lazer is a .NET program with <c>osu.Game.dll</c> beside it; stable has none.
        /// </summary>
        public static bool IsLazerExecutable(string executable)
        {
            string? folder = Path.GetDirectoryName(executable);
            return folder != null && File.Exists(Path.Combine(folder, "osu.Game.dll"));
        }
    }
}
