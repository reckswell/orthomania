// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input.Bindings;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using Realms;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// osu!mania's input manager, reading the keys bound for osu!mania while "Share osu!mania's keys" is on (Alpha 2026-11-04
    /// and 2026-11-07), and maniaortho's own otherwise.
    /// </summary>
    /// <remarks>
    /// Only where the keys are looked up changes: they map to the same actions, and everything after that, judgement and
    /// replays included, is mania's own. The setting is read as the keys load, which is after the input manager is created
    /// (the drawable ruleset creates it before its config exists), and a change reloads them.
    /// </remarks>
    public partial class ManiaOrthoInputManager : ManiaInputManager
    {
        public ManiaOrthoInputManager(RulesetInfo ruleset, int variant)
            : base(ruleset, variant)
        {
        }

        protected override KeyBindingContainer<ManiaAction> CreateKeyBindingContainer(RulesetInfo ruleset, int variant, SimultaneousBindingMode unique)
            => new SharedKeyBindingContainer(ruleset, variant, unique);

        private partial class SharedKeyBindingContainer : RulesetKeyBindingContainer
        {
            private readonly int variant;

            private readonly Bindable<bool> shared = new Bindable<bool>(true);

            [Resolved]
            private RealmAccess realm { get; set; } = null!;

            [Resolved]
            private ManiaOrthoRulesetConfigManager? config { get; set; }

            private IDisposable? maniaSubscription;

            /// <summary>
            /// Set while osu!mania's keys are being loaded, the only keys loaded while shared.
            /// </summary>
            private bool loadingManiaKeys;

            public SharedKeyBindingContainer(RulesetInfo ruleset, int variant, SimultaneousBindingMode unique)
                : base(ruleset, variant, unique)
            {
                this.variant = variant;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                config?.Highway.BindWith(HighwaySetting.ShareManiaKeys, shared);
            }

            protected override void LoadComplete()
            {
                // The base follows maniaortho's own keys; osu!mania's are followed here while shared. A notification reloads
                // from its own collection, never from realm.Realm: osu! calls every subscription with an empty one while it
                // blocks realm (compacting, moving the data folder), when the update thread's realm cannot be read.
                maniaSubscription = realm.RegisterForNotifications(maniaBindings, (sender, _) =>
                {
                    if (shared.Value)
                        loadManiaKeys(sender.AsQueryable());
                });

                base.LoadComplete();

                if (shared.Value)
                    loadManiaKeys(maniaBindings(realm.Realm));

                shared.BindValueChanged(e =>
                {
                    if (e.NewValue)
                        loadManiaKeys(maniaBindings(realm.Realm));
                    else
                        ReloadMappings();
                });
            }

            /// <summary>
            /// Loads maniaortho's own keys when they are not shared. While shared, the base's loads (from its subscription to
            /// maniaortho's keys) are skipped and osu!mania's are loaded instead.
            /// </summary>
            protected override void ReloadMappings(IQueryable<RealmKeyBinding> realmKeyBindings)
            {
                if (shared.Value && !loadingManiaKeys)
                    return;

                base.ReloadMappings(realmKeyBindings);
            }

            private void loadManiaKeys(IQueryable<RealmKeyBinding> maniaKeys)
            {
                loadingManiaKeys = true;

                try
                {
                    ReloadMappings(maniaKeys);
                }
                finally
                {
                    loadingManiaKeys = false;
                }
            }

            private IQueryable<RealmKeyBinding> maniaBindings(Realm r)
                => r.All<RealmKeyBinding>().Where(b => b.RulesetName == ManiaRuleset.SHORT_NAME && b.Variant == variant);

            protected override void Dispose(bool isDisposing)
            {
                maniaSubscription?.Dispose();
                base.Dispose(isDisposing);
            }
        }
    }
}
