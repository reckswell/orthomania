using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orthomania.Patcher.Data;
using Orthomania.Patcher.Folders;
using Orthomania.Patcher.Launch;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Releases;
using Orthomania.Patcher.Settings;

namespace Orthomania.Patcher.Flow
{
    /// <summary>
    /// What one launch of the patcher does, in order:
    /// <list type="number">
    /// <item>find official lazer, and check orthomania's folders and data are apart from osu!'s;</item>
    /// <item>wait until no lazer is running;</item>
    /// <item>update lazer with its own updater, if a newer release is out;</item>
    /// <item>get the newest orthomania build made for exactly the installed release;</item>
    /// <item>prepare the version folder for that pair, and wait on <see cref="PatcherStage.Ready"/>;</item>
    /// <item>on Launch: start osu! from the folder.</item>
    /// </list>
    /// osu! started this way keeps its data where plain osu! does, so both see the same maps, skins, scores and settings. Only one
    /// release ever opens that data, the installed one, so with no build for it yet orthomania does not start: an older release could
    /// not open what the newer one has written. The patcher never opens osu!'s data folder; every failure past finding lazer leaves
    /// it able to say what happened, and none changes lazer's install or osu!'s data.
    /// </summary>
    public sealed class PatcherFlow
    {
        /// <summary>
        /// A game that ran this long started fine, whatever its exit code.
        /// </summary>
        public static readonly TimeSpan WORKED_AFTER = TimeSpan.FromSeconds(20);

        private readonly PatcherLayout layout;
        private readonly ILazerUpdater lazerUpdater;
        private readonly IReleaseFeed feed;
        private readonly IGameRunner runner;
        private readonly PatcherLog log;
        private readonly Version patcherVersion;
        private readonly BuildStore builds;
        private readonly VersionFolders folders;
        private readonly Func<string?, LazerInstall?> findLazer;
        private readonly IReadOnlyList<string> osuData;
        private readonly IDevServerRunner? devServer;

        public PatcherSettings Settings { get; private set; }

        public PatcherState State { get; private set; }

        public PatcherStatus Status { get; private set; } = PatcherStatus.Starting;

        public event Action<PatcherStatus>? StatusChanged;

        /// <summary>
        /// Small things worth knowing that do not stop a launch (an update that failed, a newer patcher).
        /// </summary>
        public IReadOnlyList<string> Notices => notices;

        private readonly List<string> notices = new List<string>();

        public event Action<string>? NoticeAdded;

        public LazerInstall? Install { get; private set; }

        /// <summary>
        /// What Launch starts: the installed release with its newest build, on <see cref="PatcherStage.Ready"/>.
        /// </summary>
        public VersionFolder? Target { get; private set; }

        /// <summary>
        /// A newer patcher, when one is out.
        /// </summary>
        public PublishedPatcher? NewerPatcher { get; private set; }

        public string DataFolder => Settings.ResolveDataFolder();

        /// <param name="osuData">Where osu! keeps its data, for checking orthomania's is elsewhere (paths only; never opened).</param>
        /// <param name="devServer">Starts the score server with the game when <see cref="PatcherSettings.DevServer"/> says so (development only).</param>
        public PatcherFlow(PatcherLayout layout, ILazerUpdater lazerUpdater, IReleaseFeed feed, IGameRunner runner, PatcherLog log, Version patcherVersion,
                           Func<string?, LazerInstall?>? findLazer = null, IReadOnlyList<string>? osuData = null, IDevServerRunner? devServer = null)
        {
            this.devServer = devServer;
            this.layout = layout;
            this.lazerUpdater = lazerUpdater;
            this.feed = feed;
            this.runner = runner;
            this.log = log;
            this.patcherVersion = patcherVersion;
            this.findLazer = findLazer ?? (root => LazerInstall.Find(root));
            this.osuData = osuData ?? new[] { Data.DataFolder.OsuDataFolder() };

            builds = layout.CreateBuildStore();
            folders = layout.CreateVersionFolders();

            Settings = loadSettings();
            State = loadState();
        }

