// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Screens;
using osu.Game.Screens.Select;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.ManiaOrtho.Hooks
{
    /// <summary>
    /// The few changes orthomania needs in lazer itself, put in when lazer loads this ruleset, so official lazer runs it unchanged
    /// on disk. Each hook is small, applies only where orthomania is involved, and is skipped (with a line in lazer's log) if a
    /// lazer release no longer has what it hooks, rather than stopping lazer.
    /// </summary>
    public static class LazerHooks
    {
        public const string HARMONY_ID = "orthomania.lazer-hooks";

        private static readonly object installing = new object();
        private static readonly List<string> applied = new List<string>();
        private static readonly List<string> skipped = new List<string>();
        private static bool installed;

        /// <summary>
        /// The hooks in place, by name.
        /// </summary>
        public static IReadOnlyList<string> Applied
        {
            get
            {
                lock (installing)
                    return applied.ToList();
            }
        }

        /// <summary>
        /// The hooks that could not go in, with why.
        /// </summary>
        public static IReadOnlyList<string> Skipped
        {
            get
            {
                lock (installing)
                    return skipped.ToList();
            }
        }

        /// <summary>
        /// Runs before anything in this DLL is used, which is lazer creating the ruleset as it starts.
        /// </summary>
#pragma warning disable CA2255 // Lazer finds this DLL by its file name and creates the ruleset by reflection: there is no other entry point.
        [ModuleInitializer]
        internal static void InstallOnLoad() => Install();
#pragma warning restore CA2255

        public static void Install()
        {
            lock (installing)
            {
                if (installed)
                    return;

                installed = true;

                var harmony = new Harmony(HARMONY_ID);

                apply(harmony, "mania beatmaps play under maniaortho", () =>
                {
                    var target = AccessTools.Method(typeof(BeatmapInfoExtensions), nameof(BeatmapInfoExtensions.AllowGameplayWithRuleset),
                        new[] { typeof(IBeatmapInfo), typeof(RulesetInfo), typeof(bool) });
                    return patch(harmony, target, postfix: nameof(allowGameplayPostfix));
                });

                apply(harmony, "presenting a mania beatmap keeps maniaortho", () =>
                {
                    int compared = presentBeatmapMethods().ToList().Count(t => patch(harmony, t, transpiler: nameof(rulesetMatchesTranspiler)));
                    int handed = screensPresentingBeatmaps().ToList().Count(t => patch(harmony, t, prefix: nameof(keepPlayingRulesetPrefix)));
                    return compared > 0 && handed > 0;
                });

                apply(harmony, "the skin editor saves only once it has a skin", () =>
                {
                    var target = AccessTools.Method(typeof(SkinEditor), nameof(SkinEditor.Save), new[] { typeof(bool) });
                    return AccessTools.Field(typeof(SkinEditor), "currentSkin") != null && patch(harmony, target, prefix: nameof(skinEditorSavePrefix));
                });

                // Only when orthomania.exe started the game and the person did not allow the osu! sign-in (orthomania-game.json).
                if (OrthomaniaData.Current is { OsuSignIn: false })
                    apply(harmony, SIGNED_OUT, () => KeepOsuSignedOut(harmony));
            }
        }

        public const string SIGNED_OUT = "osu! stays signed out";

        /// <summary>
        /// lazer's API is made with <see cref="SignedOutConfig"/> in place of osu!'s settings: it starts without osu!'s saved username
        /// and sign-in, and what it saves (a sign-in in this session) stays in memory. lazer makes its API after it loads the rulesets,
        /// so this is in place in time.
        /// </summary>
        internal static bool KeepOsuSignedOut(Harmony harmony)
        {
            var target = AccessTools.Constructor(typeof(APIAccess), new[] { typeof(OsuGameBase), typeof(OsuConfigManager), typeof(EndpointConfiguration), typeof(string) });
            return target?.GetParameters()[1].Name == "config" && patch(harmony, target, prefix: nameof(signedOutApiPrefix));
        }

        private static void signedOutApiPrefix(ref OsuConfigManager config) => config = new SignedOutConfig();

        private static void apply(Harmony harmony, string name, Func<bool> hook)
        {
            try
            {
                if (hook())
                {
                    applied.Add(name);
                    return;
                }

                skipped.Add($"{name}: not found in this osu! release");
            }
            catch (Exception e)
            {
                skipped.Add($"{name}: {e.GetType().Name}: {e.Message}");
            }

            Logger.Log($"orthomania: could not hook \"{skipped.Last()}\"; playing on without it.", LoggingTarget.Runtime, LogLevel.Important);
        }

        private static bool patch(Harmony harmony, MethodBase? target, string? prefix = null, string? postfix = null, string? transpiler = null)
        {
            if (target == null)
                return false;

            static HarmonyMethod? method(string? name) => name == null ? null : new HarmonyMethod(typeof(LazerHooks), name);

            harmony.Patch(target, method(prefix), method(postfix), method(transpiler));
            return true;
        }

        /// <summary>
        /// Whether a beatmap of <paramref name="beatmapRuleset"/> plays under <paramref name="selected"/> without conversion:
        /// the same ruleset, or an osu!mania beatmap under maniaortho, which plays mania's beatmaps as its own.
        /// </summary>
        public static bool RulesetMatches(RulesetInfo beatmapRuleset, RulesetInfo? selected)
            => beatmapRuleset.Equals(selected) || PlaysAsOwn(beatmapRuleset.ShortName, selected?.ShortName);

        public static bool PlaysAsOwn(string? beatmapRuleset, string? selected)
            => selected == ManiaOrthoRuleset.SHORT_NAME && beatmapRuleset == ManiaOrthoRuleset.PLAYS_BEATMAPS_OF;

        private static void allowGameplayPostfix(IBeatmapInfo beatmap, RulesetInfo ruleset, ref bool __result)
        {
            if (!__result && PlaysAsOwn(beatmap.Ruleset?.ShortName, ruleset?.ShortName))
                __result = true;
        }

        /// <summary>
        /// Song select's and the game's "present this beatmap": where they ask whether the beatmap's ruleset is the selected one,
        /// they ask <see cref="RulesetMatches"/> instead, so presenting an osu!mania beatmap leaves maniaortho selected.
        /// </summary>
        private static IEnumerable<MethodBase> presentBeatmapMethods()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            var songSelect = typeof(SongSelect).GetMethods(all).Where(m => m.Name.EndsWith("IHandlePresentBeatmap.PresentBeatmap", StringComparison.Ordinal));

            // OsuGame.PresentBeatmap compares inside lambdas, which the compiler makes into methods of OsuGame or of nested types.
            var game = new[] { typeof(OsuGame) }.Concat(typeof(OsuGame).GetNestedTypes(all))
                                                .SelectMany(t => t.GetMethods(all))
                                                .Where(m => m.Name.Contains("<PresentBeatmap>", StringComparison.Ordinal));

            return songSelect.Concat(game).Where(m => callsRulesetEquals(m));
        }

        /// <summary>
        /// The screens the game hands a presented beatmap to, with the ruleset to select (<see cref="IHandlePresentBeatmap"/>).
        /// </summary>
        private static IEnumerable<MethodBase> screensPresentingBeatmaps()
        {
            var present = AccessTools.Method(typeof(IHandlePresentBeatmap), nameof(IHandlePresentBeatmap.PresentBeatmap));

            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(OsuGame).Assembly))
            {
                if (type.IsInterface || type.IsAbstract || !typeof(IHandlePresentBeatmap).IsAssignableFrom(type))
                    continue;

                var map = type.GetInterfaceMap(typeof(IHandlePresentBeatmap));
                int index = Array.IndexOf(map.InterfaceMethods, present);

                if (index >= 0 && map.TargetMethods[index].DeclaringType == type)
                    yield return map.TargetMethods[index];
            }
        }

        /// <summary>
        /// An osu!mania beatmap presented while maniaortho is selected is handed over with maniaortho, not osu!mania.
        /// </summary>
        private static void keepPlayingRulesetPrefix(object __instance, ref RulesetInfo ruleset)
        {
            var selected = (__instance as OsuScreen)?.Ruleset?.Value;

            if (selected != null && ruleset != null && PlaysAsOwn(ruleset.ShortName, selected.ShortName))
                ruleset = selected;
        }

        private static readonly MethodInfo ruleset_equals = AccessTools.Method(typeof(RulesetInfo), nameof(RulesetInfo.Equals), new[] { typeof(RulesetInfo) });

        private static readonly MethodInfo ruleset_matches = AccessTools.Method(typeof(LazerHooks), nameof(RulesetMatches));

        private static bool callsRulesetEquals(MethodBase method)
        {
            try
            {
                return PatchProcessor.ReadMethodBody(method).Any(i => i.Value is MethodInfo called && called == ruleset_equals);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IEnumerable<CodeInstruction> rulesetMatchesTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) && instruction.operand is MethodInfo called && called == ruleset_equals)
                    yield return new CodeInstruction(OpCodes.Call, ruleset_matches).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
                else
                    yield return instruction;
            }
        }

        /// <summary>
        /// Saving before the editor has its skin (closing it while it loads) would throw.
        /// </summary>
        private static bool skinEditorSavePrefix(SkinEditor __instance)
        {
            var field = AccessTools.Field(typeof(SkinEditor), "currentSkin");
            return field?.GetValue(__instance) is Bindable<Skin>;
        }
    }
}
