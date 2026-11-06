// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// Gameplay hotkeys for the wall, next to mania's scroll speed keys: Shift with the key for "Decrease scroll speed" or
    /// "Increase scroll speed" (osu!'s key bindings, F3 and F4 by default) moves it by one step of the chosen
    /// <see cref="WallScale"/> (5%, one Chunithm step, 50 white number or 5% of the lane; <see cref="WallScales.KeyStep"/>),
    /// and a toast shows the new value (<see cref="HighwayToasts"/>) as it does for the scroll speed.
    /// </summary>
    /// <remarks>
    /// It only watches the keys and never handles them, so every key press goes on to exactly what it reaches in mania (a
    /// column bound to F4 is still pressed by Shift+F4). It sits on the drawable ruleset, outside the ruleset's input
    /// manager, so nothing of it reaches a replay. Shift is read from the keyboard state, not from the key events this
    /// receives, so the keys also work while watching a replay, where osu! takes Shift itself (to fast-forward). The keys
    /// follow the scroll speed's bindings, as they change (Alpha 2026-11-04: settings consistent with mania): osu!'s key
    /// binding settings list only global actions and the ruleset's own, and adding one to the ruleset's would put it into
    /// mania's replays. Unlike the scroll speed, the wall can be moved at any point of a song, as lane covers can in the arcade
    /// games; it never changes what is judged.
    /// </remarks>
    public partial class HighwayHotkeys : Component
    {
        /// <summary>
        /// How far one press moves the wall with <see cref="WallScale.Percent"/>, as a share of the track's length.
        /// </summary>
        public const float WALL_STEP = 0.05f;

        private readonly HighwayConfigManager config;
        private readonly Bindable<float> wall;

        [Resolved]
        private RealmAccess? realm { get; set; }

        private IDisposable? bindingsSubscription;

        private KeyCombination[] lowerKeys = { new KeyCombination(InputKey.F3) };
        private KeyCombination[] raiseKeys = { new KeyCombination(InputKey.F4) };

        public HighwayHotkeys(HighwayConfigManager config)
        {
            this.config = config;
            wall = config.GetBindable<float>(HighwaySetting.Wall);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            int decrease = (int)GlobalAction.DecreaseScrollSpeed;
            int increase = (int)GlobalAction.IncreaseScrollSpeed;

            bindingsSubscription = realm?.RegisterForNotifications(
                r => r.All<RealmKeyBinding>().Where(b => b.RulesetName == null && b.Variant == null && (b.ActionInt == decrease || b.ActionInt == increase)),
                (bindings, _) =>
                {
                    // An action with none stored (a game that never registered its bindings, as some tests) keeps osu!'s default.
                    lowerKeys = keysFor(bindings, decrease, InputKey.F3);
                    raiseKeys = keysFor(bindings, increase, InputKey.F4);
                });
        }

        private static KeyCombination[] keysFor(IEnumerable<RealmKeyBinding> bindings, int action, InputKey fallback)
        {
            var keys = bindings.Where(b => b.ActionInt == action).Select(b => b.KeyCombination).ToArray();
            return keys.Length > 0 ? keys : new[] { new KeyCombination(fallback) };
        }

        /// <summary>
        /// The bindings the wall keys follow: Shift with each of these lowers or raises it.
        /// </summary>
        public IReadOnlyList<KeyCombination> LowerKeys => lowerKeys;

        /// <inheritdoc cref="LowerKeys"/>
        public IReadOnlyList<KeyCombination> RaiseKeys => raiseKeys;

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            // Held keys repeat, as the scroll speed's do.
            if (!e.ShiftPressed)
                return false;

            var key = KeyCombination.FromKey(e.Key);

            if (!hasKey(lowerKeys, key) && !hasKey(raiseKeys, key))
                return false;

            // What is held besides Shift, as the scroll speed's binding would see it (its modifiers must match exactly). A
            // binding that itself includes Shift can never match.
            var pressed = new KeyCombination(KeyCombination.FromInputState(e.CurrentState).Keys.RemoveAll(k => k is InputKey.Shift or InputKey.LShift or InputKey.RShift));

            // The setting's own range clamps the wall (none, to the most the scroll allows).
            int steps = matches(lowerKeys, key, pressed, e) ? -1 : matches(raiseKeys, key, pressed, e) ? 1 : 0;

            if (steps == 0)
                return false;

            var settings = config.Current;
            wall.Value = WallScales.Step(settings.WallScale, wall.Value, steps, settings.ToLayout().FarWidthRatio);

            // Seen, never handled: the key goes on to everything it would reach in mania.
            return false;
        }

        private static bool hasKey(KeyCombination[] bindings, InputKey key)
        {
            foreach (var binding in bindings)
            {
                if (binding.Keys.Contains(key))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="key"/>, just pressed with <paramref name="pressed"/> held (Shift aside), completes one of
        /// <paramref name="bindings"/>.
        /// </summary>
        private static bool matches(KeyCombination[] bindings, InputKey key, KeyCombination pressed, KeyDownEvent e)
        {
            foreach (var binding in bindings)
            {
                if (binding.Keys.Contains(key) && binding.IsPressed(pressed, e.CurrentState, KeyCombinationMatchingMode.Modifiers))
                    return true;
            }

            return false;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            bindingsSubscription?.Dispose();
        }
    }
}
