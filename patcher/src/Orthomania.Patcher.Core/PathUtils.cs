using System;
using System.IO;

namespace Orthomania.Patcher
{
    public static class PathUtils
    {
        /// <summary>
        /// Windows paths ignore case; elsewhere they do not.
        /// </summary>
        public static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// A full path without a trailing separator (except a root's own).
        /// </summary>
        public static string Normalise(string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? string.Empty;
            return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }

        /// <summary>
        /// Whether <paramref name="path"/> is <paramref name="folder"/> or anything below it.
        /// </summary>
        public static bool IsInside(string path, string folder)
        {
            string p = Normalise(path);
            string f = Normalise(folder);

            return p.Equals(f, Comparison) || p.StartsWith(f + Path.DirectorySeparatorChar, Comparison);
        }
    }
}
