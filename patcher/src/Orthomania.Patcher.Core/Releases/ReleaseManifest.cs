using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Releases
{
    /// <summary>
    /// <c>orthomania.json</c>: what one orthomania build is, and the one lazer release it was built and tested against. Every
    /// GitHub release carries one, and every build the patcher keeps on disk has one beside its files.
    /// </summary>
    public sealed class ReleaseManifest
    {
        public const string FILE_NAME = "orthomania.json";

        /// <summary>
        /// 2: the build runs on osu!'s own data, shared with plain lazer. It keeps everything of its own in the data folder
        /// <see cref="Folders.GameSettings.FILE_NAME"/> names, never in osu!'s data folder, and keeps osu! signed out unless that file
        /// allows the osu! sign-in, without using or changing the sign-in osu! saved. Format 1 builds were made for a separate copy of
        /// osu!'s data, so this patcher does not start them.
        /// </summary>
        public const int CURRENT_FORMAT = 2;

        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public int Format { get; set; } = CURRENT_FORMAT;

        /// <summary>
        /// The lazer release this build was compiled and tested against (<c>2026.1026.0</c>). It is never put beside any other.
        /// </summary>
        public string Lazer { get; set; } = string.Empty;

        /// <summary>
        /// This build's own number; a higher one for the same lazer release replaces a lower one.
        /// </summary>
        public string Build { get; set; } = string.Empty;

        /// <summary>
        /// The orthomania commit it was built from.
        /// </summary>
        public string? Commit { get; set; }

        /// <summary>
        /// Lazer's ruleset API version the DLL declares (<c>Ruleset.CURRENT_RULESET_API_VERSION</c>), for the record.
        /// </summary>
        public string? RulesetApi { get; set; }

        public List<ManifestFile> Files { get; set; } = new List<ManifestFile>();

        [JsonIgnore]
        public LazerVersion LazerVersion => LazerVersion.Parse(Lazer);

        public static ReleaseManifest Read(string path) => Parse(File.ReadAllText(path));

        public static ReleaseManifest Parse(string text)
        {
            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(text, json_options)
                           ?? throw new InvalidDataException("The orthomania manifest is empty.");

            manifest.Validate();
            return manifest;
        }

        public string Serialise() => JsonSerializer.Serialize(this, json_options);

        /// <summary>
        /// Refuses a manifest the patcher cannot act on safely: an unknown format, an unreadable lazer version, or a file whose name
        /// could reach outside the build's folder or is not one of the kinds a build ships.
        /// </summary>
        public void Validate()
        {
            if (Format < CURRENT_FORMAT)
            {
                throw new InvalidDataException($"This orthomania build (format {Format}) was made for its own copy of osu!'s data; this patcher only starts "
                                               + $"format {CURRENT_FORMAT} builds, which share osu!'s data and leave osu! signed out.");
            }

            if (Format != CURRENT_FORMAT)
                throw new InvalidDataException($"Unknown orthomania manifest format {Format}; this patcher reads format {CURRENT_FORMAT}. Update the patcher.");

            if (!LazerVersion.TryParse(Lazer, out _))
                throw new InvalidDataException($"The manifest names no lazer release (\"{Lazer}\").");

            if (!IsSafeBuildName(Build))
                throw new InvalidDataException($"The manifest has no usable build number (\"{Build}\").");

            if (Files.Count == 0)
                throw new InvalidDataException("The manifest lists no files.");

            if (Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Files.Count)
                throw new InvalidDataException("The manifest lists a file twice.");

            foreach (var file in Files)
                file.Validate();

            if (!Files.Any(f => f.Name.StartsWith(RULESET_PREFIX, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The manifest has no ruleset DLL.");
        }

        /// <summary>
        /// A build number is also a folder name: letters, digits and <c>. + _ -</c> only, starting with a letter or digit.
        /// </summary>
        public static bool IsSafeBuildName(string? build)
            => !string.IsNullOrEmpty(build) && build.Length <= 64 && char.IsAsciiLetterOrDigit(build[0]) && !build.Contains("..")
               && build.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '_' or '-');

        /// <summary>
        /// Lazer loads <c>osu.Game.Rulesets.*.dll</c> from the folder it runs from.
        /// </summary>
        public const string RULESET_PREFIX = "osu.Game.Rulesets.";
    }

    public sealed class ManifestFile
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Lower-case hex.
        /// </summary>
        public string Sha256 { get; set; } = string.Empty;

        public long Size { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name) || Name != Path.GetFileName(Name) || Name.Contains("..") || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || Name.Contains('/') || Name.Contains('\\') || Name.Contains(':'))
                throw new InvalidDataException($"Unsafe file name in the manifest: \"{Name}\".");

            string extension = Path.GetExtension(Name).ToLowerInvariant();

            if (extension != ".dll" && extension != ".pdb")
                throw new InvalidDataException($"A build only ships .dll and .pdb files, not \"{Name}\".");

            if (Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException($"No valid SHA-256 for \"{Name}\".");

            if (Size <= 0)
                throw new InvalidDataException($"No size for \"{Name}\".");
        }
    }
}
