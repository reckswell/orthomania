// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Configuration.Tracking;
using osu.Framework.Graphics;
using osu.Game.Input;
using osu.Game.Input.Bindings;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// The toasts osu! shows in gameplay when the scroll speed (F3 and F4) or the wall (Shift with those) changes, with
    /// how long notes are now visible on the highway (<see cref="VisibleTime"/>): mania's own speed toast shows
    /// <c>11485 / speed</c>, which ignores the skin's hit position and the wall (research/topic-covers.md C3).
    /// </summary>
    /// <remarks>
    /// The configs themselves track nothing (<see cref="ManiaOrthoRulesetConfigManager.CreateTrackedSettings"/>): only a
    /// play knows its skin and mods.
    /// </remarks>
    public partial class HighwayToasts : Component
    {
        /// <summary>
        /// The keys that move the wall in gameplay (<see cref="HighwayHotkeys"/>) with osu!'s default bindings, named under the
        /// wall's toast. The toast names the keys bound now (<see cref="WallShortcut"/>).
        /// </summary>
        public const string WALL_SHORTCUT = "Shift+F3 / Shift+F4";

        private readonly DrawableManiaRuleset drawableRuleset;
        private readonly ManiaOrthoRulesetConfigManager config;

        [Resolved]
        private OnScreenDisplay? onScreenDisplay { get; set; }

        [Resolved]
        private IGameplayClock? gameplayClock { get; set; }

        [Resolved]
        private ISkinSource skin { get; set; } = null!;

        [Resolved]
        private RealmKeyBindingStore? keyBindingStore { get; set; }

        private IDisposable? speedTracker;
        private IDisposable? wallTracker;

        public HighwayToasts(DrawableManiaRuleset drawableRuleset, ManiaOrthoRulesetConfigManager config)
        {
            this.drawableRuleset = drawableRuleset;
            this.config = config;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            speedTracker = onScreenDisplay?.BeginTracking(this, new Tracked(config, () => new TrackedSettings
            {
                new ConfigManager<ManiaRulesetSetting>.TrackedSetting<double>(ManiaRulesetSetting.ScrollSpeed, DescribeScrollSpeed),
            }));

            wallTracker = onScreenDisplay?.BeginTracking(this, new Tracked(config.Highway, () => new TrackedSettings
            {
                new ConfigManager<HighwaySetting>.TrackedSetting<float>(HighwaySetting.Wall, DescribeWall),
            }));
        }

        /// <summary>
        /// How long notes are visible now, in real milliseconds: at the speed mania applies, with this play's skin and mods and
        /// the current wall and camera.
        /// </summary>
        public double CurrentVisibleTime => visibleTimeAt(drawableRuleset.TargetTimeRange);

        /// <summary>
        /// Mania's speed toast ("405ms (speed 29.0)"), with the time notes are visible on the highway.
        /// </summary>
        /// <remarks>
        /// From the new speed, not the play's time range: this runs before mania applies the change.
        /// </remarks>
        public SettingDescription DescribeScrollSpeed(double speed)
        {
            var scale = config.Highway.Get<SpeedScale>(HighwaySetting.SpeedScale);

            if (scale == SpeedScale.Mania)
            {
                return new SettingDescription(
                    rawValue: speed,
                    name: RulesetSettingsStrings.ScrollSpeed,
                    value: RulesetSettingsStrings.ScrollSpeedTooltip(milliseconds(visibleTimeAt(DrawableManiaRuleset.ComputeScrollTime(speed))), speed));
            }

            // In the chosen game's numbers: "Chunithm 9.25 · 593 ms visible".
            var context = SpeedContext;

            return new SettingDescription(
                rawValue: speed,
                name: SpeedScales.Caption(scale),
                value: $"{SpeedScales.Format(scale, context.ToUnits(scale, speed))} · {milliseconds(context.Milliseconds(speed, true))} ms visible");
        }

        /// <summary>
        /// What turns the scroll speed into a game's speed numbers in this play: its skin's line, its rate, the wall and camera.
        /// </summary>
        public SpeedContext SpeedContext
        {
            get
            {
                var settings = config.Highway.Current;
                var mods = gameplayClock?.AdjustmentsFromMods;
                double rate = mods == null ? 1 : mods.AggregateTempo.Value * mods.AggregateFrequency.Value;

                return new SpeedContext(VisibleTime.HitPosition(skin), rate, settings.ToScroll(), settings.CameraMode == HighwayCameraMode.Flat);
            }
        }

        /// <summary>
        /// "40% covered · 405 ms visible" (in the chosen <see cref="WallScale"/>'s units: "wall 5", "WN 350"), with "(speed
        /// kept)" when the wall shortens the visible time.
        /// </summary>
        public SettingDescription DescribeWall(float wall)
        {
            var settings = config.Highway.Current;
            // The wall is read from the config, which already has the new value.
            int visible = milliseconds(CurrentVisibleTime);

            string cover;

            if (settings.CameraMode == HighwayCameraMode.Flat)
                cover = wall > 0 ? $"{WallScales.Format(settings.WallScale, wall, 1)}, not drawn by the flat camera" : "Off";
            else if (wall > 0)
            {
                var layout = settings.ToLayout();
                cover = WallScales.Describe(settings.WallScale, wall, layout.FarWidthRatio, layout.Approach);
                if (!settings.WallKeepsVisibleTime)
                    cover += " (speed kept)";
            }
            else
                cover = "Off";

            return new SettingDescription(
                rawValue: wall,
                name: "Wall",
                value: $"{cover} · {visible} ms visible",
                shortcut: WallShortcut);
        }

        /// <summary>
        /// The keys that move the wall now: Shift with the first key bound to each scroll speed action, as osu! names keys in
        /// its own toasts (<see cref="WALL_SHORTCUT"/> outside a game).
        /// </summary>
        public string WallShortcut
        {
            get
            {
                if (keyBindingStore == null)
                    return WALL_SHORTCUT;

                return $"Shift+{firstKey(GlobalAction.DecreaseScrollSpeed)} / Shift+{firstKey(GlobalAction.IncreaseScrollSpeed)}";

                string firstKey(GlobalAction action) => keyBindingStore.GetReadableKeyCombinationsFor(action).FirstOrDefault() ?? ToastStrings.NoKeyBound.ToString();
            }
        }

        private double visibleTimeAt(double targetTimeRange)
        {
            var context = SpeedContext;
            return VisibleTime.Of(targetTimeRange, context.HitPosition, context.Rate, context.Scroll, context.Flat);
        }

        private static int milliseconds(double time) => (int)Math.Round(time);

        protected override void Dispose(bool isDisposing)
        {
            speedTracker?.Dispose();
            speedTracker = null;
            wallTracker?.Dispose();
            wallTracker = null;

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// A config whose toasts are made here: the on-screen display loads the settings from the config and asks this for
        /// their descriptions.
        /// </summary>
        private class Tracked : ITrackableConfigManager
        {
            private readonly ITrackableConfigManager config;
            private readonly Func<TrackedSettings> create;

            public Tracked(ITrackableConfigManager config, Func<TrackedSettings> create)
            {
                this.config = config;
                this.create = create;
            }

            public TrackedSettings CreateTrackedSettings() => create();

            public void LoadInto(TrackedSettings settings) => config.LoadInto(settings);

            public void Load() => config.Load();

            public bool Save() => config.Save();
        }
    }
}