        /// <summary>
        /// Runs every check, ending on <see cref="PatcherStage.Ready"/>, <see cref="PatcherStage.NotInstalled"/> or
        /// <see cref="PatcherStage.Problem"/>.
        /// </summary>
        public async Task CheckAsync(CancellationToken cancellationToken)
        {
            Target = null;
            notices.Clear();
            Settings = loadSettings();
            State = loadState();

            setStatus(PatcherStatus.Starting);
            log.Add($"orthomania patcher {patcherVersion.ToString(3)} checking");

            try
            {
                await checkAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                log.Add($"check failed: {e}");
                problem(e.Message);
            }
            finally
            {
                State.LastCheckUtc = DateTime.UtcNow;
                saveState();
            }
        }

        private async Task checkAsync(CancellationToken cancellationToken)
        {
            Install = findLazer(Settings.ResolveLazerFolder());

            if (Install == null)
            {
                log.Add($"no lazer at {Settings.ResolveLazerFolder() ?? LazerInstall.DefaultRoot}");
                setStatus(new PatcherStatus(PatcherStage.NotInstalled, "Hi!", "osu!(lazer) is not installed."));
                return;
            }

            log.Add($"lazer {Install.ReadRelease().VersionText} at {Install.Root}");

            var apart = Isolation.Check(layout, Install, DataFolder, osuData);

            if (apart.Count > 0)
            {
                foreach (string p in apart)
                    log.Add($"isolation: {p}");

                problem(apart[0]);
                return;
            }

            layout.CreateFolders();

            await waitForGameToCloseAsync(cancellationToken).ConfigureAwait(false);

            if (Settings.UpdateLazer)
                await updateLazerAsync(cancellationToken).ConfigureAwait(false);

            var release = Install.ReadRelease();

            var build = await getBuildAsync(release.Version, cancellationToken).ConfigureAwait(false);

            await checkPatcherAsync(cancellationToken).ConfigureAwait(false);

            if (build == null)
            {
                log.Add($"no build for {release.Version.Number}");
                problem($"orthomania for osu! {release.Version.Number} is not out yet. Plain osu! works as usual; try again later.");
                return;
            }

            setStatus(new PatcherStatus(PatcherStage.Preparing, "Hi!", "getting osu! ready..."));

            // Lazer may have started while orthomania downloaded.
            await waitForGameToCloseAsync(cancellationToken).ConfigureAwait(false);

            var folder = prepareFolder(build);

            Target = folder;
            prune(folder);

            log.Add($"ready: {folder}");
            setStatus(new PatcherStatus(PatcherStage.Ready, "Ready", $"osu! {folder.Record.Lazer} with orthomania {folder.Record.Build}"));
        }

        private async Task waitForGameToCloseAsync(CancellationToken cancellationToken)
        {
            if (!runner.IsLazerRunning())
                return;

            log.Add("waiting for osu! to close");
            var previous = Status;
            setStatus(new PatcherStatus(PatcherStage.WaitingForGame, "Hi!", "close osu! to continue."));

            while (runner.IsLazerRunning())
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

            setStatus(previous);
        }

        private async Task updateLazerAsync(CancellationToken cancellationToken)
        {
            var install = Install!;
            LazerUpdate? update;

            try
            {
                update = await lazerUpdater.CheckAsync(install, cancellationToken).ConfigureAwait(false);
            }
            catch (LazerUpdateException e)
            {
                log.Add($"lazer update check: {e.Message}");
                addNotice("Could not check for osu! updates. Carrying on with the installed osu!.");
                return;
            }

            if (update == null)
            {
                log.Add("lazer is current");
                return;
            }

            log.Add($"updating {update}");
            setStatus(new PatcherStatus(PatcherStage.UpdatingLazer, "Updating osu!", $"to {update.To.Number}", 0));

            try
            {
                await lazerUpdater.DownloadAsync(update, p => setStatus(Status with { Progress = p }), cancellationToken).ConfigureAwait(false);

                // Nothing may run from the install while its updater replaces it.
                await waitForGameToCloseAsync(cancellationToken).ConfigureAwait(false);

                setStatus(new PatcherStatus(PatcherStage.UpdatingLazer, "Updating osu!", $"installing {update.To.Number}...", null));
                await lazerUpdater.ApplyAsync(update, cancellationToken).ConfigureAwait(false);

                log.Add($"lazer updated to {update.To.Number}");
            }
            catch (LazerUpdateException e)
            {
                log.Add($"lazer update: {e.Message}");
                addNotice($"osu! could not be updated to {update.To.Number}. Opening plain osu! updates it.");
            }
        }

