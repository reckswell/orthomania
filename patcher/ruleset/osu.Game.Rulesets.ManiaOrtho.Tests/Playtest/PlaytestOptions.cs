// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.Mods;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Playtest
{
    /// <summary>
    /// Options for <see cref="TestScenePlaytest"/>. Read from environment variables so the same scene works from
    /// the capture runner (<c>playtest</c> command), the visual test browser and <c>dotnet test</c>.
    /// </summary>
    public class PlaytestOptions
    {
        public const string ENV_BEATMAP = "ORTHO_PLAYTEST_BEATMAP";
        public const string ENV_RULESET = "ORTHO_PLAYTEST_RULESET";
        public const string ENV_TIMES = "ORTHO_PLAYTEST_TIMES";
        public const string ENV_OUT = "ORTHO_PLAYTEST_OUT";
        public const string ENV_SCROLL = "ORTHO_PLAYTEST_SCROLL_SPEED";
        public const string ENV_FULL = "ORTHO_PLAYTEST_PLAY_TO_END";
        public const string ENV_PRESET = "ORTHO_PLAYTEST_PRESET";
        public const string ENV_WALL = "ORTHO_PLAYTEST_WALL";
        public const string ENV_SPEED_LOCK = "ORTHO_PLAYTEST_SPEED_LOCK";
        public const string ENV_SET = "ORTHO_PLAYTEST_SET";
        public const string ENV_SKIN = "ORTHO_PLAYTEST_SKIN";
        public const string ENV_NOTES = "ORTHO_PLAYTEST_NOTES";
        public const string ENV_MODS = "ORTHO_PLAYTEST_MODS";
        public const string ENV_PORT = "ORTHO_PLAYTEST_PORT";
        public const string ENV_HUMANISED = "ORTHO_PLAYTEST_HUMANISED";
        public const string ENV_GAME_SET = "ORTHO_PLAYTEST_GAME_SET";

        /// <summary>See <see cref="PlaytestBeatmaps.Load"/>.</summary>
        public string Beatmap { get; init; } = "synthetic-7k";

        /// <summary><c>maniaortho</c> or <c>mania</c> (for side-by-side baselines).</summary>
        public string Ruleset { get; init; } = ManiaOrthoRuleset.SHORT_NAME;

        /// <summary>Gameplay times (ms) at which to freeze and screenshot. Empty means 20/45/70/90% through the chart.</summary>
        public double[] Times { get; init; } = Array.Empty<double>();

        /// <summary>Where screenshots and summary.json are written. Null disables capture (e.g. headless runs).</summary>
        public string? OutputDirectory { get; init; }

        /// <summary>Mania scroll speed setting (1-40). Null keeps the default.</summary>
        public double? ScrollSpeed { get; init; }

        /// <summary>Whether to keep playing to the end and record final statistics.</summary>
        public bool PlayToEnd { get; init; }

        /// <summary>
        /// The highway preset to play with (maniaortho only). Defaults to the ruleset's default preset.
        /// </summary>
        public HighwayPreset Preset { get; init; } = HighwayPresets.DEFAULT;

        /// <summary>
        /// The wall position, if overriding the preset's (maniaortho only).
        /// </summary>
        public float? Wall { get; init; }

        /// <summary>
        /// Whether moving the wall keeps the note speed instead of the visible time (maniaortho only).
        /// </summary>
        public bool SpeedLock { get; init; }

        /// <summary>
        /// One of the built-in skins: argon (the default), argon-pro, triangles or classic (the legacy skin), or the path of a
        /// stable skin's folder, which is imported into the test game's own storage (the folder itself is only read).
        /// </summary>
        public string Skin { get; init; } = "argon";

        /// <summary>
        /// Whether <see cref="Skin"/> is a skin folder to import.
        /// </summary>
        public bool SkinIsFolder => Directory.Exists(Skin);

        /// <summary>
        /// Whether maniaortho draws the skin with the skin porter: notes, track and hit effects all "Skin (ported)".
        /// </summary>
        public bool Port { get; init; }

        /// <summary>
        /// How maniaortho draws notes (skin or highway).
        /// </summary>
        public HighwayNoteStyle NoteStyle { get; init; } = HighwayNoteStyle.Highway;

        /// <summary>
        /// Mod acronyms to play with besides autoplay (and no fail), such as HD, FI or CO.
        /// </summary>
        public string[] Mods { get; init; } = Array.Empty<string>();

        /// <summary>
        /// When set, plays the parity tests' humanised replay with this seed (<see cref="HumanisedReplayGenerator"/>: presses
        /// spread around each note, so Greats, Goods, Mehs and a few misses show) instead of autoplay, which only hits Perfects.
        /// The scene always plays with No Fail, so the misses never end the play.
        /// </summary>
        public int? HumanisedSeed { get; init; }

        /// <summary>
        /// Highway settings to set after the preset and wall (maniaortho only), as <c>Name=value</c> with names from
        /// <see cref="HighwaySetting"/>, e.g. <c>JudgementLineHeight=0.3</c>.
        /// </summary>
        public string[] HighwayValues { get; init; } = Array.Empty<string>();

        /// <summary>
        /// osu!'s own settings and osu!mania's ruleset settings to set before the player loads, in both rulesets (the parity
        /// checks of the osu fork's docs/ortho/SETTINGS-PARITY.md), as <c>Name=value</c> with names from <see cref="OsuSetting"/> or
        /// <see cref="ManiaRulesetSetting"/> and values as osu! writes them to its settings, e.g.
        /// <c>KeyOverlay=true,HUDVisibilityMode=Never,TimingBasedNoteColouring=true</c>.
        /// </summary>
        public string[] GameValues { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Sets <see cref="GameValues"/>. A ruleset setting the ruleset fixes (maniaortho's scroll direction, which its camera
        /// sets) keeps its value, so one list serves both rulesets.
        /// </summary>
        public void ApplyGameValues(OsuConfigManager game, ManiaRulesetConfigManager ruleset)
        {
            foreach (string assignment in GameValues)
            {
                (string name, string value) = parseGameValue(assignment);

                if (Enum.TryParse(name, true, out ManiaRulesetSetting maniaSetting) && Enum.IsDefined(maniaSetting))
                    setFromText(ruleset, maniaSetting, value);
                else
                    setFromText(game, Enum.Parse<OsuSetting>(name, true), value);
            }
        }

        /// <summary>
        /// Splits <paramref name="assignment"/> and checks its name, so a typo fails before any run starts.
        /// </summary>
        private static (string name, string value) parseGameValue(string assignment)
        {
            string[] parts = assignment.Split('=', 2, StringSplitOptions.TrimEntries);

            bool known = parts.Length == 2
                         && ((Enum.TryParse(parts[0], true, out ManiaRulesetSetting mania) && Enum.IsDefined(mania))
                             || (Enum.TryParse(parts[0], true, out OsuSetting osu) && Enum.IsDefined(osu)));

            if (!known)
                throw new ArgumentException($"\"{assignment}\" is not Name=value with a name from {nameof(OsuSetting)} or {nameof(ManiaRulesetSetting)}.");

            return (parts[0], parts[1]);
        }

        /// <summary>
        /// Puts every osu!mania ruleset setting but the scroll speed back to its default (those the ruleset fixes excepted),
        /// so values a <see cref="GameValues"/> run set do not carry into the next: the ruleset's settings are kept between
        /// runs, unlike osu!'s own.
        /// </summary>
        public static void ResetRulesetValues(ManiaRulesetConfigManager ruleset)
        {
            foreach (var (lookup, bindable) in storeOf(ruleset))
            {
                if (lookup == ManiaRulesetSetting.ScrollSpeed || bindable.Disabled)
                    continue;

                bindable.GetType().GetMethod(nameof(Bindable<int>.SetDefault), Type.EmptyTypes)?.Invoke(bindable, null);
            }
        }

        /// <summary>
        /// Sets a setting from text as osu! reads its settings files, through its bindable, whatever its type. A setting its
        /// config fixes keeps its value.
        /// </summary>
        private static void setFromText<TLookup>(ConfigManager<TLookup> config, TLookup lookup, string value)
            where TLookup : struct, Enum
        {
            if (!storeOf(config).TryGetValue(lookup, out var bindable) || bindable is not IParseable parseable)
                throw new ArgumentException($"{lookup} is not a setting {config.GetType().Name} can set from text.");

            if (!bindable.Disabled)
                parseable.Parse(value, CultureInfo.InvariantCulture);
        }

        private static IDictionary<TLookup, IBindable> storeOf<TLookup>(ConfigManager<TLookup> config)
            where TLookup : struct, Enum
            => typeof(ConfigManager<TLookup>).GetField("ConfigStore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(config) as IDictionary<TLookup, IBindable>
               ?? throw new InvalidOperationException($"Cannot read the settings of {config.GetType().Name}.");

        public void ApplyHighwayValues(HighwayConfigManager config)
        {
            foreach (string assignment in HighwayValues)
                applyHighwayValue(config, assignment);
        }

        /// <summary>
        /// Parses <paramref name="assignment"/> and, given a <paramref name="config"/>, sets it. Without one it only checks it,
        /// so a typo fails before any run starts.
        /// </summary>
        private static void applyHighwayValue(HighwayConfigManager? config, string assignment)
        {
            string[] parts = assignment.Split('=', 2, StringSplitOptions.TrimEntries);

            if (parts.Length != 2 || !Enum.TryParse(parts[0], true, out HighwaySetting setting) || !Enum.IsDefined(setting))
                throw new ArgumentException($"\"{assignment}\" is not Name=value with a name from {nameof(HighwaySetting)}.");

            string value = parts[1];

            switch (setting)
            {
                case HighwaySetting.Preset:
                    var preset = Enum.Parse<HighwayPreset>(value, true);
                    config?.SetValue(setting, preset);
                    break;

                case HighwaySetting.CameraMode:
                    var mode = Enum.Parse<HighwayCameraMode>(value, true);
                    config?.SetValue(setting, mode);
                    break;

                case HighwaySetting.NoteStyle:
                    var style = Enum.Parse<HighwayNoteStyle>(value, true);
                    config?.SetValue(setting, style);
                    break;

                case HighwaySetting.HitEffects:
                    var effects = Enum.Parse<HighwayHitEffectStyle>(value, true);
                    config?.SetValue(setting, effects);
                    break;

                case HighwaySetting.Track:
                    var track = Enum.Parse<HighwayTrackStyle>(value, true);
                    config?.SetValue(setting, track);
                    break;

                case HighwaySetting.KeyArea:
                    var keyArea = Enum.Parse<SkinKeyArea>(value, true);
                    config?.SetValue(setting, keyArea);
                    break;

                case HighwaySetting.StageFit:
                    var fit = Enum.Parse<SkinStageFit>(value, true);
                    config?.SetValue(setting, fit);
                    break;

                case HighwaySetting.KeyBeam:
                    var beam = Enum.Parse<KeyBeamLength>(value, true);
                    config?.SetValue(setting, beam);
                    break;

                case HighwaySetting.WallScale:
                    var scale = Enum.Parse<WallScale>(value, true);
                    config?.SetValue(setting, scale);
                    break;

                case HighwaySetting.SpeedScale:
                    var speedScale = Enum.Parse<SpeedScale>(value, true);
                    config?.SetValue(setting, speedScale);
                    break;

                case HighwaySetting.WidthMode:
                    var widthMode = Enum.Parse<HighwayWidthMode>(value, true);
                    config?.SetValue(setting, widthMode);
                    break;

                case HighwaySetting.WallKeepsVisibleTime:
                case HighwaySetting.LaneGuides:
                case HighwaySetting.SkinHitTarget:
                case HighwaySetting.SkinNoteProportions:
                case HighwaySetting.SkinPortDefaultsApplied:
                case HighwaySetting.SkinLayoutDefaultApplied:
                case HighwaySetting.ShareManiaKeys:
                    bool flag = bool.Parse(value);
                    config?.SetValue(setting, flag);
                    break;

                default:
                    float number = float.Parse(value, CultureInfo.InvariantCulture);
                    config?.SetValue(setting, number);
                    break;
            }
        }

        /// <summary>
        /// The mods to load the player with. Leaves No Fail out: the scene always adds it (<c>PlayerTestScene.AllowFail</c>),
        /// and a second one would fail the player's load.
        /// </summary>
        public Mod[] CreateMods(Ruleset ruleset) => Mods.Where(acronym => !acronym.Equals("NF", StringComparison.OrdinalIgnoreCase))
                                                        .Select(acronym => ruleset.CreateModFromAcronym(acronym)
                                                                           ?? throw new ArgumentException($"{ruleset.Description} has no mod \"{acronym}\".")).ToArray();

        public SkinInfo CreateSkinInfo() => Skin switch
        {
            "argon" => ArgonSkin.CreateInfo(),
            "argon-pro" => ArgonProSkin.CreateInfo(),
            "triangles" => TrianglesSkin.CreateInfo(),
            "classic" => DefaultLegacySkin.CreateInfo(),
            _ => throw new ArgumentException($"Unknown skin \"{Skin}\" (expected argon, argon-pro, triangles, classic or a skin folder)."),
        };

        public Ruleset CreateRuleset() => Ruleset switch
        {
            ManiaOrthoRuleset.SHORT_NAME => new ManiaOrthoRuleset(),
            "mania" => new ManiaRuleset(),
            _ => throw new ArgumentException($"Unknown ruleset \"{Ruleset}\" (expected {ManiaOrthoRuleset.SHORT_NAME} or mania)."),
        };

        public static PlaytestOptions FromEnvironment()
        {
            string? get(string name) => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? null : Environment.GetEnvironmentVariable(name);

            var defaults = new PlaytestOptions();

            var options = new PlaytestOptions
            {
                Beatmap = get(ENV_BEATMAP) ?? defaults.Beatmap,
                Ruleset = get(ENV_RULESET) ?? defaults.Ruleset,
                Times = get(ENV_TIMES)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                      .Select(t => double.Parse(t, CultureInfo.InvariantCulture))
                                      .OrderBy(t => t)
                                      .ToArray() ?? defaults.Times,
                OutputDirectory = get(ENV_OUT),
                ScrollSpeed = get(ENV_SCROLL) is string s ? double.Parse(s, CultureInfo.InvariantCulture) : null,
                PlayToEnd = get(ENV_FULL) == "1",
                Preset = get(ENV_PRESET) is string p ? Enum.Parse<HighwayPreset>(p, true) : defaults.Preset,
                Wall = get(ENV_WALL) is string w ? float.Parse(w, CultureInfo.InvariantCulture) : null,
                SpeedLock = get(ENV_SPEED_LOCK) == "1",
                HighwayValues = get(ENV_SET)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? defaults.HighwayValues,
                Skin = get(ENV_SKIN) ?? defaults.Skin,
                NoteStyle = get(ENV_NOTES) is string n ? Enum.Parse<HighwayNoteStyle>(n, true) : defaults.NoteStyle,
                Mods = get(ENV_MODS)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? defaults.Mods,
                Port = get(ENV_PORT) == "1",
                HumanisedSeed = get(ENV_HUMANISED) is string seed ? int.Parse(seed, CultureInfo.InvariantCulture) : null,
                GameValues = get(ENV_GAME_SET)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? defaults.GameValues,
            };

            foreach (string assignment in options.GameValues)
                parseGameValue(assignment);

            // Fail on a bad --set in every run, including the mania baseline that comes first.
            foreach (string assignment in options.HighwayValues)
                applyHighwayValue(null, assignment);

            return options;
        }
    }
}
