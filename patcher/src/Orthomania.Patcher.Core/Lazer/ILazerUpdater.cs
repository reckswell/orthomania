using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orthomania.Patcher.Lazer
{
    /// <summary>
    /// Updates the official lazer install the way lazer updates itself. The patcher checks on every launch, so plain lazer is
    /// current even when orthomania has no build for the new release yet.
    /// </summary>
    public interface ILazerUpdater
    {
        /// <summary>
        /// The newer release lazer would update to, or null when it is current.
        /// </summary>
        Task<LazerUpdate?> CheckAsync(LazerInstall install, CancellationToken cancellationToken);

        /// <summary>
        /// Downloads the update into lazer's own packages folder, checked against the feed's hash.
        /// </summary>
        /// <param name="progress">From 0 to 1.</param>
        Task DownloadAsync(LazerUpdate update, Action<double> progress, CancellationToken cancellationToken);

        /// <summary>
        /// Installs a downloaded update with lazer's <c>Update.exe</c>, and returns once the install reports the new release.
        /// No lazer may be running.
        /// </summary>
        Task ApplyAsync(LazerUpdate update, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A lazer release newer than the installed one.
    /// </summary>
    public class LazerUpdate
    {
        public LazerInstall Install { get; }

        public LazerVersion From { get; }

        public LazerVersion To { get; }

        /// <summary>
        /// The download's size in bytes, when known.
        /// </summary>
        public long? Size { get; init; }

        public LazerUpdate(LazerInstall install, LazerVersion from, LazerVersion to)
        {
            Install = install;
            From = from;
            To = to;
        }

        public override string ToString() => $"osu! {From.Number} to {To.Number}";
    }

    public class LazerUpdateException : Exception
    {
        public LazerUpdateException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}
