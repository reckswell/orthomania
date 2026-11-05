using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher.Tests
{
    /// <summary>
    /// A temporary folder, deleted after the test.
    /// </summary>
    public sealed class TempFolder : IDisposable
    {
        public string Path { get; }

        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "orthomania-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Stand-ins for lazer's install and orthomania's builds, laid out as the real ones are.
    /// </summary>
    public static class Fakes
    {
        /// <summary>
        /// A Velopack install of <paramref name="version"/> at <paramref name="root"/>: <c>Update.exe</c>, and <c>current\</c> with the
        /// game, a nested file and <c>sq.version</c>.
        /// </summary>
        public static LazerInstall Lazer(string root, string version = "2026.1026.0")
        {
            string current = System.IO.Path.Combine(root, "current");
            Directory.CreateDirectory(System.IO.Path.Combine(current, "runtimes", "win-x64", "native"));

            File.WriteAllText(System.IO.Path.Combine(root, "Update.exe"), "updater");
            File.WriteAllText(System.IO.Path.Combine(current, LazerInstall.GAME_EXECUTABLE), $"game {version}");
            File.WriteAllText(System.IO.Path.Combine(current, "osu.Framework.dll"), $"framework {version}");
            File.WriteAllText(System.IO.Path.Combine(current, "runtimes", "win-x64", "native", "SDL3.dll"), "sdl");
            WriteVersionManifest(current, version);

            return LazerInstall.Find(root)!;
        }

        public static void WriteVersionManifest(string folder, string version, string channel = "win")
        {
            File.WriteAllText(System.IO.Path.Combine(folder, LazerInstall.VERSION_MANIFEST),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<package xmlns=\"http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd\">\n"
                + "  <metadata>\n"
                + "    <id>osulazer</id>\n"
                + "    <title>osu!</title>\n"
                + $"    <version>{version}</version>\n"
                + $"    <channel>{channel}</channel>\n"
                + "    <mainExe>osu!.exe</mainExe>\n"
                + "  </metadata>\n"
                + "</package>\n");
        }

        /// <summary>
        /// Velopack's update: a new <c>current\</c> folder (new files) in place of the old.
        /// </summary>
        public static void UpdateLazer(LazerInstall install, string version)
        {
            string staging = install.Current + "-new";
            Directory.CreateDirectory(staging);

            File.WriteAllText(System.IO.Path.Combine(staging, LazerInstall.GAME_EXECUTABLE), $"game {version} (bigger)");
            File.WriteAllText(System.IO.Path.Combine(staging, "osu.Framework.dll"), $"framework {version} (bigger)");
            WriteVersionManifest(staging, version);

            Directory.Delete(install.Current, recursive: true);
            Directory.Move(staging, install.Current);
        }

        /// <summary>
        /// An orthomania build folder for <paramref name="lazer"/>, with its manifest and files.
        /// </summary>
        public static string Build(string folder, string lazer = "2026.1026.0", string build = "0.1.0", Dictionary<string, string>? files = null)
        {
            Directory.CreateDirectory(folder);

            files ??= new Dictionary<string, string>
            {
                ["osu.Game.Rulesets.ManiaOrtho.dll"] = $"ruleset {build} for {lazer}",
                ["0Harmony.dll"] = "harmony 2.4.3",
            };

            var manifest = new ReleaseManifest { Lazer = lazer, Build = build };

            foreach (var (name, content) in files)
            {
                string path = System.IO.Path.Combine(folder, name);
                File.WriteAllText(path, content, new UTF8Encoding(false));
                manifest.Files.Add(new ManifestFile { Name = name, Sha256 = Hashing.Sha256OfFile(path), Size = new FileInfo(path).Length });
            }

            File.WriteAllText(System.IO.Path.Combine(folder, ReleaseManifest.FILE_NAME), manifest.Serialise());
            return folder;
        }
    }
}
