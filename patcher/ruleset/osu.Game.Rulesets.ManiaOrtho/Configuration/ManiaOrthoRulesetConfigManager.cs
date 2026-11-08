// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using osu.Framework.Bindables;
using osu.Framework.Configuration.Tracking;
using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.ScoreServer;

namespace osu.Game.Rulesets.ManiaOrtho.Configuration
{
    /// <summary>
    /// This ruleset's settings: osu!mania's own (scroll speed, note colouring), which <c>DrawableManiaRuleset</c> reads
    /// from this type, plus the highway settings in <see cref="Highway"/> and the score server's in <see cref="ScoreServer"/>.
    /// </summary>
    /// <remarks>
    /// Highway settings need their own enum, and a <see cref="RulesetConfigManager{TLookup}"/> has exactly one, so they live
    /// in a second manager stored under the same ruleset (and the score server's in a third). Keys are enum member names and
    /// the enums share none.
    /// None of these settings affect judgement or scoring.
    /// </remarks>
    public class ManiaOrthoRulesetConfigManager : ManiaRulesetConfigManager
    {
        public HighwayConfigManager Highway { get; }

        public ScoreServerConfigManager ScoreServer { get; }

        private readonly OwnSettings? own;

        public ManiaOrthoRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
            : base(OwnSettings.StoreFor(settings), ruleset, variant)
        {
            own = OwnSettings.Load("mania", variant, ConfigStore);

            // As mania does once its stored values are in.
            if (own != null)
                Migrate();

            Highway = new HighwayConfigManager(settings, ruleset, variant);
            ScoreServer = new ScoreServerConfigManager(settings, ruleset, variant);

            keepFineScrollSpeed();

            // Each game remembers its own speed (Alpha 2026-11-08), as it remembers the rest of its settings.
            Highway.RememberScrollSpeed(GetOriginalBindable<double>(ManiaRulesetSetting.ScrollSpeed));

            // The camera defines the direction (SPEC 3): notes always come down the track, whatever was stored.
            SetValue(ManiaRulesetSetting.ScrollDirection, ManiaScrollingDirection.Down);
            GetOriginalBindable<ManiaScrollingDirection>(ManiaRulesetSetting.ScrollDirection).Disabled = true;
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();

            // Finer and up to faster than osu!mania's (0.1, 1-40), so every speed scale's numbers land on their own speeds
            // (SpeedScale). Display only: the time range is all a speed changes.
            var speed = (BindableDouble)GetOriginalBindable<double>(ManiaRulesetSetting.ScrollSpeed);
            speed.MaxValue = SpeedScales.MAX_MANIA_SPEED;
            speed.Precision = SpeedScales.MANIA_SPEED_PRECISION;
        }

        /// <summary>
        /// Brings back the stored speed at maniaortho's precision and range, and keeps a copy of it from now on.
        /// </summary>
        /// <remarks>
        /// osu!mania's defaults read the stored speed into its own limits (0.1, up to 40) before <see cref="InitialiseDefaults"/>
        /// widens them, so the finer, faster speed would be lost on every start. The copy is used only while osu!mania's limits
        /// would read it as the stored speed, so a speed stored without one is kept.
        /// </remarks>
        private void keepFineScrollSpeed()
        {
            var speed = GetOriginalBindable<double>(ManiaRulesetSetting.ScrollSpeed);
            double fine = Highway.Get<double>(HighwaySetting.FineScrollSpeed);

            // Within one of osu!mania's 0.1 steps: its rounding of a half step may go either way.
            if (fine >= 1 && Math.Abs(Math.Min(fine, mania_max_speed) - speed.Value) < 0.1 + 1e-6)
                speed.Value = fine;

            speed.BindValueChanged(e => Highway.SetValue(HighwaySetting.FineScrollSpeed, e.NewValue), true);
        }

        /// <summary>
        /// osu!mania's own fastest speed.
        /// </summary>
        private const double mania_max_speed = 40;

        /// <summary>
        /// None: mania's scroll speed toast shows <c>11485 / speed</c>, not the time notes are visible on the highway. A play's
        /// <see cref="UI.HighwayToasts"/> shows the speed and wall toasts instead, with the real visible time.
        /// </summary>
        public override TrackedSettings CreateTrackedSettings() => new TrackedSettings();

