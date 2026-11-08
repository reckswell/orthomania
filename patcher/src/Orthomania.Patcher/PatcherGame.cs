using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using Orthomania.Patcher.Flow;
using Orthomania.Patcher.Graphics;
using Orthomania.Patcher.Installs;
using Orthomania.Patcher.Launch;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Releases;
using Orthomania.Patcher.Screens;
using Orthomania.Patcher.Settings;
using SixLabors.ImageSharp;

namespace Orthomania.Patcher
{
    /// <summary>
    /// The patcher's window: runs <see cref="PatcherFlow"/> and shows where it is.
    /// </summary>
    public partial class PatcherGame : osu.Framework.Game, IPatcherActions, IOptionsActions, IInstallsActions
    {
        public const int WIDTH = 460;
        public const int HEIGHT = 440;

        public const string LAZER_DOWNLOAD = "https://osu.ppy.sh/home/download";

        private readonly PatcherOptions options;
        private readonly CancellationTokenSource exiting = new CancellationTokenSource();

        private PatcherLayout layout = null!;
        private PatcherLog log = null!;
        private PatcherFlow? flow;

        private MainView main = null!;
        private OptionsView optionsView = null!;
        private InstallsView installsView = null!;
        private Task? work;

        private static Version patcherVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);

        /// <summary>
        /// Published by the patcher release workflow (<c>-p:OrthomaniaRelease=true</c>), as opposed to built from a clone: only a release
        /// replaces itself with a newer patcher, so a build being worked on is never swapped for a published one.
        /// </summary>
        public static bool IsRelease => Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
                                                .Any(a => a.Key == "OrthomaniaRelease" && a.Value == "true") == true;

        /// <summary>
        /// The running orthomania.exe, when it is a release that may update itself.
        /// </summary>
        private static PatcherExecutable? releasedExecutable
            => IsRelease && OperatingSystem.IsWindows() && Environment.ProcessPath is string path
               && Path.GetFileName(path).Equals(GitHubReleaseFeed.PATCHER_ASSET, StringComparison.OrdinalIgnoreCase)
                ? new PatcherExecutable(path)
                : null;

        public PatcherGame(PatcherOptions options)
        {
            this.options = options;
        }

