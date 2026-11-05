using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osuTK;
using osuTK.Graphics;

namespace Orthomania.Patcher.Graphics
{
    /// <summary>
    /// Triangles drifting upwards, bigger ones faster: lazer's background pattern (<c>osu.Game.Graphics.Backgrounds.Triangles</c>),
    /// drawn with the framework's own triangles.
    /// </summary>
    public partial class TriangleField : CompositeDrawable
    {
        private const float triangle_size = 100;
        private const float base_velocity = 50;
        private const float equilateral_ratio = 0.866f;

        public Color4 ColourLight { get; init; } = Color4.White;
        public Color4 ColourDark { get; init; } = Color4.Black;

        /// <summary>
        /// Triangles per 100,000 square pixels.
        /// </summary>
        public float Density { get; init; } = 3;

        public float TriangleScale { get; init; } = 1;

        public float Velocity { get; set; } = 1;

        /// <summary>
        /// A seed, so screenshots of a state look the same each time.
        /// </summary>
        public int? Seed { get; init; }

        private Random random = null!;

        public TriangleField()
        {
            RelativeSizeAxes = Axes.Both;
            Masking = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            random = Seed != null ? new Random(Seed.Value) : new Random();

            int count = Math.Max(4, (int)(DrawWidth * DrawHeight / 100000f * Density));

            for (int i = 0; i < count; i++)
                AddInternal(create(random.NextSingle() * (DrawHeight + triangle_size * TriangleScale)));
        }

        private Triangle create(float y)
        {
            float scale = TriangleScale * (0.4f + random.NextSingle() * 0.8f);
            float size = triangle_size * scale;

            return new Triangle
            {
                Origin = Anchor.TopCentre,
                Size = new Vector2(size, size * equilateral_ratio),
                X = random.NextSingle() * DrawWidth,
                Y = y,
                Colour = Interpolation.ValueAt(random.NextSingle(), ColourDark, ColourLight, 0, 1),
                Alpha = 0.6f + random.NextSingle() * 0.4f,
            };
        }

        protected override void Update()
        {
            base.Update();

            if (DrawHeight <= 0)
                return;

            float elapsed = (float)Math.Min(Time.Elapsed, 100) / 1000f;

            foreach (var drawable in InternalChildren)
            {
                var triangle = (Triangle)drawable;
                triangle.Y -= base_velocity * Velocity * (triangle.Width / triangle_size) * elapsed;

                if (triangle.Y + triangle.Height < 0)
                {
                    triangle.Y = DrawHeight;
                    triangle.X = random.NextSingle() * DrawWidth;
                }
            }
        }
    }
}
