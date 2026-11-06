// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// The drawable ruleset for <see cref="ManiaOrthoRuleset"/>: osu!mania's drawable ruleset, with its playfield drawn on
    /// the highway (see <see cref="HighwayPlayfieldAdjustmentContainer"/>).
    /// </summary>
    /// <remarks>
    /// Input, hit objects and judgement are mania's own, untouched. Only how the playfield is
    /// drawn changes.
    /// </remarks>
    public partial class DrawableManiaOrthoRuleset : DrawableManiaRuleset
    {
        /// <summary>
        /// The container that draws the playfield on the highway. Set once <see cref="CreatePlayfieldAdjustmentContainer"/>
        /// has run for the playfield.
        /// </summary>
        public HighwayPlayfieldAdjustmentContainer? Highway { get; private set; }

        /// <summary>
        /// The highway settings, or null outside a game (no settings store).
        /// </summary>
        public HighwayConfigManager? HighwayConfig => (Config as ManiaOrthoRulesetConfigManager)?.Highway;

        /// <summary>
        /// The speed and wall toasts, or null outside a game (no settings store).
        /// </summary>
        public HighwayToasts? Toasts { get; private set; }

        /// <summary>
        /// The actions held down in the key binding container mania's columns and notes receive their presses from (the
        /// player's keys, or a replay's), or null before it exists. Read only.
        /// </summary>
        public IEnumerable<ManiaAction>? PressedActions => (KeyBindingInputManager as RulesetInputManager<ManiaAction>)?.KeyBindingContainer.PressedActions;

        public DrawableManiaOrthoRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            : base(ruleset, beatmap, mods)
        {
        }

        [BackgroundDependencyLoader]
        private void load(RealmAccess? realm)
        {
            // Keys set for maniaortho before it shared osu!mania's move to osu!mania, which this plays with, as the settings
            // would move them (KeyBindingMirror), so they work even if the settings are never opened. Before the input
            // manager loads its keys; it would follow a later change anyway.
            realm?.Write(KeyBindingMirror.Merge);

            if (Config is ManiaOrthoRulesetConfigManager config)
            {
                AddInternal(new HighwayHotkeys(config.Highway));
                AddInternal(Toasts = new HighwayToasts(this, config));
            }

            AddInternal(new PlayLogRecorder(HasReplayLoaded, Config as ManiaOrthoRulesetConfigManager));

            // Mania keeps a beatmap's scroll speed changes (SV) only when the beatmap was made for the ruleset being played,
            // which it checks against Ruleset.RulesetInfo: maniaortho here, so every mania beatmap would lose its SV. This
            // runs after mania's loader and restores what mania keeps for its own beatmaps: at each control point, the
            // effect point in force at that time, exactly as DrawableScrollingRuleset merged them. Converts stay stripped.
            if (!Beatmap.BeatmapInfo.Ruleset.Equals(ManiaOrthoRuleset.MANIA.RulesetInfo))
                return;

            foreach (var p in ControlPoints)
                p.EffectPoint = Beatmap.ControlPointInfo.EffectPointAt(p.Time);
        }

        /// <summary>
        /// osu!mania's input manager, with the keys bound for osu!mania (Alpha 2026-11-04: change keys once and both follow).
        /// </summary>
        /// <remarks>
        /// The ruleset given is only where the input manager looks its key bindings up (by short name, per key count). Keys map
        /// to the same actions as in osu!mania, and everything after that, judgement and replays included, is mania's own.
        /// The maniaortho section osu! lists in its key binding settings is kept equal to osu!mania's by
        /// <see cref="KeyBindingMirror"/>.
        /// </remarks>
        protected override PassThroughInputManager CreateInputManager() => new ManiaInputManager(ManiaOrthoRuleset.MANIA.RulesetInfo, Variant);

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var baseDependencies = base.CreateChildDependencies(parent);

            // The config is cached under its own type only, but mania's drawables (notes, columns) resolve it as
            // ManiaRulesetConfigManager. Without this, settings such as timing-based note colouring do nothing here.
            if (Config is not ManiaOrthoRulesetConfigManager config)
                return baseDependencies;

            var dependencies = new DependencyContainer(baseDependencies);
            dependencies.CacheAs<ManiaRulesetConfigManager>(config);
            return dependencies;
        }

        public override PlayfieldAdjustmentContainer CreatePlayfieldAdjustmentContainer()
        {
            // The first call wraps the playfield. Later calls (the resume overlay) stay flat on screen.
            if (Highway != null)
                return base.CreatePlayfieldAdjustmentContainer();

            return Highway = new HighwayPlayfieldAdjustmentContainer(this);
        }
    }
}
