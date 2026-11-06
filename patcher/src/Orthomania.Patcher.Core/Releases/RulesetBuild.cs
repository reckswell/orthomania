using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Releases
{
    /// <summary>
    /// One orthomania build on disk: its <see cref="ReleaseManifest"/> and the files it lists, in one folder.
    /// </summary>
    public sealed class RulesetBuild
    {
        public string Directory { get; }

        public ReleaseManifest Manifest { get; }

        public LazerVersion Lazer => Manifest.LazerVersion;

        public string Build => Manifest.Build;

        public RulesetBuild(string directory, ReleaseManifest manifest)
        {
            Directory = directory;
            Manifest = manifest;
        }

        /// <summary>
        /// The build in <paramref name="directory"/>, or null when it has no readable manifest.
        /// </summary>
        public static RulesetBuild? Open(string directory)
        {
            string manifest = Path.Combine(directory, ReleaseManifest.FILE_NAME);

            if (!File.Exists(manifest))
                return null;

            try
            {
                return new RulesetBuild(directory, ReleaseManifest.Read(manifest));
            }
            catch (Exception e) when (e is InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                return null;
            }
        }

        public IEnumerable<(ManifestFile File, string Path)> Files => Manifest.Files.Select(f => (f, Path.Combine(Directory, f.Name)));

        /// <summary>
        /// Every listed file is present with its size and hash.
        /// </summary>
        public bool Verify(out string? problem)
        {
            foreach (var (file, path) in Files)
            {
                if (!File.Exists(path))
                {
                    problem = $"{file.Name} is missing";
                    return false;
                }

                if (!Hashing.Matches(path, file.Sha256, file.Size))
                {
                    problem = $"{file.Name} does not match its hash";
                    return false;
                }
            }

            problem = null;
            return true;
        }

        public override string ToString() => $"orthomania {Build} for osu! {Lazer.Number}";

        /// <summary>
        /// Orders build numbers: as versions when both read as one (<c>0.1.10</c> after <c>0.1.9</c>), otherwise as text.
        /// </summary>
        public static int CompareBuilds(string a, string b)
        {
            if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
                return va.CompareTo(vb);

            return string.CompareOrdinal(a, b);
        }
    }
}
