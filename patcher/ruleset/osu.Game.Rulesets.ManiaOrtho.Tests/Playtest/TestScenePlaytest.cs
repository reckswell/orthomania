// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using SixLabors.ImageSharp;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Playtest
{
    /// <summary>
    /// Plays a beatmap with autoplay in a real window, freezing at chosen times to save screenshots, and writes a
    /// summary.json with frame rate and (optionally) final judgement statistics.
    /// Configure with environment variables (<see cref="PlaytestOptions"/>), or run via scripts/ortho/playtest.sh.
    /// When no window exists (headless <c>dotnet test</c>) it still plays, which makes it a smoke test.
    /// </summary>
    public partial class TestScenePlaytest : PlayerTestScene
    {
        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions { WriteIndented = true };

        private readonly PlaytestOptions options = PlaytestOptions.FromEnvironment();

        private HighwayPreset? playedPreset;

        private readonly List<string> captures = new List<string>();
        private readonly List<object> clockAtCapture = new List<object>();
        private readonly List<double> fpsSamples = new List<double>();

        /// <summary>
        /// Draw frame times (ms) while gameplay runs, one per draw frame seen from the update thread.
        /// </summary>
        private readonly List<double> frameTimes = new List<double>();

        /// <summary>
        /// Frames are measured only after the gameplay clock has run smoothly for <see cref="settle_time"/> (real time):
        /// a seek or a capture pause makes the playfield catch up, which is the harness's doing, not gameplay's.
        /// </summary>
        private const double settle_time = 500;

        private double measureFrom;
        private double lastMeasuredGameplayTime = double.NaN;
        private double lastDrawFrame = double.NaN;
        private Task? captureTask;

        /// <summary>
        /// How far the clock ran past the current capture point before it was stopped and sought back (0 when it was
        /// stopped short and moved forward onto it).
        /// </summary>
        private double overshoot;

        /// <summary>
        /// How far the clock was moved forward onto the current capture point.
        /// </summary>
        private double jumped;

        /// <summary>
        /// The gameplay clock's time on the previous frame while approaching a capture point, to measure how far a frame moves it.
        /// </summary>
        private double? lastClockTime;

        /// <summary>
        /// How far a frame is assumed to move the clock before one has been measured (a slow renderer's frame).
        /// </summary>
        private const double default_frame_step = 100;

        /// <summary>
        /// A capture point at most this far after the previous one is reached by seeking forward from it, without playing.
        /// </summary>
        private const double close_capture = 500;

        private double[] times = Array.Empty<double>();

        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        protected override bool Autoplay => options.HumanisedSeed == null;

        protected override TestPlayer CreatePlayer(Ruleset ruleset)
        {
            var player = base.CreatePlayer(ruleset);

            if (options.HumanisedSeed is int seed)
            {
                // Attached before gameplay starts, so every note is played by it.
                var replay = HumanisedReplayGenerator.Generate(PlaytestBeatmaps.Load(options.Beatmap), seed);
                player.OnLoadComplete += _ => player.DrawableRuleset.SetReplayScore(new Score { Replay = replay, ScoreInfo = new ScoreInfo { Ruleset = ruleset.RulesetInfo } });
            }

            return player;
        }

        // The player is loaded below, with the chosen mods.
        protected override bool HasCustomSteps => true;

        protected override Ruleset CreatePlayerRuleset() => options.CreateRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => PlaytestBeatmaps.Load(options.Beatmap);

        /// <summary>
        /// The skin imported from <see cref="PlaytestOptions.Skin"/> when it is a folder.
        /// </summary>
        private Task<Live<SkinInfo>>? importedSkin;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            if (options.SkinIsFolder)
            {
                // Imported into the test game's own storage; the folder is only read.
                AddStep("import skin folder", () => importedSkin ??= skins.Import(new ImportTask(options.Skin)));
                AddUntilStep("skin imported", () => importedSkin?.IsCompleted == true);
            }

            AddStep("apply ruleset settings", () =>
            {
                // The local user's avatar would be fetched from osu!'s servers (a.ppy.sh); an empty address loads none.
                if (API is DummyAPIAccess dummyApi)
                {
                    dummyApi.LocalUser.Value = new APIUser
                    {
                        Username = @"Local user",
                        Id = DummyAPIAccess.DUMMY_USER_ID,
                        AvatarUrl = string.Empty,
                    };
                }

                skins.CurrentSkinInfo.Value = options.SkinIsFolder
                    ? importedSkin?.GetResultSafely() ?? throw new InvalidOperationException($"Could not import the skin at {options.Skin}.")
                    : options.CreateSkinInfo().ToLiveUnmanaged();

                var config = (ManiaRulesetConfigManager)RulesetConfigs.GetConfigFor(CreatePlayerRuleset()).AsNonNull();

                if (options.ScrollSpeed is double speed)
                    config.SetValue(ManiaRulesetSetting.ScrollSpeed, speed);

                // osu!'s settings start fresh in each run's storage; the ruleset's are kept between runs, so a --game-set from an
                // earlier run is reset first.
                PlaytestOptions.ResetRulesetValues(config);
                options.ApplyGameValues(LocalConfig, config);

                if (config is ManiaOrthoRulesetConfigManager ortho)
                {
                    // Before the preset, which sets its game's width and numbers (a game without a wall keeps the default wall
                    // scale), and before the speed toggle below, so the scale cannot change it. A scale given with --set sets it
                    // as that game would; --speed-lock still wins (applied again after the --set values).
                    ortho.Highway.GetBindable<WallScale>(HighwaySetting.WallScale).SetDefault();
                    ortho.Highway.GetBindable<SpeedScale>(HighwaySetting.SpeedScale).SetDefault();
                    ortho.Highway.GetBindable<HighwayWidthMode>(HighwaySetting.WidthMode).SetDefault();
                    ortho.Highway.GetBindable<float>(HighwaySetting.LaneWidth).SetDefault();

                    // A game with a wall leaves it to the player, so an earlier run's --wall would stay.
                    ortho.Highway.SetValue(HighwaySetting.Wall, 0f);

                    // Set the preset twice so its values are re-applied even if it is already selected.
                    ortho.Highway.ForgetGames(); // each game remembers its settings; start from its own
                    ortho.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                    ortho.Highway.SetValue(HighwaySetting.Preset, options.Preset);

                    if (options.Wall is float wall)
                        ortho.Highway.SetValue(HighwaySetting.Wall, wall);

                    ortho.Highway.SetValue(HighwaySetting.WallKeepsVisibleTime, !options.SpeedLock);
                    ortho.Highway.SetValue(HighwaySetting.NoteStyle, options.Port ? HighwayNoteStyle.Ported : options.NoteStyle);
                    ortho.Highway.SetValue(HighwaySetting.HitEffects, options.Port ? HighwayHitEffectStyle.Ported : HighwayHitEffectStyle.Highway);
                    ortho.Highway.SetValue(HighwaySetting.Track, options.Port ? HighwayTrackStyle.Ported : HighwayTrackStyle.Highway);

                    // Preferences outside the presets start at their defaults, so a --set from an earlier run is not kept.
                    ortho.Highway.GetBindable<bool>(HighwaySetting.LaneGuides).SetDefault();
                    ortho.Highway.GetBindable<bool>(HighwaySetting.SkinHitTarget).SetDefault();
                    ortho.Highway.GetBindable<SkinKeyArea>(HighwaySetting.KeyArea).SetDefault();
                    ortho.Highway.GetBindable<SkinStageFit>(HighwaySetting.StageFit).SetDefault();
                    ortho.Highway.GetBindable<float>(HighwaySetting.SideStagePull).SetDefault();
                    ortho.Highway.GetBindable<float>(HighwaySetting.SideStageLean).SetDefault();
                    ortho.Highway.GetBindable<bool>(HighwaySetting.SkinNoteProportions).SetDefault();
                    ortho.Highway.GetBindable<float>(HighwaySetting.FarFade).SetDefault();
                    ortho.Highway.GetBindable<float>(HighwaySetting.HoldOpacity).SetDefault();
                    ortho.Highway.GetBindable<float>(HighwaySetting.TrackOpacity).SetDefault();
                    ortho.Highway.GetBindable<KeyBeamLength>(HighwaySetting.KeyBeam).SetDefault();
                    ortho.Highway.GetBindable<float>(HighwaySetting.KiaiSway).SetDefault();
                    ortho.Highway.GetBindable<float>(HighwaySetting.KiaiLift).SetDefault();

                    options.ApplyHighwayValues(ortho.Highway);

                    if (options.SpeedLock)
                        ortho.Highway.SetValue(HighwaySetting.WallKeepsVisibleTime, false);

                    playedPreset = ortho.Highway.Get<HighwayPreset>(HighwaySetting.Preset);
                }
            });

            base.SetUpSteps();

            AddStep("load player", () => LoadPlayer(options.CreateMods(CreatePlayerRuleset())));
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        }

        [Test]
        public void TestCapture()
        {
            AddStep("choose capture times", () =>
            {
                times = options.Times.Length > 0 ? options.Times : defaultTimes(Player.GameplayState.Beatmap);
            });

            AddUntilStep("gameplay running", () => Player.GameplayClockContainer.IsRunning);

            for (int i = 0; i < 8; i++)
            {
                int index = i;

                // Steps are created up-front, so allow up to 8 capture points and skip unused ones.
                AddStep($"advance to capture point {index}", () =>
                {
                    if (index >= times.Length)
                        return;

                    double target = times[index];
                    var clock = Player.GameplayClockContainer;

                    // Skip ahead when far away, leaving 1.5s of real play so judgements and hit effects are on screen.
                    if (clock.CurrentTime < target - 1500)
                        clock.Seek(target - 1500);

                    lastClockTime = null;

                    // Unless the previous capture already moved the clock onto this one.
                    if (Math.Abs(clock.CurrentTime - target) > 1)
                        jumped = 0;
                });
                // Wait on the frame-stable clock, which is what the playfield draws from. After a long seek it catches up
                // a limited number of frames per update, so it can lag the gameplay clock by tens of seconds on slow renderers.
                AddUntilStep($"reach capture point {index}", () =>
                {
                    if (index >= times.Length)
                        return true;

                    var clock = Player.GameplayClockContainer;
                    double target = times[index];

                    if (clock.IsRunning)
                    {
                        double current = clock.CurrentTime;
                        double step = lastClockTime is double last && current > last ? current - last : default_frame_step;
                        lastClockTime = current;

                        // Stop short of the target when the next frame would pass it, and move forward onto it. A forward
                        // seek keeps every hit effect and judgement; running past and seeking back drops the pooled ones
                        // that ended in between (they do not come back), and a slow renderer's frame is 100 ms or more.
                        if (current + step >= target)
                        {
                            clock.Stop();
                            jumped = Math.Max(0, target - current);
                            clock.Seek(target);
                        }
                    }

                    return Player.DrawableRuleset.FrameStableClock.CurrentTime >= target;
                });
                AddStep($"freeze for capture {index}", () =>
                {
                    if (index >= times.Length)
                        return;

                    // Stop, then seek back any overshoot so every ruleset is captured at exactly the same time.
                    Player.GameplayClockContainer.Stop();
                    overshoot = Player.DrawableRuleset.FrameStableClock.CurrentTime - times[index];
                    Player.GameplayClockContainer.Seek(times[index]);
                });
                AddUntilStep($"frame-stable clock at capture point {index}",
                    () => index >= times.Length || Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - times[index]) < 1);
                AddWaitStep("let the frame settle", 2);
                AddStep($"capture {index}", () =>
                {
                    if (index >= times.Length)
                    {
                        captureTask = Task.CompletedTask;
                        return;
                    }

                    double timeRange = (Player.DrawableRuleset as IDrawableScrollingRuleset)?.ScrollingInfo.TimeRange.Value ?? 0;

                    clockAtCapture.Add(new
                    {
                        target = times[index],
                        gameplay = Math.Round(Player.GameplayClockContainer.CurrentTime, 1),
                        frameStable = Math.Round(Player.DrawableRuleset.FrameStableClock.CurrentTime, 1),
                        // How far past the target the clock ran (rewound before the capture: effects that ended in
                        // between are missing), and how far it was moved forward onto it.
                        overshoot = Math.Round(overshoot, 1),
                        jumped = Math.Round(jumped, 1),
                        combo = Player.ScoreProcessor.Combo.Value,
                        judged = Player.ScoreProcessor.JudgedHits,
                        // How long a note takes from the top of mania's stage to the line, and (maniaortho) from the far edge or
                        // wall to the line: the time range, shorter with the speed lock.
                        timeRange = Math.Round(timeRange, 1),
                        visibleTime = (Player.DrawableRuleset as DrawableManiaOrthoRuleset)?.Highway?.Warp.Scroll.VisibleTime(timeRange) is double visible
                            ? Math.Round(visible, 1)
                            : (double?)null,
                    });
                    captureTask = capture($"{options.Ruleset}-{index}-{times[index]:0}ms");
                });
                AddUntilStep($"capture {index} written", () => captureTask?.IsCompleted == true);
                AddStep($"resume after capture {index}", () =>
                {
                    if (captureTask?.IsFaulted == true)
                        throw captureTask.Exception!;

                    if (index >= times.Length)
                        return;

                    var clock = Player.GameplayClockContainer;

                    // A capture point close behind this one: move forward onto it instead of playing, which would run past
                    // it before the next step could stop the clock.
                    if (index + 1 < times.Length && times[index + 1] - clock.CurrentTime <= close_capture)
                    {
                        jumped = times[index + 1] - clock.CurrentTime;
                        clock.Seek(times[index + 1]);
                        return;
                    }

                    lastClockTime = null;
                    clock.Start();
                });
            }

            if (options.PlayToEnd)
                AddUntilStep("play to the end", () => Player.GameplayState.HasCompleted);

            AddStep("write summary.json", writeSummary);
        }

        protected override void Update()
        {
            base.Update();

            if (Player?.GameplayClockContainer?.IsRunning != true)
            {
                lastMeasuredGameplayTime = double.NaN;
                return;
            }

            // A jump in gameplay time (a seek), or a restart after a pause, starts a new settling period.
            double gameplayTime = Player.GameplayClockContainer.CurrentTime;
            if (double.IsNaN(lastMeasuredGameplayTime) || Math.Abs(gameplayTime - lastMeasuredGameplayTime) > 250)
                measureFrom = Time.Current + settle_time;
            lastMeasuredGameplayTime = gameplayTime;

            var drawClock = host.DrawThread.Clock;
            bool newDrawFrame = drawClock.CurrentTime != lastDrawFrame;
            lastDrawFrame = drawClock.CurrentTime;

            if (Time.Current < measureFrom)
                return;

            fpsSamples.Add(drawClock.FramesPerSecond);

            if (newDrawFrame)
                frameTimes.Add(drawClock.ElapsedFrameTime);
        }

        private static double percentile(double[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * p))];

        private static double[] defaultTimes(IBeatmap beatmap)
        {
            double start = beatmap.HitObjects.First().StartTime;
            double end = beatmap.HitObjects.Max(h => h.GetEndTime());
            return new[] { 0.2, 0.45, 0.7, 0.9 }.Select(f => Math.Round(start + (end - start) * f)).ToArray();
        }

        private Task capture(string name)
        {
            // No window means no real renderer (headless test run): nothing to capture.
            if (options.OutputDirectory == null || host.Window == null)
                return Task.CompletedTask;

            Directory.CreateDirectory(options.OutputDirectory);
            string path = Path.Combine(options.OutputDirectory, name + ".png");

            return Task.Run(async () =>
            {
                using (var image = await host.TakeScreenshotAsync().ConfigureAwait(false))
                    await image.SaveAsPngAsync(path).ConfigureAwait(false);

                lock (captures)
                    captures.Add(path);
            });
        }

        private void writeSummary()
        {
            if (options.OutputDirectory == null)
                return;

            var beatmap = Player.GameplayState.Beatmap;
            var scoreProcessor = Player.ScoreProcessor;
            double[] sortedFps = fpsSamples.OrderBy(f => f).ToArray();
            double[] sortedFrameTimes = frameTimes.OrderBy(f => f).ToArray();

            var summary = new
            {
                ruleset = options.Ruleset,
                drawableRuleset = Player.DrawableRuleset.GetType().Name,
                beatmap = options.Beatmap,
                columns = (beatmap as ManiaBeatmap)?.TotalColumns,
                hitObjects = beatmap.HitObjects.Count,
                window = host.Window == null ? "headless" : $"{host.Window.ClientSize.Width}x{host.Window.ClientSize.Height}",
                renderer = host.ResolvedRenderer.ToString(),
                scrollSpeed = options.ScrollSpeed,
                // The preset played, after the --set values: Custom when one of them changed its look.
                preset = options.Ruleset == ManiaOrthoRuleset.SHORT_NAME ? playedPreset?.ToString() : null,
                wall = options.Wall,
                speedLock = options.Ruleset == ManiaOrthoRuleset.SHORT_NAME ? options.SpeedLock : (bool?)null,
                // What the highway played with, after the preset, wall and --set values (out-of-range values are clamped).
                highway = (Player.DrawableRuleset as DrawableManiaOrthoRuleset)?.Highway?.Settings is HighwaySettings used
                    ? new
                    {
                        camera = used.CameraMode.ToString(),
                        used.LaneTilt,
                        used.LaneLength,
                        used.Approach,
                        // Where the camera puts the lane's far end, and how wide it is there.
                        farY = used.ToLayout().FarY,
                        farWidthRatio = used.ToLayout().FarWidthRatio,
                        used.JudgementLineHeight,
                        used.TrackWidth,
                        used.Wall,
                        used.WallKeepsVisibleTime,
                        used.NoteThickness,
                        notes = used.NoteStyle.ToString(),
                        hitEffects = used.HitEffects.ToString(),
                        track = used.Track.ToString(),
                        keyArea = used.KeyArea.ToString(),
                        stageFit = used.StageFit.ToString(),
                        used.SideStagePull,
                        used.SideStageLean,
                        used.SkinNoteProportions,
                        used.FarFade,
                        used.HoldOpacity,
                        used.TrackOpacity,
                        keyBeam = used.KeyBeam.ToString(),
                        used.KiaiSway,
                        used.KiaiLift,
                        wallScale = used.WallScale.ToString(),
                        speedScale = used.SpeedScale.ToString(),
                        widthMode = used.WidthMode.ToString(),
                        used.LaneWidth,
                        ported = (Player.DrawableRuleset as DrawableManiaOrthoRuleset)?.Highway?.Port != null,
                    }
                    : null,
                skin = options.Skin,
                notes = options.Ruleset == ManiaOrthoRuleset.SHORT_NAME ? (options.Port ? HighwayNoteStyle.Ported : options.NoteStyle).ToString() : null,
                mods = Player.GameplayState.Mods.Select(m => m.Acronym).ToArray(),
                replay = options.HumanisedSeed is int humanisedSeed ? $"humanised {humanisedSeed}" : "autoplay",
                // osu!'s and osu!mania's own settings given with --game-set, as given (both rulesets).
                game = options.GameValues,
                captureTimes = times,
                captures = captures.Select(Path.GetFileName).ToArray(),
                clockAtCapture,
                fps = sortedFps.Length == 0
                    ? null
                    : new
                    {
                        mean = Math.Round(sortedFps.Average(), 1),
                        p5 = Math.Round(sortedFps[(int)(sortedFps.Length * 0.05)], 1),
                        min = Math.Round(sortedFps[0], 1),
                        samples = sortedFps.Length,
                    },
                // Draw frame times in ms: the long frames are what a player feels as stutter.
                frameTimeMs = sortedFrameTimes.Length == 0
                    ? null
                    : new
                    {
                        p50 = Math.Round(percentile(sortedFrameTimes, 0.5), 2),
                        p95 = Math.Round(percentile(sortedFrameTimes, 0.95), 2),
                        p99 = Math.Round(percentile(sortedFrameTimes, 0.99), 2),
                        max = Math.Round(sortedFrameTimes[^1], 2),
                        samples = sortedFrameTimes.Length,
                    },
                frameLimits = $"draw {host.MaximumDrawHz} Hz, update {host.MaximumUpdateHz} Hz, inactive {host.MaximumInactiveHz} Hz",
                windowActive = host.IsActive.Value,
                completed = Player.GameplayState.HasCompleted,
                statistics = scoreProcessor.Statistics.Where(kvp => kvp.Value > 0).ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                accuracy = Math.Round(scoreProcessor.Accuracy.Value, 6),
                totalScore = scoreProcessor.TotalScore.Value,
                maxCombo = scoreProcessor.HighestCombo.Value,
            };

            Directory.CreateDirectory(options.OutputDirectory);
            File.WriteAllText(Path.Combine(options.OutputDirectory, $"summary-{options.Ruleset}.json"),
                JsonSerializer.Serialize(summary, json_options));
        }
    }
}
