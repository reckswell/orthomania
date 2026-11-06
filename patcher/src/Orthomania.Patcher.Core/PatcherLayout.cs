using System;
using System.IO;
using Orthomania.Patcher.Folders;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher
{
    /// <summary>
    /// Everything the patcher keeps, under one folder (<c>%LOCALAPPDATA%\orthomania</c>):
    /// <code>
    /// builds\&lt;lazer&gt;\&lt;build&gt;\   orthomania builds, each with its orthomania.json   (BuildStore)
    /// lazer\&lt;lazer&gt;\            what lazer runs from: links to its files, plus orthomania  (VersionFolders)
    /// downloads\                  unfinished downloads
    /// logs\                       the patcher's logs
    /// server\                     the score server's data, when the patcher starts it (development only)
    /// settings.json               what the person chose
    /// state.json                  what the patcher remembers between launches
    /// found-installs.json         the osu! installs the last search found
    /// </code>
    /// It sits in <c>%LOCALAPPDATA%</c> because hard links only work on the drive lazer's install is on, which is the same
    /// folder, and because OneDrive never syncs it.
    /// </summary>
    public sealed class PatcherLayout
    {
        public string Root { get; }

        public PatcherLayout(string root)
        {
            Root = PathUtils.Normalise(root);
        }

        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "orthomania");

        public static PatcherLayout Default => new PatcherLayout(DefaultRoot);

        public string Builds => Path.Combine(Root, "builds");

        public string LazerFolders => Path.Combine(Root, "lazer");

        public string Downloads => Path.Combine(Root, "downloads");

        public string Logs => Path.Combine(Root, "logs");

        public string ServerData => Path.Combine(Root, "server");

        public string SettingsFile => Path.Combine(Root, "settings.json");

        public string StateFile => Path.Combine(Root, "state.json");

        public string FoundInstallsFile => Path.Combine(Root, "found-installs.json");

        public BuildStore CreateBuildStore() => new BuildStore(Builds);

        public VersionFolders CreateVersionFolders() => new VersionFolders(LazerFolders);

        public void CreateFolders()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Builds);
            Directory.CreateDirectory(LazerFolders);
            Directory.CreateDirectory(Downloads);
            Directory.CreateDirectory(Logs);
        }
    }
}
