// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using Realms;

namespace osu.Game.Rulesets.ManiaOrtho.Configuration
{
    /// <summary>
    /// Keeps the key bindings osu! stores for maniaortho the same as osu!mania's, both ways, while it exists (Alpha
    /// 2026-11-04: changing keys matches the gameplay field; DECISIONS "Settings consistent with osu!mania").
    /// </summary>
    /// <remarks>
    /// Gameplay reads osu!mania's bindings directly (<see cref="UI.DrawableManiaOrthoRuleset"/>), so this keeps the maniaortho
    /// section of osu!'s key binding settings, which osu! lists for every ruleset, storing the same keys. When it starts,
    /// <see cref="Merge"/> settles each key count once. After that, a change on one side is copied to the other for the
    /// columns (actions) it changed only, so the echo of a copy, which finds both sides equal, writes nothing and never
    /// carries an older key back over one changed meanwhile. Each action keeps its keys in order, so a second key bound to an
    /// action stays second.
    /// </remarks>
    public sealed class KeyBindingMirror : IDisposable
    {
        private readonly RealmAccess realm;
        private readonly IDisposable maniaSubscription;
        private readonly IDisposable orthoSubscription;

        public KeyBindingMirror(RealmAccess realm)
        {
            this.realm = realm;

            // The first call (no changes) merges; so does one after realm recycled its instance, which is harmless.
            maniaSubscription = realm.RegisterForNotifications(r => bindingsOf(r, ManiaOrthoRuleset.MANIA.ShortName), (sender, changes) =>
            {
                if (changes == null)
                    realm.WriteAsync(Merge);
                else
                    copyChanged(sender, changes, ManiaOrthoRuleset.MANIA.ShortName, ManiaOrthoRuleset.SHORT_NAME);
            });

            orthoSubscription = realm.RegisterForNotifications(r => bindingsOf(r, ManiaOrthoRuleset.SHORT_NAME), (sender, changes) =>
            {
                if (changes != null)
                    copyChanged(sender, changes, ManiaOrthoRuleset.SHORT_NAME, ManiaOrthoRuleset.MANIA.ShortName);
            });
        }

        /// <summary>
        /// Copies the columns whose keys <paramref name="changes"/> touched, read again inside the write. A removed key (rare:
        /// osu!'s rows are changed in place) copies the whole side, as its column is gone from the collection.
        /// </summary>
        private void copyChanged(IRealmCollection<RealmKeyBinding> sender, ChangeSet changes, string from, string to)
        {
            if (changes.DeletedIndices.Length > 0)
            {
                realm.WriteAsync(r => Copy(r, from, to));
                return;
            }

            var slots = changes.InsertedIndices.Concat(changes.NewModifiedIndices).Select(i => key(sender[i])).ToHashSet();

            if (slots.Count > 0)
                realm.WriteAsync(r => Copy(r, from, to, slots.Contains));
        }

        /// <summary>
        /// Settles each key count once, as the mirror or a maniaortho play starts: where only one side was changed from
        /// osu!mania's defaults, its keys are copied to the other (keys set for maniaortho before the two were shared move to
        /// osu!mania, which gameplay reads); otherwise osu!mania's win, and maniaortho's are logged before they are replaced.
        /// </summary>
        public static void Merge(Realm realm)
        {
            var mania = bindingsOf(realm, ManiaOrthoRuleset.MANIA.ShortName).AsEnumerable().ToLookup(b => b.Variant);
            var ortho = bindingsOf(realm, ManiaOrthoRuleset.SHORT_NAME).AsEnumerable().ToLookup(b => b.Variant);
            var defaults = ManiaOrthoRuleset.MANIA;

            foreach (int? variant in mania.Select(g => g.Key).Union(ortho.Select(g => g.Key)).ToArray())
            {
                var maniaKeys = keysByAction(mania[variant]);
                var orthoKeys = keysByAction(ortho[variant]);

                if (sameKeys(maniaKeys, orthoKeys))
                    continue;

                bool orthoChanged = true;
                bool orthoFirst = false;

                if (variant is int v)
                {
                    var defaultKeys = keysByAction(defaults.GetDefaultKeyBindings(v).Select(b => ((int)b.Action, b.KeyCombination.ToString())));
                    orthoChanged = !sameKeys(orthoKeys, defaultKeys);
                    orthoFirst = orthoChanged && sameKeys(maniaKeys, defaultKeys);
                }

                if (orthoChanged && !orthoFirst)
                    Logger.Log($"maniaortho now plays with osu!mania's keys; its own for variant {variant} are replaced: {describe(orthoKeys)}");

                Copy(realm, orthoFirst ? ManiaOrthoRuleset.SHORT_NAME : ManiaOrthoRuleset.MANIA.ShortName,
                    orthoFirst ? ManiaOrthoRuleset.MANIA.ShortName : ManiaOrthoRuleset.SHORT_NAME, slot => slot.Variant == variant);
            }
        }

        /// <summary>
        /// Makes the bindings of the ruleset named <paramref name="to"/> the same keys, in the same order for each variant and
        /// action, as those of <paramref name="from"/>, changing only what differs, for the slots <paramref name="include"/>
        /// accepts (all when null).
        /// </summary>
        /// <returns>Whether anything changed.</returns>
        public static bool Copy(Realm realm, string from, string to, Func<(int? Variant, int Action), bool>? include = null)
        {
            var source = bindingsOf(realm, from).AsEnumerable().ToLookup(key);
            var target = bindingsOf(realm, to).AsEnumerable().ToLookup(key);
            bool changed = false;

            foreach (var slot in source.Select(g => g.Key).Union(target.Select(g => g.Key)).ToArray())
            {
                if (include?.Invoke(slot) == false)
                    continue;

                var wanted = source[slot].ToArray();
                var existing = target[slot].ToArray();

                for (int i = 0; i < Math.Max(wanted.Length, existing.Length); i++)
                {
                    if (i >= wanted.Length)
                        realm.Remove(existing[i]);
                    else if (i >= existing.Length)
                        realm.Add(new RealmKeyBinding(slot.Action, wanted[i].KeyCombination, to, slot.Variant));
                    else if (existing[i].KeyCombinationString != wanted[i].KeyCombinationString)
                        existing[i].KeyCombinationString = wanted[i].KeyCombinationString;
                    else
                        continue;

                    changed = true;
                }
            }

            return changed;
        }

        private static IQueryable<RealmKeyBinding> bindingsOf(Realm realm, string rulesetName) => realm.All<RealmKeyBinding>().Where(b => b.RulesetName == rulesetName);

        private static (int? Variant, int Action) key(RealmKeyBinding binding) => (binding.Variant, binding.ActionInt);

        private static Dictionary<int, string[]> keysByAction(IEnumerable<RealmKeyBinding> bindings)
            => keysByAction(bindings.Select(b => (b.ActionInt, b.KeyCombinationString)));

        private static Dictionary<int, string[]> keysByAction(IEnumerable<(int Action, string Keys)> bindings)
            => bindings.GroupBy(b => b.Action).ToDictionary(g => g.Key, g => g.Select(b => b.Keys).ToArray());

        private static bool sameKeys(Dictionary<int, string[]> a, Dictionary<int, string[]> b)
            => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string[]? other) && pair.Value.SequenceEqual(other));

        private static string describe(Dictionary<int, string[]> keys)
            => string.Join(", ", keys.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}: {string.Join(" / ", pair.Value)}"));

        public void Dispose()
        {
            maniaSubscription.Dispose();
            orthoSubscription.Dispose();
        }
    }
}
