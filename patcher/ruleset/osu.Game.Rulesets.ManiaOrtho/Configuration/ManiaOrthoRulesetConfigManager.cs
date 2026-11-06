// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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

            // The camera defines the direction (SPEC 3): notes always come down the track, whatever was stored.
            SetValue(ManiaRulesetSetting.ScrollDirection, ManiaScrollingDirection.Down);
            GetOriginalBindable<ManiaScrollingDirection>(ManiaRulesetSetting.ScrollDirection).Disabled = true;
        }

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

        public HighwayConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
            : base(OwnSettings.StoreFor(settings), ruleset, variant)
        {
            own = OwnSettings.Load("highway", variant, ConfigStore);

            var preset = GetOriginalBindable<HighwayPreset>(HighwaySetting.Preset);

            // A named preset always shows its current values, so tuning a preset in code reaches everyone using it.
            ApplyPreset(preset.Value);

            preset.ValueChanged += e => ApplyPreset(e.NewValue);

            // Changing any value by hand turns the preset into "Custom".
            GetOriginalBindable<HighwayCameraMode>(HighwaySetting.CameraMode).ValueChanged += _ => onPresetValueChanged();
            GetOriginalBindable<float>(HighwaySetting.FarWidthRatio).ValueChanged += _ => onPresetValueChanged();
            GetOriginalBindable<float>(HighwaySetting.JudgementLineHeight).ValueChanged += _ => onPresetValueChanged();
            GetOriginalBindable<float>(HighwaySetting.FarEdgeHeight).ValueChanged += _ => onPresetValueChanged();
            GetOriginalBindable<float>(HighwaySetting.TrackWidth).ValueChanged += _ => onPresetValueChanged();
            GetOriginalBindable<float>(HighwaySetting.Wall).ValueChanged += _ => onPresetValueChanged();
            GetOriginalBindable<float>(HighwaySetting.NoteThickness).ValueChanged += _ => onPresetValueChanged();

            void onPresetValueChanged()
            {
                if (!applyingPreset && preset.Value != HighwayPreset.Custom && !matches(HighwayPresets.Get(preset.Value)))
                    preset.Value = HighwayPreset.Custom;
            }

            // Choosing a game's wall scale also gives the wall that game's speed behaviour (Chunithm and osu!mania keep the
            // speed, IIDX the visible time); the toggle can still be changed afterwards.
            GetOriginalBindable<WallScale>(HighwaySetting.WallScale).ValueChanged += e =>
            {
                if (WallScales.KeepsVisibleTime(e.NewValue) is bool keepsVisibleTime)
                    SetValue(HighwaySetting.WallKeepsVisibleTime, keepsVisibleTime);
            };

            ApplySkinPortDefaults();
            applySkinLayoutDefault();
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

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();

            var defaults = HighwayPresets.Get(HighwayPresets.DEFAULT);

            // Fine enough for measured preset values such as a 7.4% judgement line.
            const float precision = 0.001f;

            SetDefault(HighwaySetting.Preset, HighwayPresets.DEFAULT);
            SetDefault(HighwaySetting.CameraMode, defaults.CameraMode);
            SetDefault(HighwaySetting.FarWidthRatio, defaults.FarWidthRatio, HighwayLayout.MIN_FAR_WIDTH_RATIO, 1f, precision);
            SetDefault(HighwaySetting.JudgementLineHeight, defaults.JudgementLineHeight, 0f, HighwaySettings.MAX_JUDGEMENT_LINE_HEIGHT, precision);
            SetDefault(HighwaySetting.FarEdgeHeight, defaults.FarEdgeHeight, 0f, 0.5f, precision);
            SetDefault(HighwaySetting.TrackWidth, defaults.TrackWidth, 0.2f, 1f, precision);
            // Fine enough for every scale's units (a white number is a thousandth of the lane on screen).
            SetDefault(HighwaySetting.Wall, defaults.Wall, 0f, HighwayScroll.MAX_WALL, 0.0001f);
            SetDefault(HighwaySetting.NoteThickness, defaults.NoteThickness, 0.005f, 0.08f, 0.0005f);
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
            SetDefault(HighwaySetting.WallScale, defaults.WallScale);
            SetDefault(HighwaySetting.SkinPortDefaultsApplied, false);
        }

        /// <summary>
        /// The current values as one snapshot.
        /// </summary>
        public HighwaySettings Current => new HighwaySettings
        {
            CameraMode = Get<HighwayCameraMode>(HighwaySetting.CameraMode),
            FarWidthRatio = Get<float>(HighwaySetting.FarWidthRatio),
            JudgementLineHeight = Get<float>(HighwaySetting.JudgementLineHeight),
            FarEdgeHeight = Get<float>(HighwaySetting.FarEdgeHeight),
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
            WallScale = Get<WallScale>(HighwaySetting.WallScale),
        };

        /// <summary>
        /// Writes a preset's values into the individual settings. Leaves <see cref="HighwaySetting.WallKeepsVisibleTime"/>,
        /// <see cref="HighwaySetting.LaneGuides"/>, <see cref="HighwaySetting.SkinHitTarget"/>,
        /// <see cref="HighwaySetting.NoteStyle"/>, <see cref="HighwaySetting.HitEffects"/>, <see cref="HighwaySetting.Track"/>,
        /// <see cref="HighwaySetting.FarFade"/>, <see cref="HighwaySetting.HoldOpacity"/>, <see cref="HighwaySetting.TrackOpacity"/>,
        /// <see cref="HighwaySetting.KeyBeam"/>, <see cref="HighwaySetting.KiaiSway"/>, <see cref="HighwaySetting.WallScale"/> and the
        /// skin porter's settings alone (preferences, not part of a camera).
        /// </summary>
        public void ApplyPreset(HighwayPreset preset)
        {
            if (preset == HighwayPreset.Custom)
                return;

            var values = HighwayPresets.Get(preset);

            applyingPreset = true;

            try
            {
                SetValue(HighwaySetting.CameraMode, values.CameraMode);
                SetValue(HighwaySetting.FarWidthRatio, values.FarWidthRatio);
                SetValue(HighwaySetting.JudgementLineHeight, values.JudgementLineHeight);
                SetValue(HighwaySetting.FarEdgeHeight, values.FarEdgeHeight);
                SetValue(HighwaySetting.TrackWidth, values.TrackWidth);
                SetValue(HighwaySetting.Wall, values.Wall);
                SetValue(HighwaySetting.NoteThickness, values.NoteThickness);
            }
            finally
            {
                applyingPreset = false;
            }
        }

        private bool matches(HighwaySettings preset)
        {
            var current = Current;

            // Flat ignores the geometry, so any geometry matches the Classic preset.
            if (preset.CameraMode == HighwayCameraMode.Flat)
                return current.CameraMode == HighwayCameraMode.Flat;

            return current.CameraMode == preset.CameraMode
                   && near(current.FarWidthRatio, preset.FarWidthRatio)
                   && near(current.JudgementLineHeight, preset.JudgementLineHeight)
                   && near(current.FarEdgeHeight, preset.FarEdgeHeight)
                   && near(current.TrackWidth, preset.TrackWidth)
                   && near(current.Wall, preset.Wall)
                   && near(current.NoteThickness, preset.NoteThickness);

            static bool near(float a, float b) => Math.Abs(a - b) < 1e-4f;
        }
    }

    public enum HighwaySetting
    {
        Preset,
        CameraMode,
        FarWidthRatio,
        JudgementLineHeight,
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
        /// Not shown: whether <see cref="HighwayConfigManager.ApplySkinPortDefaults"/> has run on this install.
        /// </summary>
        SkinPortDefaultsApplied,

        /// <summary>
        /// Not shown: whether the one-time move of a stored "Skin position" stage fit to the skin's own width
        /// (<c>applySkinLayoutDefault</c>, Alpha 2026-11-04) has run on this install.
        /// </summary>
        SkinLayoutDefaultApplied,
    }
}
