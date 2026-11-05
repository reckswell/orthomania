using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Releases
{
    /// <summary>
    /// Where orthomania's builds are published, one per lazer release, and newer versions of the patcher.
    /// </summary>
    public interface IReleaseFeed
    {
        /// <summary>
        /// The newest build made for exactly <paramref name="lazer"/>, or null when there is none (yet).
        /// </summary>
        Task<PublishedBuild?> FindBuildAsync(LazerVersion lazer, CancellationToken cancellationToken);

        /// <summary>
        /// Downloads a build's manifest and files into <paramref name="staging"/>, which must be empty. The caller checks them
        /// (<see cref="BuildStore.Add"/>) before using any.
        /// </summary>
        /// <param name="progress">From 0 to 1.</param>
        Task DownloadAsync(PublishedBuild build, string staging, Action<double> progress, CancellationToken cancellationToken);

        /// <summary>
        /// The newest patcher, or null when none is published.
        /// </summary>
        Task<PublishedPatcher?> FindPatcherAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// A published build: which lazer release and build it is, and where its files are.
    /// </summary>
    /// <param name="Files">Download address by file name, including <see cref="ReleaseManifest.FILE_NAME"/>.</param>
    public sealed record PublishedBuild(LazerVersion Lazer, string Build, string Tag, IReadOnlyDictionary<string, Uri> Files)
    {
        public override string ToString() => $"orthomania {Build} for osu! {Lazer.Number}";
    }

    public sealed record PublishedPatcher(Version Version, string Tag, Uri Page);

    public class ReleaseFeedException : Exception
    {
        public ReleaseFeedException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}
