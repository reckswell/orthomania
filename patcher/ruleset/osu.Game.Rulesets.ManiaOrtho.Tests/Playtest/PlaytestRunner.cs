// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Handlers.Mouse;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Playtest
{
    /// <summary>
    /// Runs one visual test scene, unattended, in a real window, then exits.
    /// This is how the agent "plays" the game: on a desktop it uses the GPU, in a cloud container it runs under Xvfb
    /// with Mesa's software renderer. Screenshots come from steps inside the scene (see <see cref="TestScenePlaytest"/>).
    /// </summary>
    /// <example>
    /// dotnet run --project osu.Game.Rulesets.ManiaOrtho.Tests -- playtest --beatmap synthetic-7k --ruleset maniaortho --out playtest-output
    /// dotnet run --project osu.Game.Rulesets.ManiaOrtho.Tests -- playtest --scene TestSceneHighwaySettings --method TestSettingsPanel --screenshot out/settings.png
    /// </example>
    public static partial class PlaytestRunner
    {
        public static int Run(string[] args)
        {
            var parsed = parse(args);

            string sceneName = parsed.GetValueOrDefault("scene", nameof(TestScenePlaytest));
            var sceneType = typeof(PlaytestRunner).Assembly.GetTypes()
                                                  .SingleOrDefault(t => typeof(TestScene).IsAssignableFrom(t) && (t.Name == sceneName || t.FullName == sceneName))
                            ?? throw new ArgumentException($"No test scene named {sceneName} in {typeof(PlaytestRunner).Assembly.GetName().Name}.");

            // Catching up after a long seek can take longer than the default 10s step timeout on software renderers.
            // Must be set before osu-framework reads its environment.
            Environment.SetEnvironmentVariable("OSU_TESTS_NO_TIMEOUT", "1");

            // Options reach the scene through the environment so it can also be run from the test browser.
            setEnv(PlaytestOptions.ENV_BEATMAP, parsed.GetValueOrDefault("beatmap"));
            setEnv(PlaytestOptions.ENV_RULESET, parsed.GetValueOrDefault("ruleset"));
            setEnv(PlaytestOptions.ENV_TIMES, parsed.GetValueOrDefault("times"));
            setEnv(PlaytestOptions.ENV_OUT, parsed.GetValueOrDefault("out"));
            setEnv(PlaytestOptions.ENV_SCROLL, parsed.GetValueOrDefault("scroll-speed"));
            setEnv(PlaytestOptions.ENV_FULL, parsed.ContainsKey("play-to-end") ? "1" : null);
            setEnv(PlaytestOptions.ENV_PRESET, parsed.GetValueOrDefault("preset"));
            setEnv(PlaytestOptions.ENV_WALL, parsed.GetValueOrDefault("wall"));
            setEnv(PlaytestOptions.ENV_SPEED_LOCK, parsed.ContainsKey("speed-lock") ? "1" : null);
            setEnv(PlaytestOptions.ENV_SET, parsed.GetValueOrDefault("set"));
            setEnv(PlaytestOptions.ENV_SKIN, parsed.GetValueOrDefault("skin"));
            setEnv(PlaytestOptions.ENV_NOTES, parsed.GetValueOrDefault("notes"));
            setEnv(PlaytestOptions.ENV_MODS, parsed.GetValueOrDefault("mods"));
            setEnv(PlaytestOptions.ENV_PORT, parsed.ContainsKey("port") ? "1" : null);
            setEnv(PlaytestOptions.ENV_HUMANISED, parsed.GetValueOrDefault("humanised"));
            setEnv(PlaytestOptions.ENV_GAME_SET, parsed.GetValueOrDefault("game-set"));

            int[] size = parsed.GetValueOrDefault("size", "1280x720").Split('x').Select(int.Parse).ToArray();

            // Portable: the harness keeps its settings and database next to its build output, inside the clone, never in the
            // user's application data (where the real osu! lives).
            try
            {
                using (var host = Host.GetSuitableDesktopHost(@"ortho-playtest", new HostOptions { FriendlyGameName = "ortho playtest", PortableInstallation = true }))
                {
                    var game = new PlaytestGame(sceneType, parsed.GetValueOrDefault("method"), new Size(size[0], size[1]), parsed.GetValueOrDefault("screenshot"),
                        int.Parse(parsed.GetValueOrDefault("case", "0")));
                    host.Run(game);

                    Console.WriteLine(game.Error == null ? "PLAYTEST PASSED" : $"PLAYTEST FAILED: {game.Error}");
                    return game.Error == null ? 0 : 1;
                }
            }
            finally
            {
                RemovePortableMarker(RuntimeInfo.StartupDirectory);
            }
        }

        /// <summary>
        /// The file osu-framework saves its settings to in a portable game's folder. Its presence alone makes the folder portable
        /// for any host started from it.
        /// </summary>
        public const string PORTABLE_MARKER = "framework.ini";

        /// <summary>
        /// Removes the framework settings a run saved next to the build output. The run is portable by its option; the file
        /// would make every later host started from this folder portable too, NUnit's headless test hosts included: they
        /// would take the build output as their storage and delete it when cleaning up, so the rest of a <c>dotnet test</c>
        /// run fails on missing assemblies. The harness sets its framework settings each run, so nothing is lost.
        /// </summary>
        public static void RemovePortableMarker(string buildOutput)
        {
            string marker = Path.Combine(buildOutput, PORTABLE_MARKER);

            if (File.Exists(marker))
                File.Delete(marker);
        }

        private static void setEnv(string name, string? value)
        {
            if (value != null)
                Environment.SetEnvironmentVariable(name, value);
        }

        private static Dictionary<string, string> parse(string[] args)
        {
            var result = new Dictionary<string, string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Unexpected argument {args[i]}");

                string key = args[i][2..];
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                result[key] = hasValue ? args[++i] : "true";
            }

            return result;
        }

        private partial class PlaytestGame : OsuGameBase
        {
            private readonly Type sceneType;
            private readonly string? methodName;
            private readonly Size windowSize;
            private readonly string? screenshotPath;
            private readonly int caseIndex;

            public string? Error { get; private set; } = "did not complete";

            public PlaytestGame(Type sceneType, string? methodName, Size windowSize, string? screenshotPath, int caseIndex)
            {
                this.caseIndex = caseIndex;
                this.sceneType = sceneType;
                this.methodName = methodName;
                this.windowSize = windowSize;
                this.screenshotPath = screenshotPath;

                // Never talk to osu! servers from the harness.
                API = new DummyAPIAccess();
            }

            [BackgroundDependencyLoader]
            private void load(FrameworkConfigManager frameworkConfig)
            {
                frameworkConfig.SetValue(FrameworkSetting.WindowMode, WindowMode.Windowed);
                frameworkConfig.SetValue(FrameworkSetting.WindowedSize, windowSize);

                // Measure what the renderer can do, not the frame limiter (2x refresh by default).
                frameworkConfig.SetValue(FrameworkSetting.FrameSync, FrameSync.Unlimited);
            }

            // Nothing from osu!'s servers: the beatmap metadata cache (online.db) and online images such as avatars.
            protected override IBeatmapUpdater CreateBeatmapUpdater() => new OfflineBeatmapUpdater();

            protected override OnlineStore CreateOnlineStore() => new OfflineOnlineStore();

            public override void SetHost(GameHost host)
            {
                base.SetHost(host);
                host.Window.CursorState |= CursorState.Hidden;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // A window that did not get focus would otherwise run at 60 fps, which says nothing about the renderer.
                Host.MaximumInactiveHz = 1000;

                // Captures should show the game, not the harness: no mouse (so no hover states or tooltips) and no cursor.
                foreach (var mouse in Host.AvailableInputHandlers.OfType<MouseHandler>())
                    mouse.Enabled.Value = false;

                GlobalCursorDisplay.MenuCursor.Alpha = 0;

                var scene = (TestScene)Activator.CreateInstance(sceneType)!;

                // Hide the test scene's step list by pushing it off the left edge of the window.
                var wrapper = new SceneWrapper(scene);
                Add(wrapper);

                LoadComponentAsync(scene, _ =>
                {
                    wrapper.Add(scene);

                    foreach (var setUp in methodsWith<SetUpAttribute>())
                        scene.AddStep("[SetUp]", () => setUp.Invoke(scene, null));

                    foreach (var setUpSteps in methodsWith<SetUpStepsAttribute>())
                        setUpSteps.Invoke(scene, null);

                    // Tests declared by the scene itself come first, so the inherited TestConstructor is never picked by default.
                    var tests = methodsWith<TestAttribute>().Concat(methodsWith<TestCaseAttribute>())
                                                            .Distinct()
                                                            .Where(m => methodName == null || m.Name == methodName)
                                                            .OrderBy(m => m.DeclaringType == sceneType ? 0 : 1)
                                                            .ToArray();
                    if (tests.Length == 0)
                        throw new ArgumentException($"No [Test] method {methodName} on {sceneType.Name}.");

                    // Parameterised tests run with the [TestCase] chosen by --case (default: the first).
                    var testCases = tests[0].GetCustomAttributes<TestCaseAttribute>(true).ToArray();
                    tests[0].Invoke(scene, testCases.Length == 0 ? null : testCases[caseIndex].Arguments);

                    scene.RunAllSteps(() =>
                    {
                        if (screenshotPath != null && Host.Window != null)
                        {
                            // Let a few frames draw the final state before capturing it.
                            Scheduler.AddDelayed(captureAndExit, 500);
                            return;
                        }

                        Error = null;
                        Exit();
                    }, (step, e) =>
                    {
                        Error = $"step \"{step}\" failed: {e}";
                        Exit();
                    });
                });
            }

            /// <summary>
            /// Leaves beatmaps as they are: the harness never imports any, and osu!'s updater would download its metadata cache.
            /// </summary>
            private sealed class OfflineBeatmapUpdater : IBeatmapUpdater
            {
                public void Queue(Live<BeatmapSetInfo> beatmapSet, MetadataLookupScope lookupScope = MetadataLookupScope.LocalCacheFirst)
                {
                }

                public void Process(BeatmapSetInfo beatmapSet, MetadataLookupScope lookupScope = MetadataLookupScope.LocalCacheFirst)
                {
                }

                public void ProcessObjectCounts(BeatmapInfo beatmapInfo, MetadataLookupScope lookupScope = MetadataLookupScope.LocalCacheFirst)
                {
                }

                public void Dispose()
                {
                }
            }

            /// <summary>
            /// Looks nothing up online (avatars, covers): those images stay blank.
            /// </summary>
            private sealed class OfflineOnlineStore : OnlineStore
            {
                protected override string GetLookupUrl(string url) => string.Empty;
            }

            private void captureAndExit()
            {
                Host.TakeScreenshotAsync().ContinueWith(task =>
                {
                    try
                    {
                        using (var image = task.GetResultSafely())
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath!))!);
                            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, screenshotPath!);
                        }

                        Error = null;
                    }
                    catch (Exception e)
                    {
                        Error = $"screenshot failed: {e}";
                    }

                    Schedule(Exit);
                });
            }

            private IEnumerable<MethodInfo> methodsWith<T>() where T : Attribute
                => sceneType.GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(m => m.GetCustomAttributes<T>(true).Any());

            private partial class SceneWrapper : Container
            {
                // Matches TestScene's internal steps panel width.
                private const float steps_width = 180;

                private readonly TestScene scene;

                public SceneWrapper(TestScene scene)
                {
                    this.scene = scene;
                    RelativeSizeAxes = Axes.Both;
                }

                protected override void Update()
                {
                    base.Update();

                    scene.RelativeSizeAxes = Axes.Y;
                    scene.Width = DrawWidth + steps_width;
                    scene.X = -steps_width;
                }
            }
        }
    }
}