        private FrameworkConfigManager frameworkConfig = null!;

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager frameworkConfig)
        {
            this.frameworkConfig = frameworkConfig;

            frameworkConfig.SetValue(FrameworkSetting.WindowMode, WindowMode.Windowed);
            frameworkConfig.SetValue(FrameworkSetting.WindowedSize, new System.Drawing.Size(WIDTH, HEIGHT));
            frameworkConfig.SetValue(FrameworkSetting.FrameSync, FrameSync.VSync);

            layout = PatcherLayout.Default;
            log = new PatcherLog(options.Demo != null ? Path.Combine(Path.GetTempPath(), "orthomania-demo-logs") : layout.Logs);

            var settings = JsonFile.ReadOrDefault<PatcherSettings>(layout.SettingsFile, out _);

            string? resources = options.LazerResources ?? LazerInstall.Find(settings.ResolveLazerFolder())?.ResourcesAssembly;
            PatcherFont.TryAddTorus(this, resources, log.Add);

            if (options.Demo == null)
            {
                var http = GitHubReleaseFeed.CreateHttpClient(patcherVersion);
                var self = releasedExecutable;
                self?.CleanUp(log.Add);

                flow = new PatcherFlow(layout, new VelopackLazerUpdater(log.Add), new GitHubReleaseFeed(http, settings.ReleaseRepository), new ProcessGameRunner(), log,
                    patcherVersion, devServer: new DevServerRunner(log.Add, Path.Combine(layout.Logs, "server.log"), layout.ServerData), self: self);
            }

            int? seed = options.Demo != null ? 7 : null;

            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Palette.BACKGROUND5 },
                new TriangleField
                {
                    ColourLight = Palette.BACKGROUND4,
                    ColourDark = Palette.BACKGROUND6,
                    Density = 14,
                    TriangleScale = 0.8f,
                    Velocity = 0.4f,
                    Seed = seed,
                },
                main = new MainView(this, seed),
                optionsView = new OptionsView(this),
                installsView = new InstallsView(this),
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (Window != null)
            {
                Window.Title = "orthomania";
                Window.Resizable = false;

                // The framework's smallest window is larger than the patcher's.
                Window.MinSize = new System.Drawing.Size(WIDTH, HEIGHT);
                Window.MaxSize = new System.Drawing.Size(WIDTH, HEIGHT);
                frameworkConfig.SetValue(FrameworkSetting.WindowedSize, new System.Drawing.Size(WIDTH, HEIGHT));
            }

            if (options.Demo != null)
                showDemo(options.Demo);
            else
            {
                flow!.StatusChanged += _ => Schedule(refresh);
                flow.NoticeAdded += _ => Schedule(refresh);
                refresh();
                CheckAgain();
            }

            if (options.Screenshot != null)
                Scheduler.AddDelayed(takeScreenshot, 1500);
        }

        private void refresh()
        {
            if (flow == null)
                return;

            main.Show(new PatcherView(flow.Status, flow.Notices));
            optionsView.SetInfo(describe());
            optionsView.SetDevServer(flow.DevServerEnabled);
            installsView.SetInstalls(flow.Found.Installs, flow.Install?.Root);

            if (flow.Status.Stage == PatcherStage.Running)
                Window?.Hide();
            else
                Window?.Show();
        }

        private string describe()
        {
            if (flow == null)
                return string.Empty;

            string lazer = flow.Install != null ? $"osu! {flow.Install.ReadReleaseSafely()} at {flow.Install.Root}" : "osu!(lazer) not found";

            if (flow.Install != null && flow.ChosenLazerFolder != null)
                lazer += " (chosen)";
            return $"{lazer}\ndata: {flow.DataFolder}\npatcher {patcherVersion.ToString(3)}";
        }

        private bool busy => work is { IsCompleted: false };

        private void run(Func<CancellationToken, Task> action)
        {
            if (flow == null || busy)
                return;

            work = Task.Run(async () =>
            {
                try
                {
                    await action(exiting.Token).ConfigureAwait(false);

                    // This patcher was replaced by a newer one, which carries on from here.
                    if (flow.Status.Stage == PatcherStage.RestartingPatcher)
                        Schedule(restartPatcher);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    log.Add($"unexpected: {e}");
                }
            });
        }

        public void Launch() => run(async token =>
        {
            int exitCode = await flow!.LaunchAsync(token).ConfigureAwait(false);

            // The game ran (or failed to start, which leaves a problem on screen to read). A patcher that updated itself first restarts.
            if (flow.Status.Stage is not (PatcherStage.Problem or PatcherStage.RestartingPatcher))
                Schedule(Exit);
            else
                log.Add($"launch ended with {exitCode}");
        });

        /// <summary>
        /// Starts the newer orthomania.exe now in this one's place, which waits for this one to close (only one patcher runs at a time).
        /// </summary>
        private void restartPatcher()
        {
            string path = flow!.UpdatedPatcher!;

            try
            {
                var start = new ProcessStartInfo(path)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(path)!,
                };

                start.ArgumentList.Add(PatcherOptions.AFTER);
                start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

                Process.Start(start)?.Dispose();
                log.Add($"started {path}; closing");
                Exit();
            }
            catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
            {
                log.Add($"could not start the updated patcher: {e.Message}");
                main.Show(new PatcherView(new PatcherStatus(PatcherStage.Problem, "Oops",
                    $"orthomania was updated, but the new one did not start ({e.Message}). Open orthomania again."), flow.Notices));
            }
        }

        public void CheckAgain()
        {
            optionsView.Hide();
            installsView.Hide();
            run(token => flow!.CheckAsync(token));
        }

        public void ShowInstalls()
        {
            if (flow != null)
            {
                installsView.SetInstalls(flow.Found.Installs, flow.Install?.Root);
                installsView.SetMessage(flow.Found.SearchedUtc is DateTime searched
                    ? $"Last searched {searched.ToLocalTime():d MMM, HH:mm}. Pick the osu!(lazer) orthomania runs on."
                    : "Pick the osu!(lazer) orthomania runs on, or search this computer.");
            }

            optionsView.Hide();
            installsView.Show();
        }

        public void HideInstalls() => installsView.Hide();

        public void UseInstall(OsuInstall install)
        {
            if (flow == null || busy)
                return;

            var choice = flow.ChooseFolder(install.Folder);
            installsView.SetMessage(choice.Message);

            if (choice.Install?.Kind == OsuKind.Lazer)
                CheckAgain();
        }

        public void ChooseFolder()
        {
            if (flow == null || busy)
                return;

            string startIn = flow.Install?.Root ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            FolderPicker.Show(Host, startIn, path => Schedule(() =>
            {
                if (path == null || busy)
                    return;

                var choice = flow.ChooseFolder(path);
                installsView.SetMessage(choice.Message);
                installsView.SetInstalls(flow.Found.Installs, flow.Install?.Root);

                if (choice.Install?.Kind == OsuKind.Lazer)
                    CheckAgain();
            }), error => Schedule(() =>
            {
                log.Add($"folder picker: {error}");
                installsView.SetMessage($"The folder picker did not open ({error}). Set lazerFolder in {layout.SettingsFile} instead.");
            }));
        }

        public void SearchAgain()
        {
            if (flow == null || busy)
                return;

            installsView.SetSearching(true);
            installsView.SetMessage("Searching this computer for osu!...");

            run(async token =>
            {
                try
                {
                    var found = await flow.SearchAsync(token).ConfigureAwait(false);
                    int lazer = found.Count(i => i.Kind == OsuKind.Lazer);
                    int stable = found.Count - lazer;

                    Schedule(() =>
                    {
                        installsView.SetMessage($"Found {plural(lazer, "osu!(lazer)", "osu!(lazer) installs")} and {plural(stable, "osu!(stable)", "osu!(stable) installs")}.");
                        installsView.SetInstalls(found, flow.Install?.Root);
                    });

                    // Nothing was found before: use what was found now.
                    if (flow.Install == null && lazer > 0)
                        await flow.CheckAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    Schedule(() => installsView.SetSearching(false));
                }
            });
        }

        private static string plural(int count, string one, string many) => count switch
        {
            0 => $"no {one}",
            1 => $"1 {one}",
            _ => $"{count} {many}",
        };

        public void InstallLazer() => Host.OpenUrlExternally(LAZER_DOWNLOAD);

        public void ShowOptions() => optionsView.Show();

        public void HideOptions() => optionsView.Hide();

        public void OpenNotice(int index)
        {
            if (flow?.NewerPatcher != null)
                Host.OpenUrlExternally(flow.NewerPatcher.Page.ToString());
            else
                OpenLog();
        }

        public void OpenDataFolder()
        {
            if (flow == null)
                return;

            Directory.CreateDirectory(flow.DataFolder);
            Host.OpenFileExternally(flow.DataFolder);
        }

        public void OpenLog() => Host.PresentFileExternally(log.Path);

        public void Repair()
        {
            optionsView.Hide();
            run(token => flow!.RepairAsync(token));
        }

        public void RemoveOrthomania()
        {
            if (flow == null || busy)
                return;

            optionsView.Hide();
            flow.RemoveOrthomania();
            CheckAgain();
        }

        public void ToggleDevServer()
        {
            if (flow == null)
                return;

            flow.SetDevServerEnabled(!flow.DevServerEnabled);
            optionsView.SetDevServer(flow.DevServerEnabled);
        }

        protected override bool OnExiting()
        {
            // Never leave lazer half updated or a version folder half made.
            if (flow?.Status.Stage is PatcherStage.UpdatingLazer or PatcherStage.Preparing && busy)
                return true;

            exiting.Cancel();
            return base.OnExiting();
        }

        private async void takeScreenshot()
        {
            try
            {
                using var image = await Host.TakeScreenshotAsync().ConfigureAwait(false);
                await image.SaveAsPngAsync(options.Screenshot!).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"screenshot failed: {e}");
            }

            Schedule(Exit);
        }

        private void showDemo(string state)
        {
            var notices = new List<string>();

            PatcherStatus status;

            switch (state)
            {
                case "starting":
                    status = PatcherStatus.Starting;
                    break;

                case "waiting":
                    status = new PatcherStatus(PatcherStage.WaitingForGame, "Hi!", "close osu! to continue.");
                    break;

                case "updating":
                    status = new PatcherStatus(PatcherStage.UpdatingLazer, "Updating osu!", "to 2026.1105.0", 0.42);
                    break;

                case "getting":
                    status = new PatcherStatus(PatcherStage.GettingOrthomania, "Getting orthomania", "built for osu! 2026.1105.0", 0.7);
                    break;

                case "notout":
                    status = new PatcherStatus(PatcherStage.Problem, "Oops", "orthomania for osu! 2026.1105.0 is not out yet. Plain osu! works as usual; try again later.");
                    break;

                case "notinstalled":
                    status = new PatcherStatus(PatcherStage.NotInstalled, "Hi!",
                        "Found osu!(stable) at D:\\osu!, but orthomania runs on osu!(lazer), which is not installed.");
                    break;

                case "problem":
                    status = new PatcherStatus(PatcherStage.Problem, "Oops",
                        "orthomania's data folder (C:\\Users\\Alpha\\Documents\\Projects\\osu!\\ortho-data) holds osu!'s own data (client.realm). orthomania now "
                        + "plays on osu!'s data and keeps only its own files there: move osu!'s files out, or choose another folder.");
                    break;

                case "notice":
                    status = new PatcherStatus(PatcherStage.Ready, "Ready", "osu! 2026.1026.0 with orthomania 0.1.0");
                    notices.Add("osu! could not be updated to 2026.1105.0. Opening plain osu! updates it.");
                    notices.Add("A newer orthomania patcher (0.2.0) is out.");
                    break;

                default:
                    status = new PatcherStatus(PatcherStage.Ready, "Ready", "osu! 2026.1105.0 with orthomania 0.2.0");
                    break;
            }

            main.Show(new PatcherView(status, notices));

            if (state == "installs")
            {
                installsView.SetInstalls(new[]
                {
                    new OsuInstall(OsuKind.Lazer, "C:\\Users\\Alpha\\AppData\\Local\\osulazer", "2026.1105.0"),
                    new OsuInstall(OsuKind.Lazer, "D:\\Games\\osulazer", "2026.1026.0"),
                    new OsuInstall(OsuKind.Stable, "D:\\osu!", "20251102.3"),
                }, "C:\\Users\\Alpha\\AppData\\Local\\osulazer");
                installsView.SetMessage("Found 2 osu!(lazer) installs and 1 osu!(stable).");
                installsView.Show();
            }

            if (state == "options")
            {
                optionsView.SetDevServer(true);
                optionsView.SetInfo("osu! 2026.1105.0 at C:\\Users\\Alpha\\AppData\\Local\\osulazer\n"
                                    + "data: C:\\Users\\Alpha\\Documents\\Projects\\osu!\\ortho-data\npatcher 0.1.0");
                optionsView.Show();
            }
        }
    }
}
