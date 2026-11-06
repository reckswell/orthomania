// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// Each lane's piece of the judgement line, lit in the colour of its last hit's result (research <c>game-sekai.md</c> K7,
    /// after Sekai's slot lights). <see cref="HighwayHitEffectLayer"/> decides the colours and fades them; this draws them.
    /// </summary>
    /// <remarks>
    /// Drawn over the judgement line, so a hit tints the line itself, and under the notes, so the next note's leading edge
    /// still reads over it. Not additive: the line's gold glow would wash every colour out towards white.
    /// </remarks>
    public partial class HighwaySlotLights : Drawable
    {
        /// <summary>
        /// The lit band's solid middle, as a fraction of the playfield height: as tall as the judgement line's glow.
        /// </summary>
        public const float SOLID_HEIGHT = HighwayGuides.JUDGEMENT_GLOW_HEIGHT;

        /// <summary>
        /// How far the band fades out above and below its solid middle, as a fraction of the playfield height, so a Perfect's
        /// gold still shows around the line's own gold glow.
        /// </summary>
        public const float FADE_HEIGHT = 0.01f;

        private HighwayLayout layout = new HighwayLayout();

        /// <summary>
        /// Where the track is, relative to this drawable's size.
        /// </summary>
        public HighwayLayout Layout
        {
            get => layout;
            set
            {
                if (layout == value)
                    return;

                layout = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private Vector2[] lanes = Array.Empty<Vector2>();
        private Color4[] colours = Array.Empty<Color4>();
        private int count;

        /// <summary>
        /// Each lane's light as drawn (transparent when unlit), in playfield column order.
        /// </summary>
        public ReadOnlySpan<Color4> Lights => colours.AsSpan(0, count);

        private IShader shader = null!;

        public HighwaySlotLights()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        /// <summary>
        /// Sets each lane's light for this frame. Keeps the storage when it is big enough.
        /// </summary>
        /// <param name="columns">Each column's left and right edge in lane space, in playfield column order.</param>
        /// <param name="lights">Each column's light, its alpha the light's strength.</param>
        public void SetLights(ReadOnlySpan<Vector2> columns, ReadOnlySpan<Color4> lights)
        {
            int newCount = Math.Min(columns.Length, lights.Length);

            if (newCount == count && columns[..newCount].SequenceEqual(lanes.AsSpan(0, count)) && lights[..newCount].SequenceEqual(Lights))
                return;

            if (lanes.Length < newCount)
            {
                lanes = new Vector2[newCount];
                colours = new Color4[newCount];
            }

            columns[..newCount].CopyTo(lanes);
            lights[..newCount].CopyTo(colours);
            count = newCount;

            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>
        /// Where a lit lane's band goes on screen: a solid middle <see cref="SOLID_HEIGHT"/> tall centred on the judgement
        /// line, with a band <see cref="FADE_HEIGHT"/> tall fading out above and below it, the sides on the lane's edges.
        /// </summary>
        /// <param name="projection">The highway's projection.</param>
        /// <param name="lane">The lane's left and right edge in lane space.</param>
        /// <param name="fadeAbove">The band fading out above the solid middle (clear at its top).</param>
        /// <param name="solid">The solid middle.</param>
        /// <param name="fadeBelow">The band fading out below the solid middle (clear at its bottom).</param>
        public static void GetBands(in HighwayProjection projection, Vector2 lane, out Quad fadeAbove, out Quad solid, out Quad fadeBelow)
        {
            float y = projection.YAt(0);
            float half = SOLID_HEIGHT * projection.Size.Y / 2;
            float fade = FADE_HEIGHT * projection.Size.Y;

            fadeAbove = TrackDrawing.GetAcrossLanes(projection, lane.X, lane.Y, y - half - fade, y - half);
            solid = TrackDrawing.GetAcrossLanes(projection, lane.X, lane.Y, y - half, y + half);
            fadeBelow = TrackDrawing.GetAcrossLanes(projection, lane.X, lane.Y, y + half, y + half + fade);
        }

        protected override DrawNode CreateDrawNode() => new HighwaySlotLightsDrawNode(this);

        private class HighwaySlotLightsDrawNode : DrawNode
        {
            protected new HighwaySlotLights Source => (HighwaySlotLights)base.Source;

            private IShader shader = null!;
            private HighwayProjection projection;
            private float alpha;

            private Vector2[] lanes = Array.Empty<Vector2>();
            private Color4[] colours = Array.Empty<Color4>();
            private int count;

            public HighwaySlotLightsDrawNode(HighwaySlotLights source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                projection = new HighwayProjection(Source.layout, Source.DrawSize);
                alpha = DrawColourInfo.Colour.MaxAlpha;

                count = Source.count;

                if (lanes.Length < count)
                {
                    lanes = new Vector2[Source.lanes.Length];
                    colours = new Color4[Source.lanes.Length];
                }

                Array.Copy(Source.lanes, lanes, count);
                Array.Copy(Source.colours, colours, count);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (alpha <= 0)
                    return;

                bool bound = false;

                for (int i = 0; i < count; i++)
                {
                    Color4 colour = colours[i];

                    if (colour.A <= 0)
                        continue;

                    if (!bound)
                    {
                        shader.Bind();
                        bound = true;
                    }

                    colour = colour.Opacity(colour.A * alpha);
                    Color4 clear = colour.Opacity(0);

                    GetBands(projection, lanes[i], out var fadeAbove, out var solid, out var fadeBelow);

                    draw(renderer, fadeAbove, clear, colour);
                    draw(renderer, solid, colour, colour);
                    draw(renderer, fadeBelow, colour, clear);
                }

                if (bound)
                    shader.Unbind();
            }

            private void draw(IRenderer renderer, Quad quad, Color4 top, Color4 bottom)
                => renderer.DrawQuad(renderer.WhitePixel, quad * DrawInfo.Matrix, ColourInfo.GradientVertical(top, bottom));
        }
    }
}
