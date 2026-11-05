using System;
using System.Threading;
using System.Threading.Tasks;
using Orthomania.Patcher.Folders;

namespace Orthomania.Patcher.Launch
{
    /// <summary>
    /// Runs osu! for the patcher: whether any lazer is running, and starting one from a version folder.
    /// </summary>
    public interface IGameRunner
    {
        bool IsLazerRunning();

        /// <summary>
        /// Starts osu! from <paramref name="folder"/> and returns its exit code once it closes.
        /// </summary>
        /// <param name="started">Called once the game has started.</param>
        Task<int> RunAsync(VersionFolder folder, Action started, CancellationToken cancellationToken);
    }

    public sealed class ProcessGameRunner : IGameRunner
    {
        public bool IsLazerRunning() => RunningGames.FindLazer().Count > 0;

        public async Task<int> RunAsync(VersionFolder folder, Action started, CancellationToken cancellationToken)
        {
            using var game = GameLauncher.Start(folder);
            started();

            await game.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return game.ExitCode;
        }
    }
}
