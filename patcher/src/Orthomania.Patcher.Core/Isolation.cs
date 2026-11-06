using System.Collections.Generic;
using Orthomania.Patcher.Data;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher
{
    /// <summary>
    /// Checks that orthomania's own files stay apart from osu!'s before the patcher writes anything: orthomania's folders must not be
    /// inside lazer's install, and orthomania's data folder must hold only orthomania's data, never osu!'s.
    /// </summary>
    public static class Isolation
    {
        /// <param name="osuData">Where osu! keeps its data (paths only; never opened).</param>
        public static IReadOnlyList<string> Check(PatcherLayout layout, LazerInstall install, string dataFolder, IEnumerable<string> osuData)
        {
            var problems = new List<string>();

            if (PathUtils.IsInside(layout.Root, install.Root))
                problems.Add($"orthomania's folder ({layout.Root}) is inside osu!'s install. Move it out.");

            if (PathUtils.IsInside(install.Root, layout.Root))
                problems.Add($"osu!'s install ({install.Root}) is inside orthomania's folder. Reinstall osu! to its usual place.");

            if (PathUtils.IsInside(dataFolder, install.Root))
                problems.Add($"orthomania's data folder ({dataFolder}) is inside osu!'s install. Choose another one.");

            if (PathUtils.IsInside(dataFolder, layout.LazerFolders))
                problems.Add($"orthomania's data folder ({dataFolder}) is inside the folders it runs osu! from. Choose another one.");

            foreach (string osu in osuData)
            {
                if (PathUtils.IsInside(dataFolder, osu) || PathUtils.IsInside(osu, dataFolder))
                    problems.Add($"orthomania's data folder ({dataFolder}) is osu!'s own data ({osu}). Choose a separate one for orthomania's own files.");
            }

            if (new DataFolder(dataFolder).HoldsOsuData(out string? found))
            {
                problems.Add($"orthomania's data folder ({dataFolder}) holds osu!'s own data ({found}). orthomania now plays on osu!'s data and keeps "
                             + "only its own files there: move osu!'s files out, or choose another folder.");
            }

            return problems;
        }
    }
}
