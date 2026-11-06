// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// Renders its children flat into an off-screen buffer, then draws the part of that buffer covering the stage onto the
    /// highway (backlog M1, rendering option "warp"). Everything mania draws inside the stage (notes, holds, bar lines,
    /// column lighting, visibility mods) follows the perspective without being reimplemented.
    /// </summary>
    /// <remarks>
    /// The stage is mapped lane-for-lane: its left and right edges become lanes 0 and 1, its hit position becomes depth 0
    /// and the top of its scrolling area becomes <see cref="HighwayScroll.ScrollLength"/>. Depth is linear in the source y
    /// (mania moves notes linearly in y), so notes keep exactly the timing mania gives them.
    /// </remarks>
    public partial class HighwayWarpContainer : Container, IBufferedDrawable
    {
        /// <summary>
        /// When false, the buffer is drawn back unchanged (the Flat camera).
        /// </summary>
        public bool Warp { get; set; } = true;

        public HighwayLayout Layout { get; set; } = new HighwayLayout();

        public HighwayScroll Scroll { get; set; } = new HighwayScroll(0, true);

        /// <summary>
        /// The stage's columns in this container's local space: left and right edges, the top of the scrolling area
        /// (<see cref="RectangleF.Top"/>) and the judgement line (<see cref="RectangleF.Bottom"/>).
        /// </summary>
        public RectangleF Source { get; set; }

        public Color4 BackgroundColour => new Color4(0, 0, 0, 0);

        public DrawColourInfo? FrameBufferDrawColour => null;

        public Vector2 FrameBufferScale { get; set; } = Vector2.One;

        public IShader TextureShader { get; private set; } = null!;

        private readonly BufferedDrawNodeSharedData sharedData = new BufferedDrawNodeSharedData(pixelSnapping: false, clipToRootNode: true);

        private long updateVersion;

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        protected override void Update()
        {
            base.Update();

            // Gameplay changes every frame, so redraw the buffer every frame.
            updateVersion++;
            Invalidate(Invalidation.DrawNode);
        }

        protected override DrawNode CreateDrawNode() => new WarpDrawNode(this, sharedData);

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            sharedData.Dispose();
        }

        private class WarpDrawNode : BufferedDrawNode, ICompositeDrawNode
        {
            protected new HighwayWarpContainer Source => (HighwayWarpContainer)base.Source;

            protected new CompositeDrawableDrawNode Child => (CompositeDrawableDrawNode)base.Child;

            private long updateVersion;
            private bool warp;
            private HighwayProjection projection;
            private HighwayScroll scroll;
            private RectangleF source;
            private Vector2 localSize;
            private Vector2 frameBufferScale;

            public WarpDrawNode(HighwayWarpContainer source, BufferedDrawNodeSharedData sharedData)
                : base(source, new CompositeDrawableDrawNode(source), sharedData)
            {
            }

            public List<DrawNode>? Children
            {
                get => Child.Children;
                set => Child.Children = value;
            }

            public bool AddChildDrawNodes => RequiresRedraw;

            public override void ApplyState()
            {
                base.ApplyState();

                updateVersion = Source.updateVersion;
                warp = Source.Warp;
                localSize = Source.DrawSize;
                projection = new HighwayProjection(Source.Layout, localSize);
                scroll = Source.Scroll;
                source = Source.Source;
                frameBufferScale = Source.FrameBufferScale;
            }

            protected override long GetDrawVersion() => updateVersion;

            /// <summary>
            /// The buffer holds premultiplied colour: it starts transparent, and content blended into it normally is
            /// multiplied by its alpha on the way in. Drawn back with normal blending it would be multiplied again, so
            /// anything see-through (bar lines, a 50% hold body, soft edges) came out at its alpha squared wherever nothing
            /// opaque lay under it in the stage. That used to be the skin's lane backgrounds; on the highway's own track,
            /// which is drawn under this container, it is nothing, and bar lines were about seven times too faint.
            /// </summary>
            private static readonly BlendingParameters premultiplied = BlendingParameters.Mixture with { Source = BlendingType.One };

            protected override void DrawContents(IRenderer renderer)
            {
                float scrollHeight = source.Bottom - source.Top;

                renderer.SetBlend(premultiplied);
                ColourInfo colour = premultiply(DrawColourInfo.Colour);

                if (!warp || source.Width <= 0 || scrollHeight <= 0)
                {
                    renderer.DrawFrameBuffer(SharedData.CurrentEffectBuffer, Quad.FromRectangle(DrawRectangle), colour);
                    return;
                }

                var texture = SharedData.MainBuffer.Texture;

                // Above the judgement line: the scrolling area, in perspective. The hit position is depth 0 and the top of
                // the scrolling area is the scroll length, linearly in source y, so notes keep exactly mania's timing.
                Vector2 topLeft = toBufferSpace(new Vector2(source.Left, source.Top));
                Vector2 lineRight = toBufferSpace(new Vector2(source.Right, source.Bottom));
                var aboveRect = new RectangleF(topLeft.X, topLeft.Y, lineRight.X - topLeft.X, lineRight.Y - topLeft.Y);

                TrackDrawing.DrawTrackRectangle(renderer, texture, projection, DrawInfo.Matrix, 0, 1, 0, scroll.ScrollLength, colour, aboveRect);

                // Below the line (receptors, key area): at its flat height. Perspective would magnify it several times.
                float belowHeight = localSize.Y - source.Bottom;
                Vector2 bottomRight = toBufferSpace(new Vector2(source.Right, localSize.Y));
                var belowRect = new RectangleF(topLeft.X, lineRight.Y, lineRight.X - topLeft.X, bottomRight.Y - lineRight.Y);

                TrackDrawing.DrawBelowLine(renderer, texture, projection, DrawInfo.Matrix, belowHeight, colour, belowRect);
            }

            /// <summary>
            /// The draw colour for premultiplied blending: its colour scaled by its alpha, so fading this container fades
            /// the stage instead of brightening it.
            /// </summary>
            private static ColourInfo premultiply(ColourInfo colour)
            {
                if (colour.HasSingleColour)
                    return premultiply(colour.TopLeft);

                return new ColourInfo
                {
                    TopLeft = premultiply(colour.TopLeft),
                    TopRight = premultiply(colour.TopRight),
                    BottomLeft = premultiply(colour.BottomLeft),
                    BottomRight = premultiply(colour.BottomRight),
                };
            }

            private static Color4 premultiply(SRGBColour colour)
            {
                Color4 c = colour.SRGB;
                return new Color4(c.R * c.A, c.G * c.A, c.B * c.A, c.A);
            }

            private Vector2 toBufferSpace(Vector2 local)
            {
                Vector2 screen = Vector2Extensions.Transform(local, DrawInfo.Matrix);
                return (screen - DrawRectangle.TopLeft) * frameBufferScale;
            }
        }
    }
}
