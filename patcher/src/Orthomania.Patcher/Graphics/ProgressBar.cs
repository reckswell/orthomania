using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace Orthomania.Patcher.Graphics
{
    /// <summary>
    /// Stable's thick progress bar, rounded and pink as lazer's are. A sweep runs along it while the patcher waits on something
    /// it cannot measure.
    /// </summary>
    public partial class ProgressBar : CompositeDrawable
    {
        private readonly Box fill;
        private readonly Box sweep;

        public ProgressBar()
        {
            RelativeSizeAxes = Axes.X;
            Height = 8;
            Masking = true;
            CornerRadius = 4;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Palette.BACKGROUND3 },
                fill = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = Palette.PINK },
                sweep = new Box { RelativeSizeAxes = Axes.Both, RelativePositionAxes = Axes.X, Width = 0.25f, Colour = Palette.PINK, Alpha = 0 },
            };
        }

        /// <summary>
        /// From 0 to 1, or null to sweep.
        /// </summary>
        public void Show(double? progress, bool active, Color4? colour = null)
        {
            fill.Colour = sweep.Colour = colour ?? Palette.PINK;

            if (progress == null && active)
            {
                fill.ResizeWidthTo(0, 200, Easing.OutQuint);

                if (sweep.Alpha == 0)
                {
                    sweep.FadeIn(200);
                    sweep.MoveToX(-0.25f).MoveToX(1, 1400, Easing.InOutSine).Loop();
                }

                return;
            }

            sweep.FadeOut(200).Finally(s => s.ClearTransforms());
            fill.ResizeWidthTo((float)(progress ?? 0), 300, Easing.OutQuint);
        }
    }
}
