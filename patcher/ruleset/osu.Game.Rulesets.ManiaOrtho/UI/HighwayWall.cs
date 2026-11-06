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
    /// The field wall (SPEC 4, Chunithm's field wall): an opaque cover over the lanes beyond the depth where notes appear,
    /// hiding the notes there. Notes come out from under its lower edge.
    /// </summary>
    /// <remarks>
    /// It covers only the lanes (Alpha, 2026-11-04): for each stage, the track between its outer columns' edges, as the
    /// columns are laid out this frame (a ported skin's stage position and column widths, dual stages), from the wall's
    /// depth to the far edge, and the stage's edge guide lines with them. The rails, a ported skin's stage borders and the
    /// background beside and above the lanes stay visible. Its sides run along the lanes, so it leans with the kiai sway and
    /// a side stage's lean.
    /// </remarks>
    public partial class HighwayWall : Drawable
    {
        /// <summary>
        /// The lower edge, marking where notes appear, in pixels of this drawable's space.
        /// </summary>
        public const float EDGE_THICKNESS = 3;

        /// <summary>
        /// The far end of the wall, darker, and its lower end.
        /// </summary>
        private static readonly Color4 far_colour = Color4Extensions.FromHex("#06060c");

        private static readonly Color4 near_colour = Color4Extensions.FromHex("#14141f");

        /// <summary>
        /// The arcade's guide-line teal (research/game-chunithm.md).
        /// </summary>
        private static readonly Color4 edge_colour = Color4Extensions.FromHex("#45d9c8");

        private HighwayLayout layout = new HighwayLayout();
        private float depth = 1;

        private static readonly Vector2[] whole_track = { new Vector2(0, 1) };

        private Vector2[] stages = { new Vector2(0, 1) };
        private int stageCount = 1;

        private IShader shader = null!;

        public HighwayWall()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        /// <summary>
        /// Where the track is this frame, relative to this drawable's size.
        /// </summary>
        public HighwayLayout Layout => layout;

        /// <summary>
        /// The depth of the wall's lower edge: where notes appear.
        /// </summary>
        public float WallDepth => depth;

        /// <summary>
        /// Each stage's outer column edges in lane space, which the wall covers.
        /// </summary>
        public ReadOnlySpan<Vector2> Stages => stages.AsSpan(0, stageCount);

        /// <summary>
        /// Moves the wall to where <paramref name="scroll"/> puts it. Hidden when there is no wall, or when the highway is
        /// drawn flat (mania's own Cover mod covers that case).
        /// </summary>
        public void Place(in HighwayProjection projection, HighwayScroll scroll, bool flat)
        {
            if (flat || scroll.Wall <= 0)
            {
                Alpha = 0;
                return;
            }

            if (layout != projection.Layout || depth != scroll.AppearDepth)
            {
                layout = projection.Layout;
                depth = scroll.AppearDepth;
                Invalidate(Invalidation.DrawNode);
            }

            Alpha = 1;
        }

        /// <summary>
        /// Sets where the lanes are: each stage's left and right edge in lane space, as the columns are laid out this frame.
        /// An empty span keeps the whole track. Keeps the storage when it is big enough.
        /// </summary>
        public void SetStages(ReadOnlySpan<Vector2> stageLanes)
        {
            if (stageLanes.IsEmpty)
                stageLanes = whole_track;

            if (stageLanes.SequenceEqual(Stages))
                return;

            if (stages.Length < stageLanes.Length)
                stages = new Vector2[stageLanes.Length];

            stageLanes.CopyTo(stages);
            stageCount = stageLanes.Length;
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>
        /// The wall's cover of stage <paramref name="index"/> (<see cref="Stages"/>) in screen space, as it is drawn.
        /// </summary>
        public Quad GetScreenSpaceCover(int index)
            => GetCover(new HighwayProjection(layout, DrawSize), Stages[index], depth, pixelSize(DrawInfo.Matrix)) * DrawInfo.Matrix;

        /// <summary>
        /// One screen pixel in the units of a drawable drawn with <paramref name="matrix"/> (gameplay is drawn inside a scaling
        /// container).
        /// </summary>
        private static float pixelSize(Matrix3 matrix)
        {
            float pixelsPerUnit = MathF.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
            return pixelsPerUnit > 0 ? 1 / pixelsPerUnit : 1;
        }

        /// <summary>
        /// What the wall covers of one stage on screen: the track between <paramref name="stage"/>'s edges (lane space) from
        /// <paramref name="depth"/> to the far edge, widened on each side by half the edge guide line's width there
        /// (<see cref="HighwayGuides.LINE_WIDTH"/>, at least <see cref="HighwayGuides.MIN_LINE_PIXELS"/> on screen) and half a
        /// pixel, so the stage's edge lines go under it too.
        /// </summary>
        /// <param name="projection">The highway's projection.</param>
        /// <param name="stage">The stage's left and right edge in lane space.</param>
        /// <param name="depth">The wall's depth.</param>
        /// <param name="pixel">One screen pixel in the projection's units.</param>
        public static Quad GetCover(in HighwayProjection projection, Vector2 stage, float depth, float pixel)
        {
            Vector2 nearLeft = projection.ToScreen(stage.X, depth);
            Vector2 nearRight = projection.ToScreen(stage.Y, depth);
            Vector2 farLeft = projection.ToScreen(stage.X, 1);
            Vector2 farRight = projection.ToScreen(stage.Y, 1);

            float nearPad = pad(projection.WidthAt(depth));
            float farPad = pad(projection.WidthAt(1));

            return new Quad(farLeft - new Vector2(farPad, 0), farRight + new Vector2(farPad, 0),
                nearLeft - new Vector2(nearPad, 0), nearRight + new Vector2(nearPad, 0));

            float pad(float trackWidth) => Math.Max(HighwayGuides.LINE_WIDTH * trackWidth, HighwayGuides.MIN_LINE_PIXELS * pixel) / 2 + pixel / 2;
        }

        /// <summary>
        /// The wall's lower edge for one stage: a band <see cref="EDGE_THICKNESS"/> tall standing on the bottom of
        /// <paramref name="cover"/> (from <see cref="GetCover"/>), its sides along the cover's.
        /// </summary>
        public static Quad GetEdge(Quad cover, float thickness)
        {
            float height = cover.BottomLeft.Y - cover.TopLeft.Y;
            float t = height > 0 ? Math.Min(1, thickness / height) : 1;

            return new Quad(Vector2.Lerp(cover.BottomLeft, cover.TopLeft, t), Vector2.Lerp(cover.BottomRight, cover.TopRight, t),
                cover.BottomLeft, cover.BottomRight);
        }

        protected override DrawNode CreateDrawNode() => new HighwayWallDrawNode(this);

        private class HighwayWallDrawNode : DrawNode
        {
            protected new HighwayWall Source => (HighwayWall)base.Source;

            private IShader shader = null!;
            private HighwayProjection projection;
            private float depth;
            private float pixel;
            private float alpha;

            private Vector2[] stages = Array.Empty<Vector2>();
            private int stageCount;

            public HighwayWallDrawNode(HighwayWall source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                projection = new HighwayProjection(Source.layout, Source.DrawSize);
                depth = Source.depth;
                alpha = DrawColourInfo.Colour.MaxAlpha;

                pixel = pixelSize(DrawInfo.Matrix);

                stageCount = Source.stageCount;
                if (stages.Length < stageCount)
                    stages = new Vector2[Source.stages.Length];
                Array.Copy(Source.stages, stages, stageCount);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (alpha <= 0 || depth >= 1)
                    return;

                shader.Bind();

                for (int i = 0; i < stageCount; i++)
                {
                    if (!(stages[i].Y > stages[i].X))
                        continue;

                    Quad cover = GetCover(projection, stages[i], depth, pixel);

                    draw(renderer, cover, far_colour, near_colour);
                    draw(renderer, GetEdge(cover, EDGE_THICKNESS), edge_colour, edge_colour);
                }

                shader.Unbind();
            }

            private void draw(IRenderer renderer, Quad quad, Color4 top, Color4 bottom)
                => renderer.DrawQuad(renderer.WhitePixel, quad * DrawInfo.Matrix, ColourInfo.GradientVertical(top.Opacity(alpha), bottom.Opacity(alpha)));
        }
    }
}