        protected override bool PerformSave()
        {
            base.PerformSave();
            own?.Save(ConfigStore);
            return true;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (isDisposing)
            {
                Highway.Dispose();
                ScoreServer.Dispose();
            }
        }
    }

    public class HighwayConfigManager : RulesetConfigManager<HighwaySetting>
    {
        private bool applyingPreset;

        private readonly OwnSettings? own;

        private readonly Bindable<HighwayPreset> preset;

        /// <summary>
        /// What each game remembers (<see cref="HighwaySetting.GameMemory"/>), read again whenever the stored string changes.
        /// </summary>
        private readonly Bindable<string> storedMemory;

        private GameMemory memory;

        /// <summary>
        /// osu!mania's scroll speed, which each game remembers too once <see cref="RememberScrollSpeed"/> gives it.
        /// </summary>
        private Bindable<double>? scrollSpeed;

        private const string scroll_speed_key = "ScrollSpeed";

        /// <summary>
        /// The settings under "Game preset", which each game remembers (Alpha 2026-11-08). The game's look and numbers start at
        /// the game's own values and keep the player's changes on top; the wall, whether it keeps the visible time and the
        /// speed (<see cref="RememberScrollSpeed"/>) are the player's alone: a game chosen for the first time starts from the
        /// current ones. A game without a wall (<see cref="HighwayPresets.HasWall"/>) keeps the wall down and remembers none
        /// of the wall's settings.
        /// </summary>
        private static readonly GameSetting[] game_settings =
        {
            new GameSetting<HighwayCameraMode>(HighwaySetting.CameraMode, g => g.CameraMode),
            new GameSetting<float>(HighwaySetting.LaneTilt, g => g.LaneTilt),
            new GameSetting<float>(HighwaySetting.LaneLength, g => g.LaneLength),
            new GameSetting<float>(HighwaySetting.Approach, g => g.Approach),
            new GameSetting<float>(HighwaySetting.JudgementLineHeight, g => g.JudgementLineHeight),
            new GameSetting<HighwayWidthMode>(HighwaySetting.WidthMode, g => g.WidthMode),
            new GameSetting<float>(HighwaySetting.TrackWidth, g => g.TrackWidth),
            new GameSetting<float>(HighwaySetting.LaneWidth, g => g.LaneWidth),
            new GameSetting<float>(HighwaySetting.NoteThickness, g => g.NoteThickness),
            new GameSetting<SpeedScale>(HighwaySetting.SpeedScale, g => g.SpeedScale),
            new GameSetting<WallScale>(HighwaySetting.WallScale, g => g.WallScale, true),
            new GameSetting<float>(HighwaySetting.Wall, null, true),
            new GameSetting<bool>(HighwaySetting.WallKeepsVisibleTime, null, true),
        };

        public HighwayConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
            : base(OwnSettings.StoreFor(settings), ruleset, variant)
        {
            own = OwnSettings.Load("highway", variant, ConfigStore);

            preset = GetOriginalBindable<HighwayPreset>(HighwaySetting.Preset);
            storedMemory = GetOriginalBindable<string>(HighwaySetting.GameMemory);
            memory = GameMemory.Parse(storedMemory.Value);
            storedMemory.ValueChanged += e => memory = GameMemory.Parse(e.NewValue);

            keepWidthByLane();
            keepCustomCameraAsTilt();
            ApplySkinPortDefaults();
            applySkinLayoutDefault();
            startGameMemoryOnce();

            // A game always shows its own values under the player's changes, so tuning a game in code reaches everyone who
            // left that value alone.
            applyGame(preset.Value, false);

            preset.ValueChanged += e => switchGame(e.OldValue, e.NewValue);

            foreach (var setting in game_settings)
                setting.Watch(this, onGameSettingChanged);

            // Choosing a game's wall scale also gives the wall that game's speed behaviour (Chunithm and osu!mania keep the
            // speed, IIDX the visible time); the toggle can still be changed afterwards.
            GetOriginalBindable<WallScale>(HighwaySetting.WallScale).ValueChanged += e =>
            {
                if (!applyingPreset && WallScales.KeepsVisibleTime(e.NewValue) is bool keepsVisibleTime)
                    SetValue(HighwaySetting.WallKeepsVisibleTime, keepsVisibleTime);
            };
        }