        private async Task<RulesetBuild?> getBuildAsync(LazerVersion lazer, CancellationToken cancellationToken)
        {
            setStatus(new PatcherStatus(PatcherStage.GettingOrthomania, "Getting orthomania", $"built for osu! {lazer.Number}"));

            var have = builds.FindNewest(lazer);

            try
            {
                var published = await feed.FindBuildAsync(lazer, cancellationToken).ConfigureAwait(false);

                if (published == null)
                {
                    log.Add($"no orthomania build published for {lazer.Number}");
                    return have;
                }

                if (have != null && RulesetBuild.CompareBuilds(have.Build, published.Build) >= 0)
                {
                    log.Add($"have {have}, published {published}");
                    return have;
                }

                log.Add($"downloading {published}");

                string staging = Path.Combine(layout.Downloads, $"{published.Lazer.Number}-{published.Build}");

                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);

                await feed.DownloadAsync(published, staging, p => setStatus(new PatcherStatus(PatcherStage.GettingOrthomania, "Getting orthomania",
                    $"built for osu! {lazer.Number}", p)), cancellationToken).ConfigureAwait(false);

                try
                {
                    var added = builds.Add(staging);
                    log.Add($"added {added}");
                    return added;
                }
                finally
                {
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception e) when (e is ReleaseFeedException or InvalidDataException)
            {
                log.Add($"orthomania build: {e.Message}");

                addNotice(have != null
                    ? $"Could not get a newer orthomania ({e.Message}). Using {have.Build}."
                    : $"Could not get orthomania for osu! {lazer.Number} ({e.Message}).");

                return have;
            }
        }

        private async Task checkPatcherAsync(CancellationToken cancellationToken)
        {
            try
            {
                var newest = await feed.FindPatcherAsync(cancellationToken).ConfigureAwait(false);

                if (newest != null && newest.Version > patcherVersion)
                {
                    NewerPatcher = newest;
                    log.Add($"newer patcher: {newest.Version}");
                    addNotice($"A newer orthomania patcher ({newest.Version.ToString(3)}) is out.");
                }
            }
            catch (ReleaseFeedException e)
            {
                log.Add($"patcher check: {e.Message}");
            }
        }

        /// <summary>
        /// The folder for <paramref name="build"/>: kept when whole and current, refreshed when only orthomania's files changed,
        /// built again otherwise.
        /// </summary>
        private VersionFolder prepareFolder(RulesetBuild build)
        {
            var install = Install!;
            var game = new GameSettings(DataFolder, Settings.OsuSignIn);
            var folder = folders.Get(build.Lazer);

            if (folder != null && !VersionFolders.Verify(folder, out string? problem))
            {
                log.Add($"rebuilding {folder}: {problem}");
                folder = null;
            }

            if (folder == null)
            {
                folder = folders.Create(install, build, game);
                log.Add($"created {folder} at {folder.Directory} ({(folder.Record.Linked ? "linked" : "copied")})");

                if (!folder.Record.Linked)
                    addNotice("osu!'s files were copied, not linked, so orthomania's folder takes extra space.");

                return folder;
            }

            if (folder.Record.Build != build.Build || !PathUtils.Normalise(folder.Record.DataFolder).Equals(game.DataFolder, PathUtils.Comparison)
                || folder.Record.OsuSignIn != game.OsuSignIn)
            {
                folder = folders.Refresh(folder, build, game);
                log.Add($"refreshed {folder}");
            }

            return folder;
        }

