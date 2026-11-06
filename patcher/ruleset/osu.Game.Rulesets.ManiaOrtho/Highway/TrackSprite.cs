// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// A texture laid on a rectangle of the track and drawn in perspective. Fills its parent; the track's placement comes
    /// from <see cref="Layout"/>.
    /// </summary>
    public partial class TrackSprite : Drawable
    {
        private Texture? texture;

        public Texture? Texture
        {
            get => texture;
            set
            {
                texture = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private HighwayLayout layout = new HighwayLayout();

        public HighwayLayout Layout
        {
            get => layout;
            set
            {
                layout = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private Vector4 trackRect = new Vector4(0, 0, 1, 1);

        /// <summary>
        /// (lane0, depth0, lane1, depth1): the rectangle of the track to cover. Defaults to the whole visible track.
        /// </summary>
        public Vector4 TrackRect
        {
            get => trackRect;
            set
            {
                trackRect = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private bool subdivide = true;

        /// <summary>
        /// Whether to draw with perspective-correct subdivision (on by default; off shows the affine bend, for comparison).
        /// </summary>
        public bool Subdivide
        {
            get => subdivide;
            set
            {
                subdivide = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private IShader shader = null!;

        public TrackSprite()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders, IRenderer renderer)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
            texture ??= renderer.WhitePixel;
        }

        protected override DrawNode CreateDrawNode() => new TrackSpriteDrawNode(this);

        private class TrackSpriteDrawNode : DrawNode
        {
            protected new TrackSprite Source => (TrackSprite)base.Source;

            private Texture? texture;
            private IShader shader = null!;
            private HighwayProjection projection;
            private Vector4 trackRect;
            private bool subdivide;
            private ColourInfo colour;

            public TrackSpriteDrawNode(TrackSprite source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                texture = Source.texture;
                shader = Source.shader;
                projection = new HighwayProjection(Source.layout, Source.DrawSize);
                trackRect = Source.trackRect;
                subdivide = Source.subdivide;
                colour = DrawColourInfo.Colour;
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (texture?.Available != true)
                    return;

                shader.Bind();

                TrackDrawing.DrawTrackRectangle(renderer, texture, projection, DrawInfo.Matrix,
                    trackRect.X, trackRect.Z, trackRect.Y, trackRect.W, colour, subdivide: subdivide);

                shader.Unbind();
            }
        }
    }
}
