// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using System.Threading;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Playtest
{
    /// <summary>
    /// Clean-up for tests that point osu!'s storage at a temporary folder.
    /// </summary>
    public static class TemporaryFolder
    {
        /// <summary>
        /// Deletes <paramref name="path"/> and everything in it, trying again for a while if a file appears meanwhile: osu!'s
        /// storage settings (storage.ini) are saved from a background task shortly after an <c>OsuStorage</c> loads them, and
        /// that save can land while the folder is being deleted ("Directory not empty").
        /// </summary>
        public static void Delete(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
