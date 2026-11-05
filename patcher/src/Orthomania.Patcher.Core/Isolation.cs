using System.Collections.Generic;
using Orthomania.Patcher.Data;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher
{
    /// <summary>
    /// Checks that plain lazer and orthomania stay apart before the patcher writes anything: orthomania's folders must not be
    /// inside lazer's install, and its data must not be plain lazer's data.
    /// </summary>
    public static class Isolation
    {
        public static IReadOnlyList<string> Check(PatcherLayout layout, LazerInstall install, string dataFolder, IEnumerable<string> plainLazerData)
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

            foreach (string plain in plainLazerData)
            {
                if (PathUtils.IsInside(dataFolder, plain) || PathUtils.IsInside(plain, dataFolder))
                    problems.Add($"orthomania's data folder ({dataFolder}) is plain osu!'s data ({plain}). Choose a separate one, so each keeps its own.");
            }

            return problems;
        }
    }
}
