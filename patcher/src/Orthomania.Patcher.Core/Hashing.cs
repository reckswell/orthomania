using System;
using System.IO;
using System.Security.Cryptography;

namespace Orthomania.Patcher
{
    public static class Hashing
    {
        /// <summary>
        /// The file's SHA-256 in lower-case hex.
        /// </summary>
        public static string Sha256OfFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        public static bool Matches(string path, string sha256, long size)
            => File.Exists(path) && new FileInfo(path).Length == size && Sha256OfFile(path).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }
}
