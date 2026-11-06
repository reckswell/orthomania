// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Mods;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho
{
    /// <summary>
    /// A separate ruleset that plays osu!mania beatmaps with a different (tilted / orthographic) playfield.
    /// </summary>
    /// <remarks>
    /// Everything except the playfield is delegated to <see cref="ManiaRuleset"/>, so that conversion, judgements,
    /// scoring, star rating and pp are identical to official osu!mania.
    /// This ruleset intentionally does NOT implement <see cref="ILegacyRuleset"/>: lazer only submits scores for
    /// legacy rulesets, so plays here can never reach the official leaderboards.
    /// </remarks>
    public class ManiaOrthoRuleset : Ruleset
    {
        public const string SHORT_NAME = "maniaortho";

        /// <summary>
        /// The upstream ruleset that owns everything except the playfield.
        /// Anything that compares ruleset identity (beatmap conversion, difficulty calculation) must be given this
        /// instance rather than <c>this</c>, or mania beatmaps will be treated as osu!standard converts.
        /// </summary>
        internal static readonly ManiaRuleset MANIA = new ManiaRuleset();

        public override string Description => "mania (ortho)";

        public override string ShortName => SHORT_NAME;

        public override string PlayingVerb => "Riding the highway";

        /// <summary>
        /// osu!mania beatmaps are this ruleset's own: they list under it at song select without enabling converted
        /// beatmaps, and presenting one keeps this ruleset selected (<see cref="Hooks.LazerHooks"/>).
        /// </summary>
        public const string PLAYS_BEATMAPS_OF = "mania";

        public string PlaysBeatmapsOfRuleset => PLAYS_BEATMAPS_OF;

        public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

        public override Drawable CreateIcon() => new SpriteIcon { Icon = FontAwesome.Solid.Road };

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            => new DrawableManiaOrthoRuleset(this, beatmap, mods);

        // Conversion: pass the mania ruleset so that mania beatmaps are passed through rather than converted.
        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new ManiaBeatmapConverter(beatmap, MANIA);

        public override IBeatmapProcessor? CreateBeatmapProcessor(IBeatmap beatmap) => MANIA.CreateBeatmapProcessor(beatmap);

        // Difficulty: mania's calculator, with the mania RulesetInfo. Star rating follows the converter above. The calculator
        // also compares the beatmap's ruleset online ID with the one it is given to decide whether the map is a convert,
        // which adds the key mods to its list of difficulty mod combinations.
        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => MANIA.CreateDifficultyCalculator(beatmap);

        public override PerformanceCalculator CreatePerformanceCalculator() => MANIA.CreatePerformanceCalculator();

        public override ScoreProcessor CreateScoreProcessor() => MANIA.CreateScoreProcessor();

        public override HealthProcessor CreateHealthProcessor(double drainStartTime) => MANIA.CreateHealthProcessor(drainStartTime);

        public override ScoreMultiplierCalculator CreateScoreMultiplierCalculator(ScoreMultiplierContext context) => MANIA.CreateScoreMultiplierCalculator(context);

        public override IEnumerable<Mod> GetModsFor(ModType type) => MANIA.GetModsFor(type).Select(withHighwayVisuals);

        public override IEnumerable<Mod> ConvertFromLegacyMods(LegacyMods mods) => MANIA.ConvertFromLegacyMods(mods).Select(withHighwayVisuals);

        /// <summary>
        /// Mania's mods, except those whose drawing needs the highway: Flashlight lights the band of the track mania lights in
        /// its flat stage (<see cref="ManiaOrthoModFlashlight"/>). Each replacement inherits the mania mod, so it scores the
        /// same and carries the same acronym.
        /// </summary>
        private static Mod withHighwayVisuals(Mod mod)
        {
            switch (mod)
            {
                case ManiaModFlashlight and not ManiaOrthoModFlashlight:
                    return new ManiaOrthoModFlashlight();

                case MultiMod multi:
                    return new MultiMod(multi.Mods.Select(withHighwayVisuals).ToArray());

                default:
                    return mod;
            }
        }

        public override LegacyMods ConvertToLegacyMods(Mod[] mods) => MANIA.ConvertToLegacyMods(mods);

        public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap) => MANIA.CreateSkinTransformer(skin, beatmap);

        public override IConvertibleReplayFrame CreateConvertibleReplayFrame() => MANIA.CreateConvertibleReplayFrame();

        // Settings are stored under this ruleset's own RulesetInfo, so scroll speed etc. can differ from regular mania.
        public override IRulesetConfigManager CreateConfig(SettingsStore? settings) => new ManiaOrthoRulesetConfigManager(settings, RulesetInfo);

        public override RulesetSettingsSubsection CreateSettings() => new ManiaOrthoSettingsSubsection(this);

        public override LocalisableString VariantDescription => MANIA.VariantDescription;

        public override IEnumerable<int> GameplayVariants => MANIA.GameplayVariants;

        public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) => MANIA.GetDefaultKeyBindings(variant);

        public override LocalisableString GetVariantName(int variant) => MANIA.GetVariantName(variant);

        public override int GetVariantForBeatmap(IBeatmapInfo beatmapInfo, IReadOnlyList<Mod>? mods = null) => MANIA.GetVariantForBeatmap(beatmapInfo, mods);

        public override IEnumerable<HitResult> GetValidHitResults() => MANIA.GetValidHitResults();

        public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap) => MANIA.CreateStatisticsForScore(score, playableBeatmap);

        public override BeatmapDifficulty GetAdjustedDisplayDifficulty(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods) => MANIA.GetAdjustedDisplayDifficulty(beatmapInfo, mods);

        public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
            => MANIA.GetBeatmapAttributesForDisplay(beatmapInfo, mods);

        public override IRulesetFilterCriteria CreateRulesetFilterCriteria() => MANIA.CreateRulesetFilterCriteria();
    }
}
