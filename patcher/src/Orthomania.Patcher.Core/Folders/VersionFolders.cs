using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher.Folders
{
    /// <summary>
    /// The folders orthomania runs lazer from, one per lazer release: <c>%LOCALAPPDATA%\orthomania\lazer\&lt;release&gt;\</c>.
    /// <para>
    /// Each holds hard links to the official files of that release (the same bytes on disk as lazer's install, no extra space), plus
    /// orthomania's own files: the build's ruleset DLL and <c>0Harmony.dll</c> (lazer loads <c>osu.Game.Rulesets.*.dll</c> from the
    /// folder it runs from), and <see cref="GameSettings.FILE_NAME"/> (where orthomania keeps its own data, and whether it may use the
    /// osu! sign-in). Plain lazer, run from its own install, sees none of this.
    /// </para>
    /// <para>
    /// There is no <c>framework.ini</c> or <c>storage.ini</c> here, so lazer started from this folder keeps its data where plain lazer
    /// does: one set of maps, skins, scores and settings for both. <see cref="Verify"/> refuses a folder that has either.
    /// </para>
    /// <para>
    /// A linked file is never opened for writing: that would change lazer's own copy. orthomania only creates files of its own here,
    /// and removes a link of the same name first if there ever is one.
    /// </para>
    /// </summary>
    public sealed class VersionFolders
    {
        /// <summary>
        /// With this beside the program, osu-framework keeps the game's storage there instead of in osu!'s data folder.
        /// </summary>
        public const string FRAMEWORK_INI = "framework.ini";

        /// <summary>
        /// osu!'s own custom data location (<c>OsuStorage</c>), read from the game's storage.
        /// </summary>
        public const string STORAGE_INI = "storage.ini";

        private const string partial_suffix = ".partial";
        private const string old_suffix = ".old";

        public string Root { get; }

        public VersionFolders(string root)
        {
            Root = root;
        }

        public string PathFor(LazerVersion lazer) => Path.Combine(Root, lazer.Number);

        /// <summary>
        /// The finished folder for <paramref name="lazer"/>, or null.
        /// </summary>
        public VersionFolder? Get(LazerVersion lazer)
        {
            var folder = VersionFolder.Open(PathFor(lazer));
            return folder != null && folder.Lazer == lazer ? folder : null;
        }

        /// <summary>
        /// Every finished folder, newest release first.
        /// </summary>
        public IReadOnlyList<VersionFolder> All()
        {
            if (!Directory.Exists(Root))
                return Array.Empty<VersionFolder>();

            return Directory.GetDirectories(Root)
                            .Where(d => !d.EndsWith(partial_suffix, StringComparison.Ordinal) && !d.EndsWith(old_suffix, StringComparison.Ordinal))
                            .Select(VersionFolder.Open)
                            .OfType<VersionFolder>()
                            .OrderByDescending(f => f.Lazer)
                            .ToList();
        }

        /// <summary>
        /// Builds the folder for the installed release from scratch: links lazer's files, then adds orthomania's. Replaces an
        /// existing folder for the same release only once the new one is complete.
        /// </summary>
        public VersionFolder Create(LazerInstall install, RulesetBuild build, GameSettings game)
        {
            var release = install.ReadRelease();

            if (release.Version != build.Lazer)
                throw new InvalidOperationException($"{build} cannot go beside osu! {release.Version.Number}.");

            string target = PathFor(release.Version);
            string partial = target + partial_suffix;

            if (Directory.Exists(partial))
                Directory.Delete(partial, recursive: true);

            Directory.CreateDirectory(partial);

            var record = new FolderRecord
            {
                Lazer = release.Version.Number,
                Build = build.Build,
                DataFolder = game.DataFolder,
                OsuSignIn = game.OsuSignIn,
                CreatedUtc = DateTime.UtcNow,
                Linked = true,
            };

            try
            {
                string source = PathUtils.Normalise(install.Current);

                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(source, file);
                    string destination = Path.Combine(partial, relative);

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    if (!HardLink.CreateOrCopy(file, destination))
                        record.Linked = false;

                    record.LazerFiles.Add(new FolderEntry(relative, new FileInfo(file).Length));
                }

                // lazer may have updated itself while its files were being linked: then they are a mix of two releases.
                if (install.ReadRelease().Version != release.Version)
                    throw new IOException("osu! changed while its files were being prepared. Try again.");

                writeOwnFiles(partial, record, build, game);
                writeRecord(partial, record);
            }
            catch
            {
                Directory.Delete(partial, recursive: true);
                throw;
            }

            swapIn(partial, target);
            return Get(release.Version)!;
        }

        /// <summary>
        /// Replaces orthomania's own files in an existing folder (a newer build for the same release, or other game settings),
        /// leaving lazer's files as they are.
        /// </summary>
        public VersionFolder Refresh(VersionFolder folder, RulesetBuild build, GameSettings game)
        {
            if (build.Lazer != folder.Lazer)
                throw new InvalidOperationException($"{build} cannot go beside osu! {folder.Record.Lazer}.");

            var record = folder.Record;

            foreach (var own in record.OwnFiles)
                deleteIfPresent(Path.Combine(folder.Directory, own.Name));

            record.OwnFiles.Clear();
            record.Build = build.Build;
            record.DataFolder = game.DataFolder;
            record.OsuSignIn = game.OsuSignIn;

            writeOwnFiles(folder.Directory, record, build, game);
            writeRecord(folder.Directory, record);

            return VersionFolder.Open(folder.Directory)!;
        }

        /// <summary>
        /// Whether the folder is whole: lazer's files present at their recorded sizes, orthomania's matching their hashes, and nothing
        /// that would keep osu!'s data apart from plain osu!'s. Other files lazer adds while running are not checked.
        /// </summary>
        public static bool Verify(VersionFolder folder, out string? problem)
        {
            // Lazer's updater puts a new release in a new folder, so the links keep the old files. Were it ever to write into the old
            // files instead, the links would see it: the folder would no longer be the release it says.
            var release = LazerInstall.ReadReleaseFrom(folder.Directory);
            var assembly = LazerInstall.ReadGameAssemblyVersion(folder.Directory);

            if (release == null || release.Version != folder.Lazer || (assembly != null && assembly != folder.Lazer))
            {
                problem = $"its osu! files are no longer release {folder.Record.Lazer}";
                return false;
            }

            foreach (var entry in folder.Record.LazerFiles)
            {
                string path = Path.Combine(folder.Directory, entry.Path);

                if (!File.Exists(path) || new FileInfo(path).Length != entry.Size)
                {
                    problem = $"osu!'s file {entry.Path} is missing or changed";
                    return false;
                }
            }

            foreach (var own in folder.Record.OwnFiles)
            {
                if (!Hashing.Matches(Path.Combine(folder.Directory, own.Name), own.Sha256, own.Size))
                {
                    problem = $"{own.Name} is missing or changed";
                    return false;
                }
            }

            // orthomania's DLLs against the osu! files beside them, which are the ones lazer runs them with.
            var ownDlls = folder.Record.OwnFiles.Where(o => o.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                .Select(o => Path.Combine(folder.Directory, o.Name)).ToList();

            if (!OsuCompatibility.Check(ownDlls, folder.Directory, out string? unfit))
            {
                problem = $"orthomania does not fit its osu! files: {unfit}";
                return false;
            }

            // Were lazer's own release to ship these, plain lazer would have them too; any other copy gives orthomania's osu! data of its own.
            foreach (string apart in new[] { FRAMEWORK_INI, STORAGE_INI })
            {
                bool lazers = folder.Record.LazerFiles.Exists(e => e.Path.Equals(apart, PathUtils.Comparison));

                if (!lazers && File.Exists(Path.Combine(folder.Directory, apart)))
                {
                    problem = $"{apart} would keep osu!'s data apart from plain osu!'s";
                    return false;
                }
            }

            problem = null;
            return true;
        }

        /// <summary>
        /// Deletes a folder. Only the links go: lazer's own files stay where they are.
        /// </summary>
        public void Remove(VersionFolder folder) => Directory.Delete(folder.Directory, recursive: true);

        /// <summary>
        /// Deletes every folder (and unfinished leftovers) except those in <paramref name="keep"/>.
        /// </summary>
        public void Prune(IEnumerable<VersionFolder> keep)
        {
            if (!Directory.Exists(Root))
                return;

            var kept = new HashSet<string>(keep.Select(f => PathUtils.Normalise(f.Directory)), StringComparer.Ordinal);

            foreach (string dir in Directory.GetDirectories(Root))
            {
                if (!kept.Contains(PathUtils.Normalise(dir)))
                    Directory.Delete(dir, recursive: true);
            }
        }

        private static void writeOwnFiles(string folder, FolderRecord record, RulesetBuild build, GameSettings game)
        {
            writeOwnFile(folder, record, GameSettings.FILE_NAME, Encoding.UTF8.GetBytes(game.Json));

            foreach (var (file, path) in build.Files)
            {
                // A build adds files to lazer's, it never replaces one: the folder must hold lazer's own code byte for byte.
                if (File.Exists(Path.Combine(folder, file.Name)))
                    throw new InvalidDataException($"{build} ships {file.Name}, which osu! {record.Lazer} already has. It cannot replace osu!'s own files.");

                writeOwnFile(folder, record, file.Name, File.ReadAllBytes(path));
            }
        }

        private static void writeOwnFile(string folder, FolderRecord record, string name, byte[] content)
        {
            string path = Path.Combine(folder, name);

            // Never write through a link to lazer's own copy: if lazer ever ships a file of this name, drop the link and own the name.
            if (File.Exists(path))
            {
                File.Delete(path);
                record.LazerFiles.RemoveAll(e => e.Path.Equals(name, PathUtils.Comparison));
            }

            File.WriteAllBytes(path, content);
            record.OwnFiles.Add(new OwnFile(name, Hashing.Sha256OfFile(path), content.Length));
        }

        private static void writeRecord(string folder, FolderRecord record)
        {
            string path = Path.Combine(folder, VersionFolder.MARKER);
            string temp = path + ".tmp";

            File.WriteAllText(temp, JsonSerializer.Serialize(record, FolderRecord.JSON_OPTIONS));
            File.Move(temp, path, overwrite: true);
        }

        private static void deleteIfPresent(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void swapIn(string partial, string target)
        {
            string old = target + old_suffix;

            if (Directory.Exists(old))
                Directory.Delete(old, recursive: true);

            if (Directory.Exists(target))
                Directory.Move(target, old);

            Directory.Move(partial, target);

            if (Directory.Exists(old))
                Directory.Delete(old, recursive: true);
        }
    }
}
