using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace Orthomania.Patcher.Graphics
{
    /// <summary>
    /// The circle from stable's updater, drawn as lazer draws its own: pink, triangles inside, a white ring. It carries orthomania's
    /// mark, not osu!'s logo. It breathes while the patcher is busy.
    /// </summary>
    public partial class LogoCircle : CompositeDrawable
    {
        private const float ring = 9;

        public LogoCircle(float size, int? seed = null)
        {
            Size = new Vector2(size);

            InternalChild = new CircularContainer
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                BorderThickness = ring,
                BorderColour = Color4.White,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Colour = Color4.Black.Opacity(0.35f),
                    Radius = 12,
                    Offset = new Vector2(0, 3),
                },
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Palette.PINK },
                    // Drawn to a buffer first: the framework's triangles ignore the circle's rounded mask, the buffer's image does not.
                    new BufferedContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = new TriangleField
                        {
                            ColourLight = Palette.PINK_LIGHT,
                            ColourDark = Palette.PINK_DARK,
                            Density = 80,
                            TriangleScale = 0.4f,
                            Velocity = 0.6f,
                            Seed = seed,
                        },
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "ortho",
                        Font = PatcherFont.Get(size * 0.27f, FontWeight.Bold),
                        Colour = Color4.White,
                        Shadow = true,
                        ShadowColour = Palette.PINK_DARKER.Opacity(0.4f),
                        Y = -size * 0.02f,
                    },
                },
            };
        }

        private bool busy;

        public bool Busy
        {
            get => busy;
            set
            {
                if (busy == value)
                    return;

                busy = value;
                ClearTransforms();

                if (busy)
                    this.ScaleTo(1.035f, 900, Easing.InOutSine).Then().ScaleTo(1f, 900, Easing.InOutSine).Loop();
                else
                    this.ScaleTo(1, 300, Easing.OutQuint);
            }
        }
    }
}
