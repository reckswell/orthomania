using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Releases
{
    /// <summary>
    /// The orthomania builds the patcher has downloaded (or was given), one folder each:
    /// <c>builds\&lt;lazer release&gt;\&lt;build&gt;\</c>. Only verified builds are ever added.
    /// </summary>
    public sealed class BuildStore
    {
        public string Root { get; }

        public BuildStore(string root)
        {
            Root = root;
        }

        public string FolderFor(LazerVersion lazer, string build) => Path.Combine(Root, lazer.Number, build);

        /// <summary>
        /// The newest intact build made for <paramref name="lazer"/>, or null.
        /// </summary>
        public RulesetBuild? FindNewest(LazerVersion lazer)
            => All(lazer).Where(b => b.Verify(out _)).OrderByDescending(b => b.Build, Comparer<string>.Create(RulesetBuild.CompareBuilds)).FirstOrDefault();

        /// <summary>
        /// Every build on disk for <paramref name="lazer"/>, intact or not.
        /// </summary>
        public IEnumerable<RulesetBuild> All(LazerVersion lazer)
        {
            string folder = Path.Combine(Root, lazer.Number);

            if (!Directory.Exists(folder))
                yield break;

            foreach (string dir in Directory.GetDirectories(folder))
            {
                var build = RulesetBuild.Open(dir);

                // A folder whose manifest names another release is not trusted for this one.
                if (build != null && build.Lazer == lazer)
                    yield return build;
            }
        }

        /// <summary>
        /// Moves a complete, verified build from <paramref name="staging"/> into the store, replacing an earlier copy of the same build.
        /// </summary>
        public RulesetBuild Add(string staging)
        {
            var build = RulesetBuild.Open(staging) ?? throw new InvalidDataException($"No orthomania manifest in {staging}.");

            if (!build.Verify(out string? problem))
                throw new InvalidDataException($"The downloaded build is damaged: {problem}.");

            string target = FolderFor(build.Lazer, build.Build);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);

            Directory.Move(staging, target);
            return RulesetBuild.Open(target)!;
        }

        /// <summary>
        /// Deletes every build except those in <paramref name="keep"/>.
        /// </summary>
        public void Prune(IEnumerable<RulesetBuild> keep)
        {
            if (!Directory.Exists(Root))
                return;

            var kept = new HashSet<string>(keep.Select(b => PathUtils.Normalise(b.Directory)), StringComparer.Ordinal);

            foreach (string lazerFolder in Directory.GetDirectories(Root))
            {
                foreach (string buildFolder in Directory.GetDirectories(lazerFolder))
                {
                    if (!kept.Contains(PathUtils.Normalise(buildFolder)))
                        Directory.Delete(buildFolder, recursive: true);
                }

                if (Directory.GetFileSystemEntries(lazerFolder).Length == 0)
                    Directory.Delete(lazerFolder);
            }
        }
    }
}