        /// <summary>
        /// Makes each game remember osu!mania's scroll speed too (<see cref="ManiaOrthoRulesetConfigManager"/> owns it). The
        /// game in use keeps the speed it has now, if it remembers none yet.
        /// </summary>
        public void RememberScrollSpeed(Bindable<double> speed)
        {
            scrollSpeed = speed;
            speed.ValueChanged += _ => onGameSettingChanged();

            if (memory.Get(preset.Value, scroll_speed_key) == null)
                rememberAll(preset.Value);
        }

        /// <summary>
        /// Forgets what every game remembers, and shows the current game's own values again; the speed and the wall stay.
        /// </summary>
        public void ForgetGames()
        {
            storedMemory.Value = string.Empty;
            applyGame(preset.Value, false);
            rememberAll(preset.Value);
        }

        /// <summary>
        /// Whether the current game remembers a change of the player's to its look or numbers (not the speed or the wall).
        /// </summary>
        public bool GameChanged
        {
            get
            {
                var game = preset.Value;

                if (game == HighwayPreset.Custom)
                    return false;

                foreach (var setting in game_settings)
                {
                    if (!setting.IsPlayers && memory.Get(game, setting.Key) != null)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Leaving <paramref name="from"/>: it keeps what it has, so choosing it again brings it back. <paramref name="to"/>
        /// shows its own values and what it remembers; chosen for the first time, it starts from the current speed and wall.
        /// </summary>
        private void switchGame(HighwayPreset from, HighwayPreset to)
        {
            rememberAll(from);
            applyGame(to, true);
            rememberAll(to);
        }

        /// <summary>
        /// Shows <paramref name="game"/>'s values with what it remembers on top: its look and numbers always, the player's
        /// wall and speed when <paramref name="restorePlayers"/> (on load they are the stored ones already). Custom has no values
        /// of its own: it shows what it remembers and keeps the rest. Each setting's reset goes back to the game's value
        /// (Custom: the default game's).
        /// </summary>
        private void applyGame(HighwayPreset game, bool restorePlayers)
        {
            bool wasApplying = applyingPreset;
            applyingPreset = true;

            try
            {
                var values = HighwayPresets.Get(game == HighwayPreset.Custom ? HighwayPresets.DEFAULT : game);
                bool hasWall = HighwayPresets.HasWall(game);

                foreach (var setting in game_settings)
                {
                    if (setting.IsWall && !hasWall)
                        continue;

                    setting.UseDefault(this, values);

                    string? remembered = memory.Get(game, setting.Key);

                    if (remembered != null && (restorePlayers || !setting.IsPlayers))
                        setting.Write(this, remembered);
                    else if (game != HighwayPreset.Custom)
                        setting.UseGames(this, values);
                }

                if (!hasWall)
                    SetValue(HighwaySetting.Wall, 0f);

                if (restorePlayers && scrollSpeed != null
                                   && double.TryParse(memory.Get(game, scroll_speed_key), NumberStyles.Float, CultureInfo.InvariantCulture, out double speed))
                    scrollSpeed.Value = speed;
            }
            finally
            {
                applyingPreset = wasApplying;
            }
        }

        private void onGameSettingChanged()
        {
            if (applyingPreset)
                return;

            var game = preset.Value;

            // A game without a wall keeps it down (Alpha 2026-11-08: "sdvx should have wall = 0").
            if (!HighwayPresets.HasWall(game) && Get<float>(HighwaySetting.Wall) != 0)
            {
                applyingPreset = true;

                try
                {
                    SetValue(HighwaySetting.Wall, 0f);
                }
                finally
                {
                    applyingPreset = false;
                }
            }

            rememberAll(game);
        }

        /// <summary>
        /// Remembers the current values for <paramref name="game"/>: of its look and numbers only the player's changes (a value
        /// back at the game's own is forgotten), the player's wall and speed always, and everything for Custom.
        /// </summary>
        private void rememberAll(HighwayPreset game)
        {
            bool changed = false;

            foreach (var setting in game_settings)
                changed |= remember(game, setting);

            if (scrollSpeed != null)
                changed |= memory.Set(game, scroll_speed_key, scrollSpeed.Value.ToString("R", CultureInfo.InvariantCulture));

            if (changed)
                storedMemory.Value = memory.ToString();
        }

        private bool remember(HighwayPreset game, GameSetting setting)
        {
            // A game without a wall keeps none.
            if (setting.IsWall && !HighwayPresets.HasWall(game))
                return false;

            bool gamesOwn = game != HighwayPreset.Custom && !setting.IsPlayers && setting.IsGames(this, HighwayPresets.Get(game));
            return memory.Set(game, setting.Key, gamesOwn ? null : setting.Read(this));
        }

        /// <summary>
        /// Once per install, when games started to remember their settings (Alpha 2026-11-08): the game in use keeps what the
        /// player had chosen until then. That is its wall and whether it keeps the visible time, and its speed and wall
        /// numbers if they had been shown already (<see cref="HighwaySetting.PresetScalesApplied"/>; otherwise they become the
        /// game's now). Its look was the game's own, or it would have been Custom, which keeps everything.
        /// </summary>
        private void startGameMemoryOnce()
        {
            var started = GetOriginalBindable<bool>(HighwaySetting.GameMemoryStarted);

            if (started.Value)
                return;

            var game = preset.Value;
            bool scalesShown = Get<bool>(HighwaySetting.PresetScalesApplied);
            bool changed = false;

            foreach (var setting in game_settings)
            {
                if (game == HighwayPreset.Custom || setting.IsPlayers || (scalesShown && setting.Setting is HighwaySetting.SpeedScale or HighwaySetting.WallScale))
                    changed |= remember(game, setting);
            }

            if (changed)
                storedMemory.Value = memory.ToString();

            SetValue(HighwaySetting.PresetScalesApplied, true);
            started.Value = true;
        }

        /// <summary>
        /// Once per install, when the lane's tilt and length replaced the far edge's width and height (Alpha 2026-11-08): a
        /// Custom camera keeps its shape as the tilt and length that draw it (<see cref="HighwayCamera.FromLayout"/>). A named
        /// preset already shows its own.
        /// </summary>
        private void keepCustomCameraAsTilt()
        {
            var applied = GetOriginalBindable<bool>(HighwaySetting.LaneTiltApplied);

            if (applied.Value)
                return;

            if (Get<HighwayPreset>(HighwaySetting.Preset) == HighwayPreset.Custom)
            {
                bool perspective = Get<HighwayCameraMode>(HighwaySetting.CameraMode) == HighwayCameraMode.Perspective;
                var camera = HighwayCamera.FromLayout(1 - Get<float>(HighwaySetting.JudgementLineHeight), Get<float>(HighwaySetting.FarEdgeHeight),
                    Get<float>(HighwaySetting.FarWidthRatio), perspective);

                SetValue(HighwaySetting.LaneTilt, camera.Tilt);

                // A parallel camera's tilt alone sets its height; the length stays for a later perspective camera.
                if (perspective)
                    SetValue(HighwaySetting.LaneLength, camera.LaneLength);

                // Custom shows what it remembers, which is this camera now.
                rememberAll(HighwayPreset.Custom);
            }

            applied.Value = true;
        }

        /// <summary>
        /// Once, on an install from before presets set the width (Alpha 2026-11-08): "Width by" Lane was a preference outside
        /// the presets then, and every preset is "Width by" Track or has its own lane width now. Applying the preset would
        /// throw the player's lane width away, so such an install moves to Custom and keeps it. Runs before
        /// <c>startGameMemoryOnce</c> marks the install as done.
        /// </summary>
        private void keepWidthByLane()
        {
            if (GetOriginalBindable<bool>(HighwaySetting.PresetScalesApplied).Value || preset.Value == HighwayPreset.Custom)
                return;

            var values = HighwayPresets.Get(preset.Value);

            if (Get<HighwayWidthMode>(HighwaySetting.WidthMode) != values.WidthMode
                || (values.WidthMode == HighwayWidthMode.Lane && !near(Get<float>(HighwaySetting.LaneWidth), values.LaneWidth)))
            {
                preset.Value = HighwayPreset.Custom;

                // Custom shows what it remembers, which is this look now.
                rememberAll(HighwayPreset.Custom);
            }
        }

        protected override bool PerformSave()
        {
            base.PerformSave();
            own?.Save(ConfigStore);
            return true;
        }

        /// <summary>
        /// Once per install: the note, hit effect and track styles were stored as "Highway" (the default before the skin
        /// porter) the first time this ruleset ran, so the new "Skin (ported)" default would never reach an existing install.
        /// Styles still on "Highway" move to "Skin (ported)" (skins without legacy mania images keep the highway's look
        /// anyway); a style set to anything else is left alone. Runs once, so choosing "Highway" again sticks.
        /// </summary>
        public void ApplySkinPortDefaults()
        {
            var applied = GetOriginalBindable<bool>(HighwaySetting.SkinPortDefaultsApplied);

            if (applied.Value)
                return;

            if (Get<HighwayNoteStyle>(HighwaySetting.NoteStyle) == HighwayNoteStyle.Highway)
                SetValue(HighwaySetting.NoteStyle, HighwayNoteStyle.Ported);
            if (Get<HighwayHitEffectStyle>(HighwaySetting.HitEffects) == HighwayHitEffectStyle.Highway)
                SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Ported);
            if (Get<HighwayTrackStyle>(HighwaySetting.Track) == HighwayTrackStyle.Highway)
                SetValue(HighwaySetting.Track, HighwayTrackStyle.Ported);

            applied.Value = true;
        }

        /// <summary>
        /// Once per install, for the same reason as the styles above: the stage fit's default moved from the camera's width
        /// ("Skin position") to the skin's own ("Skin position and width"), so an install still on the old default moves; a
        /// fit chosen afterwards sticks.
        /// </summary>
        private void applySkinLayoutDefault()
        {
            var applied = GetOriginalBindable<bool>(HighwaySetting.SkinLayoutDefaultApplied);

            if (applied.Value)
                return;

            if (Get<SkinStageFit>(HighwaySetting.StageFit) == SkinStageFit.SkinPosition)
                SetValue(HighwaySetting.StageFit, SkinStageFit.SkinLayout);

            applied.Value = true;
        }

        /// <summary>
        /// The settings panel's groups folded on a new install (<see cref="HighwaySetting.FoldedGroups"/>).
        /// </summary>
        public const string DEFAULT_FOLDED_GROUPS = "Other";

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();

            var defaults = HighwayPresets.Get(HighwayPresets.DEFAULT);

            // Fine enough for measured preset values such as a 7.4% judgement line.
            const float precision = 0.001f;

            SetDefault(HighwaySetting.Preset, HighwayPresets.DEFAULT);
            SetDefault(HighwaySetting.CameraMode, defaults.CameraMode);
            SetDefault(HighwaySetting.LaneTilt, defaults.LaneTilt, HighwayCamera.MIN_TILT, HighwayCamera.MAX_TILT, 0.1f);
            SetDefault(HighwaySetting.LaneLength, defaults.LaneLength, HighwayCamera.MIN_LANE_LENGTH, HighwayCamera.MAX_LANE_LENGTH, 0.01f);
            SetDefault(HighwaySetting.Approach, defaults.Approach, ApproachCurve.PERSPECTIVE, ApproachCurve.EVEN, 0.05f);
            SetDefault(HighwaySetting.JudgementLineHeight, defaults.JudgementLineHeight, 0f, HighwaySettings.MAX_JUDGEMENT_LINE_HEIGHT, precision);
            // Read once, by keepCustomCameraAsTilt: the far edge as set before the lane's tilt and length (Chunithm's then).
            SetDefault(HighwaySetting.FarWidthRatio, 0.24f, HighwayLayout.MIN_FAR_WIDTH_RATIO, 1f, precision);
            SetDefault(HighwaySetting.FarEdgeHeight, 0f, 0f, 0.5f, precision);
            SetDefault(HighwaySetting.LaneTiltApplied, false);
            SetDefault(HighwaySetting.TrackWidth, defaults.TrackWidth, 0.2f, 1f, precision);
            // Fine enough for every scale's units (a white number is a thousandth of the lane on screen).
            SetDefault(HighwaySetting.Wall, defaults.Wall, 0f, HighwayScroll.MAX_WALL, 0.0001f);
            SetDefault(HighwaySetting.NoteThickness, defaults.NoteThickness, 0.005f, HighwaySettings.MAX_NOTE_THICKNESS, 0.0005f);
            SetDefault(HighwaySetting.WallKeepsVisibleTime, true);
            SetDefault(HighwaySetting.LaneGuides, true);
            SetDefault(HighwaySetting.SkinHitTarget, false);
            SetDefault(HighwaySetting.NoteStyle, defaults.NoteStyle);
            SetDefault(HighwaySetting.HitEffects, defaults.HitEffects);
            SetDefault(HighwaySetting.Track, defaults.Track);
            SetDefault(HighwaySetting.KeyArea, defaults.KeyArea);
            SetDefault(HighwaySetting.StageFit, defaults.StageFit);
            SetDefault(HighwaySetting.SideStagePull, defaults.SideStagePull, 0f, 1f, 0.01f);
            SetDefault(HighwaySetting.SideStageLean, defaults.SideStageLean, 0f, 1f, 0.01f);
            SetDefault(HighwaySetting.SkinLayoutDefaultApplied, false);
            SetDefault(HighwaySetting.SkinNoteProportions, defaults.SkinNoteProportions);
            SetDefault(HighwaySetting.FarFade, defaults.FarFade, 0f, HighwaySettings.MAX_FAR_FADE, 0.01f);
            SetDefault(HighwaySetting.HoldOpacity, defaults.HoldOpacity, HighwaySettings.MIN_HOLD_OPACITY, 1f, 0.01f);
            SetDefault(HighwaySetting.TrackOpacity, defaults.TrackOpacity, 0f, 1f, 0.01f);
            SetDefault(HighwaySetting.KeyBeam, defaults.KeyBeam);
            SetDefault(HighwaySetting.KiaiSway, defaults.KiaiSway, 0f, 1f, 0.01f);
            SetDefault(HighwaySetting.KiaiLift, defaults.KiaiLift, 0f, 1f, 0.01f);
            SetDefault(HighwaySetting.WallScale, defaults.WallScale);
            SetDefault(HighwaySetting.ShareManiaKeys, true);
            SetDefault(HighwaySetting.SpeedScale, defaults.SpeedScale);
            SetDefault(HighwaySetting.WidthMode, defaults.WidthMode);
            SetDefault(HighwaySetting.LaneWidth, defaults.LaneWidth, HighwaySettings.MIN_LANE_WIDTH, HighwaySettings.MAX_LANE_WIDTH, 0.001f);
            SetDefault(HighwaySetting.SkinPortDefaultsApplied, false);
            SetDefault(HighwaySetting.PresetScalesApplied, false);
            SetDefault(HighwaySetting.FineScrollSpeed, 0.0, 0, SpeedScales.MAX_MANIA_SPEED, SpeedScales.MANIA_SPEED_PRECISION);
            SetDefault(HighwaySetting.GameMemory, string.Empty);
            SetDefault(HighwaySetting.GameMemoryStarted, false);
            SetDefault(HighwaySetting.FoldedGroups, DEFAULT_FOLDED_GROUPS);
        }

        /// <summary>
        /// The current values as one snapshot.
        /// </summary>
        public HighwaySettings Current => new HighwaySettings
        {
            CameraMode = Get<HighwayCameraMode>(HighwaySetting.CameraMode),
            LaneTilt = Get<float>(HighwaySetting.LaneTilt),
            LaneLength = Get<float>(HighwaySetting.LaneLength),
            Approach = Get<float>(HighwaySetting.Approach),
            JudgementLineHeight = Get<float>(HighwaySetting.JudgementLineHeight),
            TrackWidth = Get<float>(HighwaySetting.TrackWidth),
            Wall = Get<float>(HighwaySetting.Wall),
            NoteThickness = Get<float>(HighwaySetting.NoteThickness),
            WallKeepsVisibleTime = Get<bool>(HighwaySetting.WallKeepsVisibleTime),
            LaneGuides = Get<bool>(HighwaySetting.LaneGuides),
            SkinHitTarget = Get<bool>(HighwaySetting.SkinHitTarget),
            NoteStyle = Get<HighwayNoteStyle>(HighwaySetting.NoteStyle),
            HitEffects = Get<HighwayHitEffectStyle>(HighwaySetting.HitEffects),
            Track = Get<HighwayTrackStyle>(HighwaySetting.Track),
            KeyArea = Get<SkinKeyArea>(HighwaySetting.KeyArea),
            StageFit = Get<SkinStageFit>(HighwaySetting.StageFit),
            SideStagePull = Get<float>(HighwaySetting.SideStagePull),
            SideStageLean = Get<float>(HighwaySetting.SideStageLean),
            SkinNoteProportions = Get<bool>(HighwaySetting.SkinNoteProportions),
            FarFade = Get<float>(HighwaySetting.FarFade),
            HoldOpacity = Get<float>(HighwaySetting.HoldOpacity),
            TrackOpacity = Get<float>(HighwaySetting.TrackOpacity),
            KeyBeam = Get<KeyBeamLength>(HighwaySetting.KeyBeam),
            KiaiSway = Get<float>(HighwaySetting.KiaiSway),
            KiaiLift = Get<float>(HighwaySetting.KiaiLift),
            WallScale = Get<WallScale>(HighwaySetting.WallScale),
            SpeedScale = Get<SpeedScale>(HighwaySetting.SpeedScale),
            WidthMode = Get<HighwayWidthMode>(HighwaySetting.WidthMode),
            LaneWidth = Get<float>(HighwaySetting.LaneWidth),
        };

        private static bool near(float a, float b) => Math.Abs(a - b) < 1e-4f;

        /// <summary>
        /// One of the settings each game remembers (<see cref="game_settings"/>), read and written as text for
        /// <see cref="GameMemory"/>.
        /// </summary>
        private abstract class GameSetting
        {
            public readonly HighwaySetting Setting;
            public readonly string Key;

            /// <summary>
            /// One of the wall's settings, which a game without a wall leaves alone.
            /// </summary>
            public readonly bool IsWall;

            protected GameSetting(HighwaySetting setting, bool isWall)
            {
                Setting = setting;
                Key = setting.ToString();
                IsWall = isWall;
            }

            /// <summary>
            /// The player's alone: no game sets it.
            /// </summary>
            public abstract bool IsPlayers { get; }

            public abstract string Read(HighwayConfigManager config);
            public abstract void Write(HighwayConfigManager config, string value);

            /// <summary>
            /// Whether the current value is <paramref name="game"/>'s own.
            /// </summary>
            public abstract bool IsGames(HighwayConfigManager config, HighwaySettings game);

            public abstract void UseGames(HighwayConfigManager config, HighwaySettings game);

            /// <summary>
            /// Makes <paramref name="game"/>'s value the setting's default, which its reset button goes back to.
            /// </summary>
            public abstract void UseDefault(HighwayConfigManager config, HighwaySettings game);

            public abstract void Watch(HighwayConfigManager config, Action changed);
        }

        private sealed class GameSetting<T> : GameSetting
            where T : notnull
        {
            private readonly Func<HighwaySettings, T>? game;

            public GameSetting(HighwaySetting setting, Func<HighwaySettings, T>? game, bool isWall = false)
                : base(setting, isWall)
            {
                this.game = game;
            }

            public override bool IsPlayers => game == null;

            public override string Read(HighwayConfigManager config) => config.Get<T>(Setting) switch
            {
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                var value => value.ToString() ?? string.Empty,
            };

            public override void Write(HighwayConfigManager config, string value)
            {
                if (tryParse(value, out T parsed))
                    config.SetValue(Setting, parsed);
            }

            public override bool IsGames(HighwayConfigManager config, HighwaySettings values)
            {
                if (game == null)
                    return false;

                T current = config.Get<T>(Setting);
                T own = game(values);

                return current is float a && own is float b ? near(a, b) : EqualityComparer<T>.Default.Equals(current, own);
            }

            public override void UseGames(HighwayConfigManager config, HighwaySettings values)
            {
                if (game != null)
                    config.SetValue(Setting, game(values));
            }

            public override void UseDefault(HighwayConfigManager config, HighwaySettings values)
            {
                if (game != null)
                    config.GetOriginalBindable<T>(Setting).Default = game(values);
            }

            public override void Watch(HighwayConfigManager config, Action changed) => config.GetOriginalBindable<T>(Setting).ValueChanged += _ => changed();

            private static bool tryParse(string text, out T value)
            {
                object? parsed = null;

                if (typeof(T) == typeof(float))
                {
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                        parsed = number;
                }
                else if (typeof(T) == typeof(bool))
                {
                    if (bool.TryParse(text, out bool flag))
                        parsed = flag;
                }
                else if (typeof(T).IsEnum && Enum.TryParse(typeof(T), text, out object? member) && Enum.IsDefined(typeof(T), member))
                    parsed = member;

                value = parsed is T typed ? typed : default!;
                return parsed != null;
            }
        }
    }

