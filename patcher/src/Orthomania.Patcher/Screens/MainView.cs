using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using Orthomania.Patcher.Flow;
using Orthomania.Patcher.Graphics;
using osuTK;

namespace Orthomania.Patcher.Screens
{
    /// <summary>
    /// What the person can do from the main view.
    /// </summary>
    public interface IPatcherActions
    {
        void Launch();
        void CheckAgain();
        void InstallLazer();
        void ShowInstalls();
        void ShowOptions();
        void OpenNotice(int index);
    }

    /// <summary>
    /// Everything the main view shows.
    /// </summary>
    public sealed record PatcherView(PatcherStatus Status, IReadOnlyList<string> Notices);

    /// <summary>
    /// Stable's updater laid out in lazer's look: the circle, the thick bar, a big line and a small line, and here the buttons the
    /// state needs. Small notices go along the bottom.
    /// </summary>
    public partial class MainView : CompositeDrawable
    {
        private readonly IPatcherActions actions;
        private readonly LogoCircle logo;
        private readonly ProgressBar bar;
        private readonly SpriteText title;
        private readonly TextFlowContainer detail;
        private readonly FillFlowContainer buttons;
        private readonly FillFlowContainer notices;

        public MainView(IPatcherActions actions, int? seed)
        {
            this.actions = actions;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new LinkText("options", actions.ShowOptions)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Top = 8, Right = 12 },
                },
                logo = new LogoCircle(176, seed)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = 34,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = 24 },
                    Y = 240,
                    Child = bar = new ProgressBar(),
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Horizontal = 24 },
                    Y = 262,
                    Spacing = new Vector2(0, 2),
                    Children = new Drawable[]
                    {
                        title = new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = PatcherFont.Get(40, FontWeight.Light),
                            Colour = Palette.CONTENT2,
                        },
                        detail = new TextFlowContainer(t =>
                        {
                            t.Font = PatcherFont.Get(15);
                            t.Colour = Palette.LIGHT3;
                        })
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            TextAnchor = Anchor.TopCentre,
                        },
                        buttons = new FillFlowContainer
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(10, 0),
                            Margin = new MarginPadding { Top = 14 },
                        },
                    },
                },
                notices = new FillFlowContainer
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Horizontal = 16, Bottom = 8 },
                    Spacing = new Vector2(0, 2),
                },
            };
        }

        public void Show(PatcherView view)
        {
            var status = view.Status;

            title.Text = status.Title;
            detail.Text = status.Detail;

            bool busy = status.Stage is PatcherStage.Starting or PatcherStage.WaitingForGame or PatcherStage.UpdatingLazer or PatcherStage.UpdatingPatcher
                or PatcherStage.RestartingPatcher or PatcherStage.GettingOrthomania or PatcherStage.Preparing;

            logo.Busy = busy;

            switch (status.Stage)
            {
                case PatcherStage.Ready:
                case PatcherStage.Running:
                    bar.Show(1, false);
                    break;

                case PatcherStage.Problem:
                case PatcherStage.NotInstalled:
                    bar.Show(0, false);
                    break;

                default:
                    bar.Show(status.Progress, true);
                    break;
            }

            buttons.Clear();

            switch (status.Stage)
            {
                case PatcherStage.Ready:
                    buttons.Add(new PatcherButton("Launch", Palette.PINK, actions.Launch, 160));
                    break;

                case PatcherStage.NotInstalled:
                    buttons.Add(new PatcherButton("Install osu!", Palette.PINK, actions.InstallLazer, 150));
                    buttons.Add(new PatcherButton("Find osu!", Palette.BACKGROUND1, actions.ShowInstalls, 130));
                    buttons.Add(new PatcherButton("Check again", Palette.BACKGROUND1, actions.CheckAgain, 130));
                    break;

                case PatcherStage.Problem:
                    buttons.Add(new PatcherButton("Try again", Palette.BACKGROUND1, actions.CheckAgain, 140));
                    break;
            }

            notices.Clear();

            for (int i = 0; i < view.Notices.Count; i++)
            {
                int index = i;
                notices.Add(new NoticeLine(view.Notices[i], () => actions.OpenNotice(index)));
            }
        }

        private partial class NoticeLine : ClickableContainer
        {
            public NoticeLine(string text, Action action)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Action = action;

                Child = new TextFlowContainer(t =>
                {
                    t.Font = PatcherFont.Get(12);
                    t.Colour = Palette.YELLOW;
                })
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    TextAnchor = Anchor.TopCentre,
                    Text = text,
                };
            }
        }
    }
}
