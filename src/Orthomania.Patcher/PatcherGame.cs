using System;
using System.Collections.Generic;
using System.IO;
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
    public partial class PatcherGame : osu.Framework.Game, IPatcherActions, IOptionsActions
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
        private Task? work;

        private static Version patcherVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);

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

                flow = new PatcherFlow(layout, new VelopackLazerUpdater(log.Add), new GitHubReleaseFeed(http, settings.ReleaseRepository), new ProcessGameRunner(), log,
                    patcherVersion);
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

            main.Show(new PatcherView(flow.Status, flow.Notices, flow.Target?.Record.Lazer));
            optionsView.SetInfo(describe());

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

            // The game ran (or failed to start, which leaves a problem on screen to read).
            if (flow.Status.Stage != PatcherStage.Problem)
                Schedule(Exit);
            else
                log.Add($"launch ended with {exitCode}");
        });

        public void CheckAgain()
        {
            optionsView.Hide();
            run(token => flow!.CheckAsync(token));
        }

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
            string? fallback = null;

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

                case "fallback":
                    status = new PatcherStatus(PatcherStage.Fallback, "Almost", "orthomania for osu! 2026.1105.0 is not out yet.");
                    fallback = "2026.1026.0";
                    break;

                case "notinstalled":
                    status = new PatcherStatus(PatcherStage.NotInstalled, "Hi!", "osu!(lazer) is not installed.");
                    break;

                case "problem":
                    status = new PatcherStatus(PatcherStage.Problem, "Oops",
                        "orthomania's data folder (C:\\Users\\Alpha\\AppData\\Roaming\\osu) is plain osu!'s data. Choose a separate one, so each keeps its own.");
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

            main.Show(new PatcherView(status, notices, fallback));

            if (state == "options")
            {
                optionsView.SetInfo("osu! 2026.1105.0 at C:\\Users\\Alpha\\AppData\\Local\\osulazer\n"
                                    + "data: C:\\Users\\Alpha\\Documents\\Projects\\osu!\\ortho-data\npatcher 0.1.0");
                optionsView.Show();
            }
        }
    }
}