    public enum HighwaySetting
    {
        Preset,
        CameraMode,

        /// <summary>
        /// Not shown: the far end's width before <see cref="LaneTilt"/> and <see cref="LaneLength"/> replaced it, read once to
        /// keep a Custom camera's shape (<c>keepCustomCameraAsTilt</c>).
        /// </summary>
        FarWidthRatio,

        JudgementLineHeight,

        /// <summary>
        /// Not shown: the far edge's height before <see cref="LaneTilt"/> and <see cref="LaneLength"/> replaced it, read once to
        /// keep a Custom camera's shape (<c>keepCustomCameraAsTilt</c>).
        /// </summary>
        FarEdgeHeight,

        TrackWidth,
        Wall,
        WallKeepsVisibleTime,
        LaneGuides,
        SkinHitTarget,
        NoteStyle,
        NoteThickness,
        HitEffects,
        Track,
        KeyArea,
        StageFit,
        SkinNoteProportions,
        FarFade,
        HoldOpacity,
        TrackOpacity,
        KeyBeam,
        KiaiSway,
        WallScale,
        SideStagePull,
        SideStageLean,

        /// <summary>
        /// The units the scroll speed is set and shown in (<see cref="Highway.SpeedScale"/>); the speed itself stays osu!mania's
        /// scroll speed setting.
        /// </summary>
        SpeedScale,

