using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using Orthomania.Patcher.Graphics;
using osuTK;

namespace Orthomania.Patcher.Screens
{
    public interface IOptionsActions
    {
        void OpenDataFolder();
        void OpenLog();
        void CheckAgain();
        void Repair();
        void RemoveOrthomania();
        void ToggleDevServer();
        void HideOptions();
    }

    /// <summary>
    /// Options, over the main view: where things are, and the few things to do with them.
    /// </summary>
    public partial class OptionsView : VisibilityContainer
    {
        private readonly TextFlowContainer info;
        private readonly PatcherButton remove;
        private readonly PatcherButton devServer;
        private readonly IOptionsActions actions;
        private double confirmRemoveUntil;

        public OptionsView(IOptionsActions actions)
        {
            this.actions = actions;
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
                    Spacing = new Vector2(0, 8),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "options",
                            Font = PatcherFont.Get(26, FontWeight.Light),
                            Colour = Palette.CONTENT2,
                        },
                        info = new TextFlowContainer(t =>
                        {
                            t.Font = PatcherFont.Get(12);
                            t.Colour = Palette.FOREGROUND1;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            TextAnchor = Anchor.TopCentre,
                            Margin = new MarginPadding { Bottom = 6 },
                        },
                        button("Open the data folder", Palette.BACKGROUND2, actions.OpenDataFolder),
                        button("Open the patcher's log", Palette.BACKGROUND2, actions.OpenLog),
                        button("Check again", Palette.BACKGROUND2, actions.CheckAgain),
                        button("Repair (rebuild orthomania's copy of osu!)", Palette.BACKGROUND2, actions.Repair),
                        devServer = button(devServerText(false), Palette.BACKGROUND2, actions.ToggleDevServer),
                        remove = button("Remove orthomania (your data stays)", Palette.PINK_DARKER, removeClicked),
                        button("Back", Palette.BACKGROUND1, actions.HideOptions),
                    },
                },
            };
        }

        private static PatcherButton button(string text, osuTK.Graphics.Color4 colour, Action action) => new PatcherButton(text, colour, action, 340)
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Height = 32,
        };

        public void SetInfo(string text) => info.Text = text;

        /// <summary>
        /// Development only, until the score server runs on its own.
        /// </summary>
        public void SetDevServer(bool on) => devServer.Text = devServerText(on);

        private static string devServerText(bool on) => $"Start the score server with osu! (dev): {(on ? "on" : "off")}";

        private void removeClicked()
        {
            if (Time.Current < confirmRemoveUntil)
            {
                confirmRemoveUntil = 0;
                remove.Text = "Remove orthomania (your data stays)";
                actions.RemoveOrthomania();
                return;
            }

            confirmRemoveUntil = Time.Current + 4000;
            remove.Text = "Click again to remove";
            Scheduler.AddDelayed(() => remove.Text = "Remove orthomania (your data stays)", 4000);
        }

        protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);

        protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);
    }
}
