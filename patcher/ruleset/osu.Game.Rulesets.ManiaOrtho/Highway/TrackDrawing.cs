// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    /// <summary>
    /// Draws textured rectangles that lie on the track, for use inside a <c>DrawNode</c>.
    /// </summary>
    /// <remarks>
    /// The GPU interpolates texture coordinates linearly across each triangle, which is wrong under perspective: a
    /// trapezoid drawn as two triangles shows a visible bend along its diagonal (about a quarter of the width change
    /// across the quad, in pixels). Splitting the rectangle into a grid of small quads, each nearly a parallelogram,
    /// brings that error under a pixel without a custom shader. Each cell is placed exactly by the projection, so cell
    /// edges stay on the true perspective lines.
    /// </remarks>
    public static class TrackDrawing
    {
        /// <summary>
        /// Target height of one strip on screen, in pixels.
        /// </summary>
        public const float STRIP_HEIGHT = 8;

        public const int MAX_STRIPS = 256;

        /// <summary>
        /// Target width change across one cell, in pixels. The bend error is about a quarter of this.
        /// </summary>
        public const float MAX_WIDTH_CHANGE_PER_CELL = 1;

        public const int MAX_COLUMNS = 32;

        /// <summary>
        /// Draws <paramref name="texture"/> (or its <paramref name="textureRect"/>, in texture pixels) over the track rectangle
        /// lanes <paramref name="lane0"/>..<paramref name="lane1"/>, depths <paramref name="depth0"/> (near) to
        /// <paramref name="depth1"/> (far). The texture's top edge maps to the far end.
        /// </summary>
        /// <param name="renderer">The renderer.</param>
        /// <param name="texture">The texture to draw.</param>
        /// <param name="projection">Projects track space into the drawable's local space.</param>
        /// <param name="toScreenSpace">The drawable's local-to-screen matrix (<c>DrawInfo.Matrix</c>).</param>
        /// <param name="lane0">Left edge, in lane space.</param>
        /// <param name="lane1">Right edge, in lane space.</param>
        /// <param name="depth0">Near edge depth.</param>
        /// <param name="depth1">Far edge depth.</param>
        /// <param name="colour">Colour, interpolated over the whole rectangle (top = far end).</param>
        /// <param name="textureRect">The part of the texture to draw, in texture pixels; the whole texture if null.</param>
        /// <param name="subdivide">Whether to subdivide. Small or thin rectangles (a note) can skip it.</param>
        public static void DrawTrackRectangle(IRenderer renderer, Texture texture, in HighwayProjection projection, Matrix3 toScreenSpace,
                                              float lane0, float lane1, float depth0, float depth1, ColourInfo colour,
                                              RectangleF? textureRect = null, bool subdivide = true)
        {
            var fullRect = textureRect ?? new RectangleF(0, 0, texture.DisplayWidth, texture.DisplayHeight);
            var grid = new TrackGrid(projection, lane0, lane1, depth0, depth1, fullRect, subdivide);

            for (int s = 0; s < grid.Strips; s++)
            {
                for (int c = 0; c < grid.Columns; c++)
                {
                    grid.GetCell(projection, s, c, out var quad, out var coords, out var cellRect);

                    ColourInfo cellColour = colour.HasSingleColour
                        ? colour
                        : new ColourInfo
                        {
                            TopLeft = colour.Interpolate(cellRect.TopLeft),
                            TopRight = colour.Interpolate(cellRect.TopRight),
                            BottomLeft = colour.Interpolate(cellRect.BottomLeft),
                            BottomRight = colour.Interpolate(cellRect.BottomRight),
                        };

                    renderer.DrawQuad(texture, quad * toScreenSpace, cellColour, textureRect: fullRect, textureCoords: coords);
                }
            }
        }

        /// <summary>
        /// Draws <paramref name="textureRect"/> of <paramref name="texture"/> across the whole track width, from the judgement
        /// line down <paramref name="height"/> pixels, with the texture spread evenly over that height (no perspective).
        /// The sides follow the lane edges' straight lines, so the track continues below the line without being magnified
        /// the way the perspective would magnify it there.
        /// </summary>
        public static void DrawBelowLine(IRenderer renderer, Texture texture, in HighwayProjection projection, Matrix3 toScreenSpace,
                                         float height, ColourInfo colour, RectangleF textureRect)
        {
            if (height <= 0)
                return;

            float top = projection.YAt(0);
            float bottom = top + height;

            int strips = Math.Clamp((int)MathF.Ceiling(height / STRIP_HEIGHT), 1, MAX_STRIPS);
            float widthChangePerStrip = Math.Abs(projection.WidthAtY(bottom) - projection.WidthAtY(top)) / strips;
            int columns = Math.Clamp((int)MathF.Ceiling(widthChangePerStrip / MAX_WIDTH_CHANGE_PER_CELL), 1, MAX_COLUMNS);

            for (int s = 0; s < strips; s++)
            {
                float tTop = (float)s / strips;
                float tBottom = (float)(s + 1) / strips;

                float yTop = top + height * tTop;
                float yBottom = top + height * tBottom;

                for (int c = 0; c < columns; c++)
                {
                    float uLeft = (float)c / columns;
                    float uRight = (float)(c + 1) / columns;

                    var quad = new Quad(
                        new Vector2(projection.XAt(uLeft, yTop), yTop),
                        new Vector2(projection.XAt(uRight, yTop), yTop),
                        new Vector2(projection.XAt(uLeft, yBottom), yBottom),
                        new Vector2(projection.XAt(uRight, yBottom), yBottom)) * toScreenSpace;

                    var coords = new RectangleF(
                        textureRect.Left + textureRect.Width * uLeft,
                        textureRect.Top + textureRect.Height * tTop,
                        textureRect.Width * (uRight - uLeft),
                        textureRect.Height * (tBottom - tTop));

                    ColourInfo cellColour = colour.HasSingleColour
                        ? colour
                        : new ColourInfo
                        {
                            TopLeft = colour.Interpolate(new Vector2(uLeft, tTop)),
                            TopRight = colour.Interpolate(new Vector2(uRight, tTop)),
                            BottomLeft = colour.Interpolate(new Vector2(uLeft, tBottom)),
                            BottomRight = colour.Interpolate(new Vector2(uRight, tBottom)),
                        };

                    renderer.DrawQuad(texture, quad, cellColour, textureRect: textureRect, textureCoords: coords);
                }
            }
        }

        /// <summary>
        /// A line running along the track at <paramref name="lane"/>, from the judgement line to <paramref name="farDepth"/>,
        /// <paramref name="width"/> track widths wide, so it thins with distance (Chunithm's guide lines). Where it would be
        /// thinner than <paramref name="minPixels"/> on screen it keeps that width instead, so it never breaks up.
        /// </summary>
        /// <remarks>
        /// On a projected plane both a lane's x and the track's width are linear in screen y, so each part is an exact
        /// trapezoid and the point where the line reaches the minimum width is found in closed form.
        /// </remarks>
        /// <param name="projection">The projection, in the space the quads are wanted in.</param>
        /// <param name="lane">Where the line runs, in lane space.</param>
        /// <param name="width">The line's width, in track widths.</param>
        /// <param name="farDepth">Where the line ends.</param>
        /// <param name="minPixels">The least width on screen.</param>
        /// <param name="near">The part from the judgement line, tapering.</param>
        /// <param name="far">The part of constant (minimum) width beyond it. Empty when <paramref name="split"/> is 1.</param>
        /// <param name="split">Where the parts meet, as a fraction of the line's length on screen (0 at the judgement line).</param>
        public static void GetLaneLine(in HighwayProjection projection, float lane, float width, float farDepth, float minPixels,
                                       out Quad near, out Quad far, out float split)
        {
            float nearY = projection.YAt(0);
            float farY = projection.YAt(farDepth);
            float nearX = projection.ToScreen(lane, 0).X;
            float farX = projection.ToScreen(lane, farDepth).X;
            float nearWidth = width * projection.WidthAt(0);
            float farWidth = width * projection.WidthAt(farDepth);

            if (farWidth >= minPixels || nearWidth <= minPixels)
            {
                near = line(nearX, nearY, Math.Max(nearWidth, minPixels), farX, farY, Math.Max(farWidth, minPixels));
                far = default;
                split = 1;
                return;
            }

            split = (nearWidth - minPixels) / (nearWidth - farWidth);

            float splitX = nearX + (farX - nearX) * split;
            float splitY = nearY + (farY - nearY) * split;

            near = line(nearX, nearY, nearWidth, splitX, splitY, minPixels);
            far = line(splitX, splitY, minPixels, farX, farY, minPixels);

            static Quad line(float x0, float y0, float w0, float x1, float y1, float w1) => new Quad(
                new Vector2(x1 - w1 / 2, y1), new Vector2(x1 + w1 / 2, y1),
                new Vector2(x0 - w0 / 2, y0), new Vector2(x0 + w0 / 2, y0));
        }

        /// <summary>
        /// Splits a line from <see cref="GetLaneLine"/> into a solid core and a fading edge on each side, so it widens smoothly
        /// on screen. Without them a line keeps whole pixels: one centred on a pixel boundary jumps from 2 to 4 px wide where
        /// its width passes 3 px.
        /// </summary>
        /// <remarks>
        /// The core is <paramref name="feather"/> narrower than the line and each edge is <paramref name="feather"/> wide,
        /// centred on the line's own edge. Faded from full to none across its width, each edge adds half of it, so the line
        /// keeps its width and brightness overall. Its sides stay on the same straight lines, offset sideways.
        /// </remarks>
        /// <param name="line">The line, as wide as wanted, at least <paramref name="feather"/>.</param>
        /// <param name="feather">The width of each fading edge (one screen pixel, in the line's space).</param>
        /// <param name="core">The solid middle.</param>
        /// <param name="left">The left edge: its right side touches the core.</param>
        /// <param name="right">The right edge: its left side touches the core.</param>
        public static void Feather(in Quad line, float feather, out Quad core, out Quad left, out Quad right)
        {
            var half = new Vector2(feather / 2, 0);

            core = new Quad(line.TopLeft + half, line.TopRight - half, line.BottomLeft + half, line.BottomRight - half);
            left = new Quad(line.TopLeft - half, line.TopLeft + half, line.BottomLeft - half, line.BottomLeft + half);
            right = new Quad(line.TopRight - half, line.TopRight + half, line.BottomRight - half, line.BottomRight + half);
        }

        /// <summary>
        /// The part of the track between lanes <paramref name="lane0"/> and <paramref name="lane1"/> from the judgement line down
        /// <paramref name="height"/> pixels, its sides on the lane edges' straight lines (as <see cref="DrawBelowLine"/> draws
        /// the stage there). A plain trapezoid: anything that varies only with screen height is drawn exactly on it.
        /// </summary>
        public static Quad GetBelowLine(in HighwayProjection projection, float lane0, float lane1, float height)
        {
            float top = projection.YAt(0);
            float bottom = top + Math.Max(0, height);

            return new Quad(
                new Vector2(projection.XAt(lane0, top), top), new Vector2(projection.XAt(lane1, top), top),
                new Vector2(projection.XAt(lane0, bottom), bottom), new Vector2(projection.XAt(lane1, bottom), bottom));
        }

        /// <summary>
        /// The band across the whole track between screen heights <paramref name="top"/> and <paramref name="bottom"/>, its
        /// sides on the track's edges (the judgement line and its glow).
        /// </summary>
        public static Quad GetAcrossTrack(in HighwayProjection projection, float top, float bottom) => GetAcrossLanes(projection, 0, 1, top, bottom);

        /// <summary>
        /// The band between lanes <paramref name="lane0"/> and <paramref name="lane1"/> and screen heights <paramref name="top"/>
        /// and <paramref name="bottom"/>, its sides on the lane edges' straight lines, above the judgement line or below it.
        /// </summary>
        public static Quad GetAcrossLanes(in HighwayProjection projection, float lane0, float lane1, float top, float bottom) => new Quad(
            new Vector2(projection.XAt(lane0, top), top), new Vector2(projection.XAt(lane1, top), top),
            new Vector2(projection.XAt(lane0, bottom), bottom), new Vector2(projection.XAt(lane1, bottom), bottom));
    }

    /// <summary>
    /// How <see cref="TrackDrawing.DrawTrackRectangle"/> splits a track rectangle into cells, and where each cell goes on
    /// screen and in the texture. Pure maths, so it can be tested without a renderer.
    /// </summary>
    public readonly struct TrackGrid
    {
        public readonly int Strips;
        public readonly int Columns;

        /// <summary>
        /// False when nothing is in front of the camera (or the rectangle is empty): draw nothing.
        /// </summary>
        public readonly bool Visible;

        private readonly float lane0;
        private readonly float lane1;
        private readonly float fullDepth0;
        private readonly float depth0;
        private readonly float depth1;
        private readonly RectangleF textureRect;

        public TrackGrid(in HighwayProjection projection, float lane0, float lane1, float depth0, float depth1, RectangleF textureRect, bool subdivide = true)
        {
            this.lane0 = lane0;
            this.lane1 = lane1;
            this.depth1 = depth1;
            this.textureRect = textureRect;

            fullDepth0 = depth0;

            // Anything nearer than the camera plane cannot be drawn. The texture keeps its mapping over the full rectangle,
            // so the part in front of the camera shows exactly the texels it would show unclamped.
            float minDepth = projection.MinDepth;
            this.depth0 = Math.Max(depth0, minDepth);

            Visible = depth1 > minDepth && depth1 > depth0;
            Strips = 0;
            Columns = 0;

            if (!Visible)
                return;

            Strips = 1;
            Columns = 1;

            if (subdivide)
            {
                float screenHeight = projection.YAt(this.depth0) - projection.YAt(depth1);
                Strips = Math.Clamp((int)MathF.Ceiling(screenHeight / TrackDrawing.STRIP_HEIGHT), 1, TrackDrawing.MAX_STRIPS);

                float widthChangePerStrip = (projection.WidthAt(this.depth0) - projection.WidthAt(depth1)) * Math.Abs(lane1 - lane0) / Strips;
                Columns = Math.Clamp((int)MathF.Ceiling(widthChangePerStrip / TrackDrawing.MAX_WIDTH_CHANGE_PER_CELL), 1, TrackDrawing.MAX_COLUMNS);
            }
        }

        /// <summary>
        /// The cell in strip <paramref name="strip"/> (0 = nearest) and column <paramref name="column"/> (0 = left).
        /// </summary>
        /// <param name="projection">The projection the grid was made with.</param>
        /// <param name="strip">The strip, counted from the near edge.</param>
        /// <param name="column">The column, counted from the left.</param>
        /// <param name="quad">The cell on screen, in the projection's space.</param>
        /// <param name="textureCoords">The cell in the texture, in texture pixels. The texture's top is the far end.</param>
        /// <param name="relative">The cell as a fraction of the whole rectangle, top-left origin (for colour gradients).</param>
        public void GetCell(in HighwayProjection projection, int strip, int column, out Quad quad, out RectangleF textureCoords, out RectangleF relative)
        {
            float dNear = depth0 + (depth1 - depth0) * strip / Strips;
            float dFar = depth0 + (depth1 - depth0) * (strip + 1) / Strips;

            // Fractions along the full rectangle, 0 at the near edge.
            float totalDepth = depth1 - fullDepth0;
            float tNear = (dNear - fullDepth0) / totalDepth;
            float tFar = (dFar - fullDepth0) / totalDepth;

            float uLeft = (float)column / Columns;
            float uRight = (float)(column + 1) / Columns;

            float laneLeft = lane0 + (lane1 - lane0) * uLeft;
            float laneRight = lane0 + (lane1 - lane0) * uRight;

            quad = projection.ToScreen(laneLeft, laneRight, dNear, dFar);

            textureCoords = new RectangleF(
                textureRect.Left + textureRect.Width * uLeft,
                textureRect.Top + textureRect.Height * (1 - tFar),
                textureRect.Width * (uRight - uLeft),
                textureRect.Height * (tFar - tNear));

            relative = new RectangleF(uLeft, 1 - tFar, uRight - uLeft, tFar - tNear);
        }
    }
}
