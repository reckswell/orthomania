using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace Orthomania.Patcher.Lazer
{
    /// <summary>
    /// Updates lazer with Velopack, the library and package feed lazer updates itself with (<c>osu.Desktop/Updater/VelopackUpdateManager.cs</c>):
    /// the same GitHub releases of <c>ppy/osu</c>, the same package in lazer's own <c>packages</c> folder, and lazer's own
    /// <c>Update.exe</c> to install it, so the install ends up exactly as lazer's in-game update leaves it.
    /// <para>
    /// The one difference is who asks: Velopack normally finds the install from the running program, so here a locator describes
    /// lazer's install instead (<see cref="LazerLocator"/>). Unlike lazer, this never moves to an older release.
    /// </para>
    /// </summary>
    public sealed class VelopackLazerUpdater : ILazerUpdater
    {
        public const string LAZER_REPOSITORY = "https://github.com/ppy/osu";

        private readonly Func<bool, IUpdateSource> createSource;
        private readonly Func<ProcessStartInfo, Task<int>> runUpdater;
        private readonly Action<string> log;

        /// <summary>
        /// How long <c>Update.exe</c> may take to install a release.
        /// </summary>
        public TimeSpan ApplyTimeout { get; init; } = TimeSpan.FromMinutes(5);

        /// <param name="log">Where Velopack's and the updater's messages go.</param>
        /// <param name="createSource">The feed, given whether the install follows pre-releases (lazer's tachyon stream). Defaults to lazer's.</param>
        /// <param name="runUpdater">Runs <c>Update.exe</c> and gives its exit code. Defaults to starting it.</param>
        public VelopackLazerUpdater(Action<string> log, Func<bool, IUpdateSource>? createSource = null, Func<ProcessStartInfo, Task<int>>? runUpdater = null)
        {
            this.log = log;
            this.createSource = createSource ?? (prerelease => new GithubSource(LAZER_REPOSITORY, null, prerelease));
            this.runUpdater = runUpdater ?? UpdaterProcesses.RunAsync;
        }

        public async Task<LazerUpdate?> CheckAsync(LazerInstall install, CancellationToken cancellationToken)
        {
            var release = install.ReadRelease();
            var locator = new LazerLocator(install, release, log, new UpdaterProcesses(install, runUpdater));

            // As lazer does: a tachyon install follows pre-releases (ReleaseStream.Tachyon).
            bool prerelease = release.VersionText.Contains("-tachyon", StringComparison.OrdinalIgnoreCase);

            var manager = new UpdateManager(createSource(prerelease), new UpdateOptions { AllowVersionDowngrade = false }, locator);

            UpdateInfo? info;

            try
            {
                info = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new LazerUpdateException($"Could not check for osu! updates ({e.Message}).", e);
            }

            if (info == null || info.IsDowngrade)
                return null;

            if (!LazerVersion.TryParse(info.TargetFullRelease.Version.ToString(), out var to))
                throw new LazerUpdateException($"osu!'s update feed offers a release this patcher cannot read ({info.TargetFullRelease.Version}).");

            if (to <= release.Version)
                return null;

            return new VelopackUpdate(install, release.Version, to, manager, info, locator) { Size = info.TargetFullRelease.Size };
        }

        public async Task DownloadAsync(LazerUpdate update, Action<double> progress, CancellationToken cancellationToken)
        {
            var velopack = (VelopackUpdate)update;

            try
            {
                await velopack.Manager.DownloadUpdatesAsync(velopack.Info, p => progress(p / 100.0), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new LazerUpdateException($"Could not download osu! {update.To.Number} ({e.Message}).", e);
            }
        }

        public async Task ApplyAsync(LazerUpdate update, CancellationToken cancellationToken)
        {
            var velopack = (VelopackUpdate)update;
            var locator = velopack.Locator;

            try
            {
                // Lazer's own call (WaitExitThenApplyUpdates) with nothing to wait for and no restart: the patcher starts the game itself.
                UpdateExe.Apply(locator, velopack.Info.TargetFullRelease, silent: false, waitPid: 0, restart: false);
            }
            catch (Exception e)
            {
                throw new LazerUpdateException($"Could not start osu!'s updater ({e.Message}).", e);
            }

            var updater = locator.Processes.Running ?? throw new LazerUpdateException("osu!'s updater did not start.");

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ApplyTimeout);

                int exitCode;

                try
                {
                    exitCode = await updater.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new LazerUpdateException($"osu!'s updater did not finish within {ApplyTimeout.TotalMinutes:0} minutes.");
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw new LazerUpdateException($"Could not run osu!'s updater ({e.Message}).", e);
                }

                if (exitCode != 0)
                    throw new LazerUpdateException($"osu!'s updater failed (exit code {exitCode}). Its log is {VelopackLogFile(update.Install)}.");
            }

            // The updater may hand the work to a copy of itself; the install is current once it says so.
            var deadline = DateTime.UtcNow + ApplyTimeout;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (readInstalled(update.Install) == update.To)
                    return;

                if (DateTime.UtcNow > deadline)
                    throw new LazerUpdateException($"osu!'s updater finished, but osu! is not at {update.To.Number}. Its log is {VelopackLogFile(update.Install)}.");

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }

        private static LazerVersion? readInstalled(LazerInstall install)
        {
            try
            {
                return LazerInstall.ReadReleaseFrom(install.Current)?.Version;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Mid-swap.
                return null;
            }
        }

        public static string VelopackLogFile(LazerInstall install)
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "velopack", "velopack_osulazer.log");

        private sealed class VelopackUpdate : LazerUpdate
        {
            public UpdateManager Manager { get; }

            public UpdateInfo Info { get; }

            public LazerLocator Locator { get; }

            public VelopackUpdate(LazerInstall install, LazerVersion from, LazerVersion to, UpdateManager manager, UpdateInfo info, LazerLocator locator)
                : base(install, from, to)
            {
                Manager = manager;
                Info = info;
                Locator = locator;
            }
        }
    }

    /// <summary>
    /// Describes lazer's install to Velopack as Velopack's Windows locator would from inside lazer (<c>WindowsVelopackLocator</c>):
    /// the install folder, <c>Update.exe</c>, <c>current</c>, <c>packages</c>, and the release and channel in <c>current\sq.version</c>.
    /// </summary>
    public sealed class LazerLocator : VelopackLocator
    {
        private readonly LazerInstall install;
        private readonly InstalledRelease release;

        public LazerLocator(LazerInstall install, InstalledRelease release, Action<string> log, UpdaterProcesses processes)
        {
            this.install = install;
            this.release = release;

            Log = new ActionLogger(log);
            Processes = processes;

            CurrentlyInstalledVersion = SemanticVersion.TryParse(release.VersionText, out var version)
                ? version
                : new SemanticVersion(release.Version.Year, release.Version.MonthDay, release.Version.Patch);
        }

        /// <summary>
        /// Starts <c>Update.exe</c> for Velopack and keeps hold of it, so the patcher can wait for it.
        /// </summary>
        public UpdaterProcesses Processes { get; }

        public override string AppId => release.AppId;

        public override string RootAppDir => install.Root;

        public override string PackagesDir => CreateSubDirIfDoesNotExist(install.Root, "packages") ?? install.PackagesDir;

        public override string UpdateExePath => install.UpdateExe;

        public override string AppContentDir => install.Current;

        public override string? Channel => release.Channel;

        public override IProcessImpl Process => Processes;

        public override IVelopackLogger Log { get; }

        public override SemanticVersion CurrentlyInstalledVersion { get; }

        private sealed class ActionLogger : IVelopackLogger
        {
            private readonly Action<string> log;

            public ActionLogger(Action<string> log)
            {
                this.log = log;
            }

            public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
            {
                if (logLevel < VelopackLogLevel.Information)
                    return;

                log($"velopack {logLevel}: {message}{(exception != null ? $" ({exception.Message})" : string.Empty)}");
            }
        }
    }

    /// <summary>
    /// The process side of <see cref="LazerLocator"/>. Velopack sees lazer as the running program, but never exits the patcher.
    /// </summary>
    public sealed class UpdaterProcesses : IProcessImpl
    {
        private readonly LazerInstall install;
        private readonly Func<ProcessStartInfo, Task<int>> run;

        public UpdaterProcesses(LazerInstall install, Func<ProcessStartInfo, Task<int>>? run = null)
        {
            this.install = install;
            this.run = run ?? RunAsync;
        }

        /// <summary>
        /// The exit code of the last process Velopack started (<c>Update.exe</c>), once it exits.
        /// </summary>
        public Task<int>? Running { get; private set; }

        public string GetCurrentProcessPath() => install.GameExecutable;

        public uint GetCurrentProcessId() => (uint)Environment.ProcessId;

        public void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow)
        {
            var info = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = !showWindow,
            };

            foreach (string arg in args)
                info.ArgumentList.Add(arg);

            Running = run(info);
        }

        public void Exit(int exitCode) => throw new InvalidOperationException("Velopack asked to exit; the patcher keeps running while osu! updates.");

        public static async Task<int> RunAsync(ProcessStartInfo info)
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {info.FileName}.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}