        /// <summary>
        /// Whether the track width or the lane width sets the highway track's width (<see cref="HighwaySettings.WidthMode"/>).
        /// </summary>
        WidthMode,

        /// <summary>
        /// One lane's width at the judgement line in lane mode (<see cref="HighwaySettings.LaneWidth"/>).
        /// </summary>
        LaneWidth,

        /// <summary>
        /// Whether maniaortho plays with the keys bound for osu!mania, the two key binding sections kept equal
        /// (<see cref="KeyBindingMirror"/>), or with its own.
        /// </summary>
        ShareManiaKeys,

        /// <summary>
        /// Not shown: whether <see cref="HighwayConfigManager.ApplySkinPortDefaults"/> has run on this install.
        /// </summary>
        SkinPortDefaultsApplied,

        /// <summary>
        /// Not shown: whether the preset in use has set its game's speed and wall scales on this install (Alpha 2026-11-08;
        /// <c>startGameMemoryOnce</c> sets it now).
        /// </summary>
        PresetScalesApplied,

        /// <summary>
        /// Not shown: osu!mania's scroll speed at maniaortho's precision and range (0 before it is first stored), which
        /// osu!mania's limits would round and cap as they read it (<see cref="ManiaOrthoRulesetConfigManager"/>).
        /// </summary>
        FineScrollSpeed,

