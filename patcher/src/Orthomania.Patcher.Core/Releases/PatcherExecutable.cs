using System;
using System.IO;

namespace Orthomania.Patcher.Releases
{
    /// <summary>
    /// The running orthomania.exe, which a released patcher replaces with a newer published one. Windows lets a running program be
    /// renamed but not deleted, so the old file moves aside to <see cref="OldPath"/> and goes on the next start.
    /// </summary>
    public sealed class PatcherExecutable
    {
        /// <summary>
        /// No orthomania.exe is anywhere near this size; a larger download is not one.
        /// </summary>
        public const long MAX_SIZE = 512 * 1024 * 1024;

        public string Path { get; }

        public string OldPath => Path + ".old";

        /// <summary>
        /// Where a newer patcher downloads to, beside this one (so putting it in place is a rename on the same drive).
        /// </summary>
        public string DownloadPath => Path + ".download";

        public PatcherExecutable(string path)
        {
            Path = System.IO.Path.GetFullPath(path);
        }

        /// <summary>
        /// Removes what an earlier update left: the old patcher (now closed) and an unfinished download.
        /// </summary>
        public void CleanUp(Action<string> log)
        {
            foreach (string leftover in new[] { OldPath, DownloadPath })
            {
                try
                {
                    if (File.Exists(leftover))
                    {
                        File.Delete(leftover);
                        log($"removed {System.IO.Path.GetFileName(leftover)} left by an update");
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    log($"could not remove {leftover}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Puts <paramref name="downloaded"/> (already checked against its published hash) in place of this patcher. When that fails
        /// part way, this patcher is put back as it was and the exception is thrown.
        /// </summary>
        public void Replace(string downloaded)
        {
            if (!IsWindowsProgram(downloaded))
                throw new InvalidDataException("the download is not a Windows program");

            if (File.Exists(OldPath))
                File.Delete(OldPath);

            File.Move(Path, OldPath);

            try
            {
                File.Move(downloaded, Path);
            }
            catch
            {
                File.Move(OldPath, Path);
                throw;
            }
        }

        /// <summary>
        /// Starts with the "MZ" every Windows program starts with.
        /// </summary>
        public static bool IsWindowsProgram(string path)
        {
            using var stream = File.OpenRead(path);
            return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
        }
    }
}
