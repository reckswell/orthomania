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
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// The highway's own markings, after Chunithm (research/game-chunithm.md, "Judgement line and receptors" and P4): teal
    /// guide lines along the lane boundaries that thin and dim with distance, and a white judgement line with a gold glow.
    /// From 6K, the boundaries between mania's hand groups are drawn twice as wide and brighter (<see cref="LaneGroups"/>,
    /// research/topic-wide-keys.md W1).
    /// </summary>
    /// <remarks>
    /// Drawn over the warped stage. Chunithm draws its judgement line and guides under the notes; these are drawn over
    /// them, so the line is kept thin and the notes' leading edges still show through the glow.
    /// </remarks>
    public partial class HighwayGuides : Drawable
    {
        /// <summary>
        /// Guide line width, in track widths: about 2.5 px at the judgement line at 1080p (research P4).
        /// </summary>
        public const float LINE_WIDTH = 0.0022f;

        /// <summary>
        /// Guide lines never get thinner than this on screen, in screen pixels.
        /// </summary>
        public const float MIN_LINE_PIXELS = 1;

        /// <summary>
        /// Group divider width, in track widths: twice a lane line.
        /// </summary>
        public const float GROUP_LINE_WIDTH = 2 * LINE_WIDTH;

        /// <summary>
        /// Group dividers never get thinner than this on screen, so far away they still differ from the 1 px lane lines.
        /// </summary>
        public const float MIN_GROUP_LINE_PIXELS = 2;

        /// <summary>
        /// The judgement line's white core, as a fraction of the playfield height (1.0% H in the arcade).
        /// </summary>
        public const float JUDGEMENT_CORE_HEIGHT = 0.010f;

        /// <summary>
        /// The judgement line's glow, core included, as a fraction of the playfield height (3.3% H in the arcade).
        /// </summary>
        public const float JUDGEMENT_GLOW_HEIGHT = 0.033f;

        private static readonly Color4 divider_colour = Color4Extensions.FromHex("#45d9c8");
        private static readonly Color4 edge_colour = Color4Extensions.FromHex("#1f6763");
        private static readonly Color4 glow_inner = new Color4(255, 226, 119, 255);
        private static readonly Color4 glow_outer = new Color4(249, 188, 24, 0);

        private const float near_alpha = 0.8f;
        private const float far_alpha = 0.3f;
        private const float group_near_alpha = 1;
        private const float group_far_alpha = 0.45f;

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

        private bool showJudgementLine = true;

        public bool ShowJudgementLine
        {
            get => showJudgementLine;
            set
            {
                if (showJudgementLine == value)
                    return;

                showJudgementLine = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private bool showLaneLines = true;

        public bool ShowLaneLines
        {
            get => showLaneLines;
            set
            {
                if (showLaneLines == value)
                    return;

                showLaneLines = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private GuideLine[] lines = Array.Empty<GuideLine>();

        /// <summary>
        /// The number of lines set with <see cref="SetLine"/>.
        /// </summary>
        public int LineCount { get; private set; }

        private IShader shader = null!;

        public HighwayGuides()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        /// <summary>
        /// Sets how many lane lines there are, keeping the storage when it is big enough.
        /// </summary>
        public void SetLineCount(int count)
        {
            if (lines.Length < count)
                lines = new GuideLine[count];

            if (LineCount != count)
                Invalidate(Invalidation.DrawNode);

            LineCount = count;
        }

        /// <summary>
        /// Sets line <paramref name="index"/>: at <paramref name="lane"/> in lane space, an edge of a stage, a divider between
        /// two lanes, or one between two hand groups.
        /// </summary>
        public void SetLine(int index, float lane, GuideLineKind kind)
        {
            var line = new GuideLine(lane, kind);

            if (lines[index] == line)
                return;

            lines[index] = line;
            Invalidate(Invalidation.DrawNode);
        }

        public GuideLine GetLine(int index) => lines[index];

        /// <summary>
        /// The lines set with <see cref="SetLine"/>: for each stage its left edge, the line before each of its columns after the
        /// first, then its right edge.
        /// </summary>
        public ReadOnlySpan<GuideLine> Lines => lines.AsSpan(0, LineCount);

        /// <summary>
        /// Half of the solid part of a <paramref name="kind"/> line, in lane space, where the whole track is
        /// <paramref name="trackPixels"/> screen pixels wide: half its width (at least its pixel floor), less the fading edge of
        /// a feathered group divider, whose solid core is a pixel narrower (<see cref="TrackDrawing.Feather"/>). A note's side
        /// placed this far from the line's centre meets the line without a gap.
        /// </summary>
        public static float SolidHalfWidth(GuideLineKind kind, float trackPixels)
        {
            bool group = kind == GuideLineKind.Group;
            float width = group ? GROUP_LINE_WIDTH : LINE_WIDTH;

            if (!(trackPixels > 0))
                return width / 2;

            float pixels = Math.Max(width * trackPixels, group ? MIN_GROUP_LINE_PIXELS : MIN_LINE_PIXELS);

            if (group)
                pixels -= 1;

            return pixels / 2 / trackPixels;
        }

        protected override DrawNode CreateDrawNode() => new HighwayGuidesDrawNode(this);

        public readonly record struct GuideLine(float Lane, GuideLineKind Kind)
        {
            public bool IsEdge => Kind == GuideLineKind.Edge;
        }

        public enum GuideLineKind
        {
            /// <summary>
            /// A stage's outer edge.
            /// </summary>
            Edge,

            /// <summary>
            /// Between two lanes.
            /// </summary>
            Lane,

            /// <summary>
            /// Between two of mania's hand groups (<see cref="LaneGroups"/>).
            /// </summary>
            Group,
        }

        private class HighwayGuidesDrawNode : DrawNode
        {
            protected new HighwayGuides Source => (HighwayGuides)base.Source;

            private IShader shader = null!;
            private HighwayProjection projection;
            private bool showJudgementLine;
            private bool showLaneLines;
            private GuideLine[] lines = Array.Empty<GuideLine>();
            private int lineCount;
            private float alpha;
            private float pixel;
            private float minLineWidth;
            private float minGroupLineWidth;

            public HighwayGuidesDrawNode(HighwayGuides source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                projection = new HighwayProjection(Source.layout, Source.DrawSize);
                showJudgementLine = Source.showJudgementLine;
                showLaneLines = Source.showLaneLines;
                alpha = DrawColourInfo.Colour.MaxAlpha;

                // The minimum is in screen pixels; gameplay is drawn inside a scaling container.
                float pixelsPerUnit = MathF.Sqrt(DrawInfo.Matrix.M11 * DrawInfo.Matrix.M11 + DrawInfo.Matrix.M12 * DrawInfo.Matrix.M12);
                pixel = pixelsPerUnit > 0 ? 1 / pixelsPerUnit : 1;
                minLineWidth = MIN_LINE_PIXELS * pixel;
                minGroupLineWidth = MIN_GROUP_LINE_PIXELS * pixel;

                lineCount = Source.LineCount;
                if (lines.Length < lineCount)
                    lines = new GuideLine[Source.lines.Length];
                Array.Copy(Source.lines, lines, lineCount);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (alpha <= 0)
                    return;

                shader.Bind();

                if (showLaneLines)
                {
                    for (int i = 0; i < lineCount; i++)
                        drawLaneLine(renderer, lines[i]);
                }

                if (showJudgementLine)
                    drawJudgementLine(renderer);

                shader.Unbind();
            }

            private void drawLaneLine(IRenderer renderer, GuideLine line)
            {
                bool group = line.Kind == GuideLineKind.Group;

                TrackDrawing.GetLaneLine(projection, line.Lane, group ? GROUP_LINE_WIDTH : LINE_WIDTH, 1, group ? minGroupLineWidth : minLineWidth,
                    out var near, out var far, out float split);

                Color4 colour = line.IsEdge ? edge_colour : divider_colour;
                float nearAlpha = group ? group_near_alpha : near_alpha;
                float farAlpha = group ? group_far_alpha : far_alpha;
                float splitAlpha = nearAlpha + (farAlpha - nearAlpha) * split;

                // Group dividers are wide enough to step visibly from 2 to 4 px: their edges fade instead (TrackDrawing.Feather).
                drawPart(renderer, near, colour.Opacity(splitAlpha * alpha), colour.Opacity(nearAlpha * alpha), group);

                if (split < 1)
                    drawPart(renderer, far, colour.Opacity(farAlpha * alpha), colour.Opacity(splitAlpha * alpha), group);
            }

            private void drawPart(IRenderer renderer, Quad part, Color4 top, Color4 bottom, bool feathered)
            {
                if (!feathered)
                {
                    draw(renderer, part, top, bottom);
                    return;
                }

                TrackDrawing.Feather(part, pixel, out var core, out var left, out var right);

                Color4 topClear = top.Opacity(0);
                Color4 bottomClear = bottom.Opacity(0);

                draw(renderer, core, top, bottom);
                renderer.DrawQuad(renderer.WhitePixel, left * DrawInfo.Matrix, new ColourInfo
                {
                    TopLeft = topClear,
                    BottomLeft = bottomClear,
                    TopRight = top,
                    BottomRight = bottom,
                });
                renderer.DrawQuad(renderer.WhitePixel, right * DrawInfo.Matrix, new ColourInfo
                {
                    TopLeft = top,
                    BottomLeft = bottom,
                    TopRight = topClear,
                    BottomRight = bottomClear,
                });
            }

            private void drawJudgementLine(IRenderer renderer)
            {
                float y = projection.YAt(0);
                float core = JUDGEMENT_CORE_HEIGHT * projection.Size.Y / 2;
                float glow = JUDGEMENT_GLOW_HEIGHT * projection.Size.Y / 2;

                Color4 outer = glow_outer.Opacity(0);
                Color4 inner = glow_inner.Opacity(alpha);

                draw(renderer, TrackDrawing.GetAcrossTrack(projection, y - glow, y - core), outer, inner);
                draw(renderer, TrackDrawing.GetAcrossTrack(projection, y + core, y + glow), inner, outer);
                draw(renderer, TrackDrawing.GetAcrossTrack(projection, y - core, y + core), Color4.White.Opacity(alpha), Color4.White.Opacity(alpha));
            }

            private void draw(IRenderer renderer, Quad quad, Color4 top, Color4 bottom)
                => renderer.DrawQuad(renderer.WhitePixel, quad * DrawInfo.Matrix, ColourInfo.GradientVertical(top, bottom));
        }
    }
}