        /// <summary>
        /// Not shown: whether the one-time move of a stored "Skin position" stage fit to the skin's own width
        /// (<c>applySkinLayoutDefault</c>, Alpha 2026-11-04) has run on this install.
        /// </summary>
        SkinLayoutDefaultApplied,

        /// <summary>
        /// How far the lane leans back, in degrees (<see cref="HighwaySettings.LaneTilt"/>).
        /// </summary>
        LaneTilt,

        /// <summary>
        /// How long the lane is, in screen heights (<see cref="HighwaySettings.LaneLength"/>).
        /// </summary>
        LaneLength,

        /// <summary>
        /// How notes travel down the lane (<see cref="HighwaySettings.Approach"/>).
        /// </summary>
        Approach,

        /// <summary>
        /// How far the lane lifts in kiai sections (<see cref="HighwaySettings.KiaiLift"/>).
        /// </summary>
        KiaiLift,

        /// <summary>
        /// Not shown: whether a Custom camera's far edge has been turned into a lane tilt and length on this install
        /// (<c>keepCustomCameraAsTilt</c>, Alpha 2026-11-08).
        /// </summary>
        LaneTiltApplied,

        /// <summary>
        /// Not shown: what each game remembers of the settings under "Game preset" (<see cref="Configuration.GameMemory"/>,
        /// Alpha 2026-11-08).
        /// </summary>
        GameMemory,

        /// <summary>
        /// Not shown: whether the game in use has taken over the player's wall, speed and numbers on this install
        /// (<c>startGameMemoryOnce</c>, Alpha 2026-11-08).
        /// </summary>
        GameMemoryStarted,

        /// <summary>
        /// The settings panel's folded groups, by name, comma separated. Only how the panel looks.
        /// </summary>
        FoldedGroups,
    }
}
