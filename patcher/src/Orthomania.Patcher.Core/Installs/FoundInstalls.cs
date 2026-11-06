using System;
using System.Collections.Generic;

namespace Orthomania.Patcher.Installs
{
    /// <summary>
    /// What the last search found (<c>found-installs.json</c>), so the next launch need not search again.
    /// </summary>
    public sealed class FoundInstalls
    {
        public DateTime? SearchedUtc { get; set; }

        public List<OsuInstall> Installs { get; set; } = new List<OsuInstall>();

        public void Remember(OsuInstall install)
        {
            Installs.RemoveAll(i => i.Folder.Equals(install.Folder, PathUtils.Comparison));
            Installs.Insert(0, install);
        }
    }

    /// <summary>
    /// What choosing a folder by hand came to: the install there (null when there is none), and what to tell the person.
    /// </summary>
    public sealed record FolderChoice(OsuInstall? Install, string Message);
}
