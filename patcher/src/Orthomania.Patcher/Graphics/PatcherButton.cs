using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Orthomania.Patcher.Graphics
{
    /// <summary>
    /// A button in lazer's style (<c>RoundedButton</c>): rounded corners, a flat colour that lightens on hover, Torus semibold.
    /// </summary>
    public partial class PatcherButton : ClickableContainer
    {
        private readonly Box background;
        private readonly Box flash;
        private readonly SpriteText label;
        private readonly Color4 colour;

        public string Text
        {
            get => label.Text.ToString();
            set => label.Text = value;
        }

        public PatcherButton(string text, Color4 colour, Action action, float width = 150)
        {
            this.colour = colour;
            Action = action;
            Size = new Vector2(width, 36);
            Masking = true;
            CornerRadius = 5;
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Color4.Black.Opacity(0.2f),
                Radius = 6,
                Offset = new Vector2(0, 2),
            };

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
                flash = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White, Alpha = 0, Blending = BlendingParameters.Additive },
                label = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = text,
                    Font = PatcherFont.Get(16, FontWeight.SemiBold),
                    Colour = Color4.White,
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(colour.Lighten(0.2f), 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(colour, 200, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            this.ScaleTo(0.96f, 400, Easing.OutQuint);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            this.ScaleTo(1, 400, Easing.OutElastic);
            base.OnMouseUp(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            flash.FadeTo(0.25f).FadeOut(400, Easing.OutQuint);
            return base.OnClick(e);
        }
    }

    /// <summary>
    /// A small text link, like "help?" in stable's updater.
    /// </summary>
    public partial class LinkText : ClickableContainer
    {
        private readonly SpriteText text;
        private readonly Color4 colour;

        public LinkText(string label, Action action, float size = 14, Color4? colour = null)
        {
            this.colour = colour ?? Palette.LIGHT3;
            Action = action;
            AutoSizeAxes = Axes.Both;
            Child = text = new SpriteText { Text = label, Font = PatcherFont.Get(size), Colour = this.colour };
        }

        protected override bool OnHover(HoverEvent e)
        {
            text.FadeColour(Palette.PINK_LIGHT, 150);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            text.FadeColour(colour, 150);
            base.OnHoverLost(e);
        }
    }
}
