using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Orthomania.Patcher.Releases
{
    /// <summary>
    /// Checks orthomania's DLLs against the osu! they are about to run in, by reading their metadata (nothing is loaded or run): every
    /// assembly a DLL needs must be among orthomania's own files or in osu!'s folder, osu!'s and its framework's at exactly the version
    /// the DLL was built against, anything else at that version or newer. A build made for another osu!, or an osu! whose files are
    /// not the release they say, fails here instead of inside the game.
    /// </summary>
    public static class OsuCompatibility
    {
        /// <summary>
        /// The prefix lazer loads rulesets by; a build ships exactly one ruleset.
        /// </summary>
        public const string RULESET_PREFIX = "osu.Game.Rulesets.";

        /// <summary>
        /// Checks <paramref name="build"/>'s DLLs against the osu! in <paramref name="osuFolder"/>.
        /// </summary>
        public static bool Check(RulesetBuild build, string osuFolder, out string? problem)
            => Check(build.Files.Where(f => isDll(f.File.Name)).Select(f => f.Path).ToList(), osuFolder, out problem);

        /// <summary>
        /// Checks the DLLs at <paramref name="ownDlls"/> (orthomania's files) against the osu! in <paramref name="osuFolder"/>.
        /// </summary>
        public static bool Check(IReadOnlyList<string> ownDlls, string osuFolder, out string? problem)
        {
            var own = new Dictionary<string, (string Path, Version Version, List<(string Name, Version Version)> References)>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in ownDlls)
            {
                if (!tryRead(path, out var identity, out problem))
                    return false;

                own[identity.Name] = (path, identity.Version, identity.References);
            }

            var rulesets = own.Keys.Where(n => n.StartsWith(RULESET_PREFIX, StringComparison.OrdinalIgnoreCase)).ToList();

            if (rulesets.Count != 1)
            {
                problem = rulesets.Count == 0 ? "the build has no ruleset DLL" : $"the build has {rulesets.Count} ruleset DLLs";
                return false;
            }

            // A self-contained lazer carries the .NET runtime; one that does not gets it from the computer.
            bool carriesRuntime = File.Exists(Path.Combine(osuFolder, "System.Private.CoreLib.dll"));

            foreach (var (name, (path, _, references)) in own)
            {
                string file = Path.GetFileName(path);

                foreach (var (needed, version) in references)
                {
                    if (own.TryGetValue(needed, out var shipped))
                    {
                        if (shipped.Version < version)
                        {
                            problem = $"{file} needs {needed} {version}, but orthomania's is {shipped.Version}";
                            return false;
                        }

                        continue;
                    }

                    string candidate = Path.Combine(osuFolder, needed + ".dll");

                    if (!File.Exists(candidate))
                    {
                        if (!carriesRuntime && isRuntime(needed))
                            continue;

                        problem = $"{file} needs {needed} {version}, which is not in osu!'s folder";
                        return false;
                    }

                    if (!tryRead(candidate, out var installed, out problem))
                        return false;

                    bool fits = isOsu(needed) ? installed.Version == version : installed.Version >= version;

                    if (!fits)
                    {
                        problem = $"{file} was built for {needed} {version}, but osu! has {installed.Version}";
                        return false;
                    }
                }
            }

            problem = null;
            return true;
        }

        /// <summary>
        /// osu!'s own assemblies and its framework's: built against exactly these, so nothing else will do.
        /// </summary>
        private static bool isOsu(string name)
            => name.Equals("osu.Game", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("osu.Game.", StringComparison.OrdinalIgnoreCase)
               || name.Equals("osu.Framework", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("osu.Framework.", StringComparison.OrdinalIgnoreCase);

        private static bool isRuntime(string name)
            => name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal)
               || name is "System" or "netstandard" or "mscorlib" or "WindowsBase";

        private static bool isDll(string name) => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        private sealed record Identity(string Name, Version Version, List<(string Name, Version Version)> References);

        private static bool tryRead(string path, out Identity identity, out string? problem)
        {
            identity = null!;
            string file = Path.GetFileName(path);

            try
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);

                if (!pe.HasMetadata)
                {
                    problem = $"{file} is not a .NET assembly";
                    return false;
                }

                var metadata = pe.GetMetadataReader();

                if (!metadata.IsAssembly)
                {
                    problem = $"{file} is not a .NET assembly";
                    return false;
                }

                var definition = metadata.GetAssemblyDefinition();
                var references = new List<(string, Version)>();

                foreach (var handle in metadata.AssemblyReferences)
                {
                    var reference = metadata.GetAssemblyReference(handle);
                    references.Add((metadata.GetString(reference.Name), reference.Version));
                }

                identity = new Identity(metadata.GetString(definition.Name), definition.Version, references);
                problem = null;
                return true;
            }
            catch (Exception e) when (e is BadImageFormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                problem = $"{file} could not be read as a .NET assembly ({e.Message})";
                return false;
            }
        }
    }
}
