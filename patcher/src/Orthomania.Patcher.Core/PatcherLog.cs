using System;
using System.Globalization;
using System.IO;

namespace Orthomania.Patcher
{
    /// <summary>
    /// The patcher's log (<c>logs\patcher.log</c>), kept to about a megabyte with one older file beside it.
    /// </summary>
    public sealed class PatcherLog
    {
        private const long max_size = 1024 * 1024;

        private readonly object writeLock = new object();

        public string Path { get; }

        public event Action<string>? Written;

        public PatcherLog(string folder)
        {
            Directory.CreateDirectory(folder);
            Path = System.IO.Path.Combine(folder, "patcher.log");

            try
            {
                if (File.Exists(Path) && new FileInfo(Path).Length > max_size)
                    File.Move(Path, Path + ".old", overwrite: true);
            }
            catch (IOException)
            {
                // Another patcher has it open: share the file.
            }
        }

        public void Add(string message)
        {
            string line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {message}";

            lock (writeLock)
            {
                try
                {
                    File.AppendAllText(Path, line + Environment.NewLine);
                }
                catch (IOException)
                {
                }
            }

            Written?.Invoke(line);
        }
    }
}
