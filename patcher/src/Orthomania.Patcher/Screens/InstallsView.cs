using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using Orthomania.Patcher.Graphics;
using Orthomania.Patcher.Installs;
using osuTK;
using osuTK.Graphics;

namespace Orthomania.Patcher.Screens
{
    public interface IInstallsActions
    {
        void UseInstall(OsuInstall install);
        void ChooseFolder();
        void SearchAgain();
        void HideInstalls();
    }

    /// <summary>
    /// The osu! installs found on this computer: pick the lazer orthomania runs on, search again, or choose a folder by hand.
    /// </summary>
    public partial class InstallsView : VisibilityContainer
    {
        public const int MAX_ROWS = 5;

        private readonly FillFlowContainer rows;
        private readonly TextFlowContainer message;
        private readonly PatcherButton search;

        public InstallsView(IInstallsActions actions)
        {
            RelativeSizeAxes = Axes.Both;

            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Palette.BACKGROUND6 },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Horizontal = 30, Top = 22 },
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "osu! installs",
                            Font = PatcherFont.Get(26, FontWeight.Light),
                            Colour = Palette.CONTENT2,
                        },
                        message = new TextFlowContainer(t =>
                        {
                            t.Font = PatcherFont.Get(12);
                            t.Colour = Palette.FOREGROUND1;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            TextAnchor = Anchor.TopCentre,
                            Margin = new MarginPadding { Bottom = 4 },
                        },
                        rows = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 6),
                            Margin = new MarginPadding { Bottom = 6 },
                        },
                        button("Choose a folder...", Palette.BACKGROUND2, actions.ChooseFolder),
                        search = button("Search this computer again", Palette.BACKGROUND2, actions.SearchAgain),
                        button("Back", Palette.BACKGROUND1, actions.HideInstalls),
                    },
                },
            };

            this.actions = actions;
        }

        private readonly IInstallsActions actions;

        private static PatcherButton button(string text, Color4 colour, Action action) => new PatcherButton(text, colour, action, 340)
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Height = 30,
        };

        /// <param name="installs">Everything found, lazer first.</param>
        /// <param name="current">The lazer folder orthomania runs on now, if any.</param>
        public void SetInstalls(IReadOnlyList<OsuInstall> installs, string? current)
        {
            rows.Clear();

            var shown = installs.OrderBy(i => i.Kind).Take(MAX_ROWS).ToList();

            if (shown.Count == 0)
            {
                rows.Add(new SpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = "No osu! found yet.",
                    Font = PatcherFont.Get(14),
                    Colour = Palette.FOREGROUND1,
                });
                return;
            }

            foreach (var install in shown)
            {
                bool inUse = current != null && install.Kind == OsuKind.Lazer && install.Folder.Equals(current, PathUtils.Comparison);
                rows.Add(new InstallRow(install, inUse, () => actions.UseInstall(install)));
            }
        }

        public void SetMessage(string text) => message.Text = text;

        public void SetSearching(bool searching)
        {
            search.Text = searching ? "Searching..." : "Search this computer again";
            search.Alpha = searching ? 0.6f : 1;
        }

        protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);

        protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);

        /// <summary>
        /// One install: its name and release, and its folder below. Lazer can be picked; stable is shown for reference.
        /// </summary>
        private partial class InstallRow : ClickableContainer
        {
            private readonly Box background;
            private readonly Color4 colour;
            private readonly bool pickable;

            public InstallRow(OsuInstall install, bool inUse, Action action)
            {
                pickable = install.Kind == OsuKind.Lazer && !inUse;
                colour = inUse ? Palette.PINK_DARKER : install.Kind == OsuKind.Lazer ? Palette.BACKGROUND2 : Palette.BACKGROUND4;

                Anchor = Anchor.TopCentre;
                Origin = Anchor.TopCentre;
                Size = new Vector2(380, 40);
                Masking = true;
                CornerRadius = 5;
                Action = action;

                string name = install.Release != null ? $"{install.Name} {install.Release}" : install.Name;

                if (inUse)
                    name += "  (orthomania uses this one)";
                else if (install.Kind == OsuKind.Stable)
                    name += "  (for reference)";

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Horizontal = 12, Vertical = 4 },
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = name,
                                Font = PatcherFont.Get(14, FontWeight.SemiBold),
                                Colour = Color4.White,
                                RelativeSizeAxes = Axes.X,
                                Truncate = true,
                            },
                            new SpriteText
                            {
                                Text = install.Folder,
                                Font = PatcherFont.Get(11),
                                Colour = Palette.FOREGROUND1,
                                RelativeSizeAxes = Axes.X,
                                Truncate = true,
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (pickable)
                    background.FadeColour(colour.Lighten(0.2f), 200, Easing.OutQuint);

                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(colour, 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