        /// <summary>
        /// Keeps only the target and its build: no other release may open osu!'s data, and a folder kept for one would also keep that
        /// release's files on disk after lazer's updater removed them.
        /// </summary>
        private void prune(VersionFolder target)
        {
            try
            {
                folders.Prune(new[] { target });

                var keepBuilds = new[] { RulesetBuild.Open(builds.FolderFor(target.Lazer, target.Record.Build)) }.OfType<RulesetBuild>().ToList();
                builds.Prune(keepBuilds);
            }
            catch (IOException e)
            {
                log.Add($"prune: {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                log.Add($"prune: {e.Message}");
            }
        }

        /// <summary>
        /// Starts osu! with orthomania from <see cref="Target"/> and returns its exit code once it closes.
        /// </summary>
        public async Task<int> LaunchAsync(CancellationToken cancellationToken)
        {
            var target = Target ?? throw new InvalidOperationException("Nothing to launch; run the checks first.");

            await waitForGameToCloseAsync(cancellationToken).ConfigureAwait(false);

            if (!VersionFolders.Verify(target, out string? damage))
            {
                problem($"orthomania's copy of osu! changed since the check ({damage}). Try again.");
                return -1;
            }

            var data = new DataFolder(DataFolder);
            data.Prepare();

            State.RecordStarted(target.Lazer, target.Record.Build);
            saveState();

            await using var server = await startDevServerAsync(cancellationToken).ConfigureAwait(false);

            log.Add($"starting {target.GameExecutable} (orthomania data: {data.Path}; osu! sign-in: {(target.Record.OsuSignIn ? "allowed" : "off")})");

            var clock = Stopwatch.StartNew();
            int exitCode;

            try
            {
                exitCode = await runner.RunAsync(target, () => setStatus(new PatcherStatus(PatcherStage.Running, "Have fun!", "osu! is running.")), cancellationToken)
                                       .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.Add($"could not start osu!: {e}");
                problem($"Could not start osu! ({e.Message}).");
                return -1;
            }

            log.Add($"osu! closed with code {exitCode} after {clock.Elapsed:hh\\:mm\\:ss}");

            if (exitCode == 0 || clock.Elapsed >= WORKED_AFTER)
            {
                State.RecordWorking(target.Lazer, target.Record.Build);
                saveState();
            }

            return exitCode;
        }

        /// <summary>
        /// The score server for development, when turned on: one already running is used as it is; one started here stops when the
        /// game closes. A server that does not start never stops the game from starting: the game keeps plays until it can send them.
        /// </summary>
        private async Task<IStartedServer?> startDevServerAsync(CancellationToken cancellationToken)
        {
            var settings = Settings.DevServer;

            if (settings is not { Enabled: true } || devServer == null)
                return null;

            setStatus(new PatcherStatus(PatcherStage.Preparing, "Hi!", "starting the score server..."));

            try
            {
                return await devServer.EnsureRunningAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            catch (DevServerException e)
            {
                log.Add($"score server: {e.Message}");
                addNotice($"The score server did not start: {e.Message}");
                return null;
            }
        }

        public bool DevServerEnabled => Settings.DevServer?.Enabled == true;

        /// <summary>
        /// Turns starting the score server with the game on or off, and saves it.
        /// </summary>
        public void SetDevServerEnabled(bool enabled)
        {
            Settings.DevServer ??= new DevServerSettings();
            Settings.DevServer.Enabled = enabled;

            JsonFile.Write(layout.SettingsFile, Settings);
            log.Add($"starting the score server with the game: {(enabled ? "on" : "off")}");
        }

        /// <summary>
        /// Builds the target's folder again from lazer's install (Options, Repair).
        /// </summary>
        public async Task RepairAsync(CancellationToken cancellationToken)
        {
            foreach (var folder in folders.All())
                folders.Remove(folder);

            log.Add("repair: removed the version folders");
            await CheckAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes everything orthomania made beside lazer (version folders, builds, downloads). orthomania's data folder and the
        /// settings stay; lazer's install and osu!'s data are untouched.
        /// </summary>
        public void RemoveOrthomania()
        {
            foreach (string folder in new[] { layout.LazerFolders, layout.Builds, layout.Downloads })
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }

            Target = null;
            log.Add("removed orthomania's version folders and builds");
        }

        private void problem(string message) => setStatus(new PatcherStatus(PatcherStage.Problem, "Oops", message));

        private void setStatus(PatcherStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }

        private void addNotice(string notice)
        {
            notices.Add(notice);
            NoticeAdded?.Invoke(notice);
        }

        private PatcherSettings loadSettings()
        {
            var settings = JsonFile.ReadOrDefault<PatcherSettings>(layout.SettingsFile, out string? problem);
            if (problem != null)
                log.Add(problem);
            return settings;
        }

        private PatcherState loadState()
        {
            var state = JsonFile.ReadOrDefault<PatcherState>(layout.StateFile, out string? problem);
            if (problem != null)
                log.Add(problem);
            return state;
        }

        private void saveState()
        {
            try
            {
                JsonFile.Write(layout.StateFile, State);
            }
            catch (IOException e)
            {
                log.Add($"could not save state: {e.Message}");
            }
        }
    }
}
