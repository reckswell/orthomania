// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// How an <see cref="OrthoSettingsGroup"/> heads its settings.
    /// </summary>
    public enum SettingsGroupStyle
    {
        /// <summary>
        /// A large heading over groups ("Settings: orthomania", "Game preset").
        /// </summary>
        Section,

        /// <summary>
        /// A heading that folds its settings away when clicked (Alpha 2026-11-08: "not a long list of things to scroll through").
        /// </summary>
        Fold,

        /// <summary>
        /// A small heading inside a fold.
        /// </summary>
        Label,
    }

    /// <summary>
    /// A heading and the settings under it, in this ruleset's part of the settings panel (Alpha 2026-11-08). While the settings
    /// are searched it shows when its name or any of its settings matches, folded or not, and a match on its name shows all of
    /// its settings.
    /// </summary>
    public partial class OrthoSettingsGroup : FillFlowContainer, IConditionalFilterable
    {
        /// <summary>
        /// Whether the group can show at all (the wall's, only under a game with a wall); a search then never shows it either.
        /// </summary>
        public BindableBool CanBeShown { get; } = new BindableBool(true);

        IBindable<bool> IConditionalFilterable.CanBeShown => CanBeShown;

        public LocalisableString Title { get; }

        public SettingsGroupStyle Style { get; }

        /// <summary>
        /// The name a fold is remembered by (<see cref="Configuration.HighwaySetting.FoldedGroups"/>).
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Whether a fold shows its settings; always true for the other styles.
        /// </summary>
        public readonly BindableBool Expanded = new BindableBool(true);

        /// <summary>
        /// Whether its settings are shown now: unfolded, or while the settings are searched.
        /// </summary>
        public bool ContentShown => content.Alpha > 0;

        protected override Container<Drawable> Content => content;

        private readonly FillFlowContainer content;

        private bool matchingFilter = true;
        private bool filteringActive;

        public OrthoSettingsGroup(LocalisableString title, SettingsGroupStyle style, string? key = null)
        {
            Title = title;
            Style = style;
            Key = key ?? title.ToString();

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Direction = FillDirection.Vertical;

            content = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, SettingsSection.ITEM_SPACING_V2),
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddRangeInternal(new Drawable[]
            {
                Style == SettingsGroupStyle.Fold ? new FoldHeader(Title, Expanded) : new Heading(Title, Style),
                content,
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Outside a search too.
            CanBeShown.BindValueChanged(e => Alpha = e.NewValue ? 1 : 0, true);

            Expanded.BindValueChanged(e =>
            {
                updateContent();

                if (e.NewValue)
                    content.FadeInFromZero(200, Easing.OutQuint);
            }, true);
        }

        private void updateContent()
        {
            bool shown = Style != SettingsGroupStyle.Fold || Expanded.Value || filteringActive;

            // Hidden at once, so the panel closes up; a hidden container's settings stay bound to their values.
            content.ClearTransforms();
            content.Alpha = shown ? 1 : 0;
        }

        public IEnumerable<LocalisableString> FilterTerms => new[] { Title };

        public bool MatchingFilter
        {
            set
            {
                if (matchingFilter == value)
                    return;

                matchingFilter = value;
                Invalidate(Invalidation.Presence);
            }
        }

        public bool FilteringActive
        {
            get => filteringActive;
            set
            {
                if (filteringActive == value)
                    return;

                filteringActive = value;
                Invalidate(Invalidation.Presence);

                if (IsLoaded)
                    updateContent();
            }
        }

        public override bool IsPresent => base.IsPresent && (!filteringActive || matchingFilter);

        private partial class Heading : CompositeDrawable
        {
            private readonly LocalisableString text;
            private readonly SettingsGroupStyle style;

            public Heading(LocalisableString text, SettingsGroupStyle style)
            {
                this.text = text;
                this.style = style;
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider? colourProvider, OsuColour colours)
            {
                bool section = style == SettingsGroupStyle.Section;

                InternalChild = new OsuSpriteText
                {
                    Text = text,
                    Font = OsuFont.GetFont(size: section ? 24 : 14, weight: FontWeight.Bold),
                    Colour = section ? colourProvider?.Content1 ?? colours.Gray9 : colourProvider?.Content2 ?? colours.Gray8,
                    Margin = new MarginPadding { Top = section ? 24 : 6, Bottom = section ? 2 : 0 },
                    Padding = SettingsPanel.CONTENT_PADDING,
                };
            }
        }

        /// <summary>
        /// A fold's heading: its name and a chevron, which point down while its settings show. Clicking anywhere on it folds or
        /// unfolds them.
        /// </summary>
        private partial class FoldHeader : OsuClickableContainer
        {
            private readonly LocalisableString text;
            private readonly BindableBool expanded;

            private OsuSpriteText title = null!;
            private SpriteIcon chevron = null!;

            [Resolved]
            private OverlayColourProvider? colourProvider { get; set; }

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            public FoldHeader(LocalisableString text, BindableBool expanded)
            {
                this.text = text;
                this.expanded = expanded;
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Action = () => expanded.Toggle();
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Margin = new MarginPadding { Top = 10, Bottom = 2 },
                    Children = new Drawable[]
                    {
                        title = new OsuSpriteText
                        {
                            Text = text,
                            Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                        },
                        chevron = new SpriteIcon
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.Centre,
                            X = -8,
                            Size = new Vector2(12),
                            Icon = FontAwesome.Solid.ChevronDown,
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                expanded.BindValueChanged(e => chevron.RotateTo(e.NewValue ? 0 : -90, 200, Easing.OutQuint), true);
                updateColour();
                FinishTransforms(true);
            }

            protected override bool OnHover(HoverEvent e)
            {
                updateColour();
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                updateColour();
                base.OnHoverLost(e);
            }

            private void updateColour()
            {
                var colour = IsHovered ? colourProvider?.Content1 ?? colours.Gray9 : colourProvider?.Highlight1 ?? colours.Yellow;
                title.FadeColour(colour, 100);
                chevron.FadeColour(colour, 100);
            }
        }
    }
}
