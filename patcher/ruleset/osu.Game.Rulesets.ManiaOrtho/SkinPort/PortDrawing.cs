// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.SkinPort
{
    /// <summary>
    /// Draws a skin's images on the highway, for use inside a <c>DrawNode</c>: lying on the track in perspective, or upright
    /// (flat on screen) at and below the judgement line.
    /// </summary>
    public static class PortDrawing
    {
        /// <summary>
        /// Draws <paramref name="rect"/> of <paramref name="texture"/> lying on the track between lanes <paramref name="lane0"/>
        /// and <paramref name="lane1"/>, from depth <paramref name="depth0"/> (near) to <paramref name="depth1"/> (far),
        /// perspective-correct. The texture's top is at the far end, or at the near end when <paramref name="flip"/>.
        /// </summary>
        public static void DrawOnTrack(IRenderer renderer, Texture texture, RectangleF rect, in HighwayProjection projection, Matrix3 toScreenSpace,
                                       float lane0, float lane1, float depth0, float depth1, ColourInfo colour, bool flip = false)
        {
            if (!flip)
            {
                TrackDrawing.DrawTrackRectangle(renderer, texture, projection, toScreenSpace, lane0, lane1, depth0, depth1, colour, rect);
                return;
            }

            var grid = new TrackGrid(projection, lane0, lane1, depth0, depth1, rect);

            for (int s = 0; s < grid.Strips; s++)
            {
                for (int c = 0; c < grid.Columns; c++)
                {
                    grid.GetCell(projection, s, c, out var quad, out _, out var relative);

                    // The same cell of the texture mirrored top to bottom: its top row goes to the near end.
                    var coords = new RectangleF(
                        rect.Left + rect.Width * relative.Left,
                        rect.Top + rect.Height * (1 - relative.Bottom),
                        rect.Width * relative.Width,
                        rect.Height * relative.Height);

                    ColourInfo cellColour = colour.HasSingleColour
                        ? colour
                        : new ColourInfo
                        {
                            // The quad is passed near edge first, so its "top" colours are the near ones.
                            TopLeft = colour.Interpolate(relative.BottomLeft),
                            TopRight = colour.Interpolate(relative.BottomRight),
                            BottomLeft = colour.Interpolate(relative.TopLeft),
                            BottomRight = colour.Interpolate(relative.TopRight),
                        };

                    var nearFirst = new Quad(quad.BottomLeft, quad.BottomRight, quad.TopLeft, quad.TopRight);
                    renderer.DrawQuad(texture, nearFirst * toScreenSpace, cellColour, textureRect: rect, textureCoords: coords);
                }
            }
        }

        /// <summary>
        /// Draws <paramref name="rect"/> of <paramref name="texture"/> upright, spread evenly from screen height
        /// <paramref name="yTop"/> to <paramref name="yBottom"/>, between lanes <paramref name="lane0"/> and
        /// <paramref name="lane1"/>: with upright sides at the lanes' positions on the judgement line when
        /// <paramref name="vertical"/>, otherwise on the lane edges' straight lines (widening below the line, as the track
        /// continues there).
        /// </summary>
        public static void DrawUpright(IRenderer renderer, Texture texture, RectangleF rect, in HighwayProjection projection, Matrix3 toScreenSpace,
                                       float lane0, float lane1, float yTop, float yBottom, bool vertical, ColourInfo colour)
        {
            if (!(yBottom > yTop))
                return;

            if (vertical)
            {
                float lineY = projection.YAt(0);
                float x0 = projection.XAt(lane0, lineY);
                float x1 = projection.XAt(lane1, lineY);

                var quad = new Quad(new Vector2(x0, yTop), new Vector2(x1, yTop), new Vector2(x0, yBottom), new Vector2(x1, yBottom));
                renderer.DrawQuad(texture, quad * toScreenSpace, colour, textureRect: rect, textureCoords: rect);
                return;
            }

            float height = yBottom - yTop;
            int strips = Math.Clamp((int)MathF.Ceiling(height / TrackDrawing.STRIP_HEIGHT), 1, TrackDrawing.MAX_STRIPS);
            float widthChange = Math.Abs(projection.WidthAtY(yBottom) - projection.WidthAtY(yTop)) * Math.Abs(lane1 - lane0) / strips;
            int columns = Math.Clamp((int)MathF.Ceiling(widthChange / TrackDrawing.MAX_WIDTH_CHANGE_PER_CELL), 1, TrackDrawing.MAX_COLUMNS);

            // A trapezoid with the texture spread linearly in screen y: exact when split into cells that are nearly
            // parallelograms, as TrackDrawing.DrawBelowLine does.
            if (strips * columns == 1 || widthChange < 0.5f)
            {
                strips = 1;
                columns = 1;
            }

            for (int s = 0; s < strips; s++)
            {
                float tTop = (float)s / strips;
                float tBottom = (float)(s + 1) / strips;
                float y0 = yTop + height * tTop;
                float y1 = yTop + height * tBottom;

                for (int c = 0; c < columns; c++)
                {
                    float uLeft = (float)c / columns;
                    float uRight = (float)(c + 1) / columns;
                    float laneLeft = lane0 + (lane1 - lane0) * uLeft;
                    float laneRight = lane0 + (lane1 - lane0) * uRight;

                    var quad = new Quad(
                        new Vector2(projection.XAt(laneLeft, y0), y0), new Vector2(projection.XAt(laneRight, y0), y0),
                        new Vector2(projection.XAt(laneLeft, y1), y1), new Vector2(projection.XAt(laneRight, y1), y1));

                    var coords = new RectangleF(
                        rect.Left + rect.Width * uLeft,
                        rect.Top + rect.Height * tTop,
                        rect.Width * (uRight - uLeft),
                        rect.Height * (tBottom - tTop));

                    ColourInfo cellColour = colour.HasSingleColour
                        ? colour
                        : new ColourInfo
                        {
                            TopLeft = colour.Interpolate(new Vector2(uLeft, tTop)),
                            TopRight = colour.Interpolate(new Vector2(uRight, tTop)),
                            BottomLeft = colour.Interpolate(new Vector2(uLeft, tBottom)),
                            BottomRight = colour.Interpolate(new Vector2(uRight, tBottom)),
                        };

                    renderer.DrawQuad(texture, quad * toScreenSpace, cellColour, textureRect: rect, textureCoords: coords);
                }
            }
        }

        /// <summary>
        /// Draws the skin's flat-stage rectangle between rows <paramref name="flatTop"/> and <paramref name="flatBottom"/>
        /// (the highway's pixels, before tilting) as the porter places it: the part above the hit position lying on the track
        /// (or, when <paramref name="uprightAbove"/>, standing at its flat height above the line), the part below it in the key
        /// area. The texture rectangle is split between the parts in proportion.
        /// </summary>
        /// <param name="renderer">The renderer.</param>
        /// <param name="texture">The texture, or the renderer's white pixel for a plain colour.</param>
        /// <param name="rect">The part of the texture to draw, in texture pixels; its top is at <paramref name="flatTop"/>.</param>
        /// <param name="placement">This frame's placement.</param>
        /// <param name="toScreenSpace">The drawable's local-to-screen matrix.</param>
        /// <param name="lane0">The left edge, in lane space.</param>
        /// <param name="lane1">The right edge, in lane space.</param>
        /// <param name="flatTop">The top row, in the flat stage.</param>
        /// <param name="flatBottom">The bottom row, in the flat stage.</param>
        /// <param name="colour">The colour.</param>
        /// <param name="uprightAbove">Whether the part above the line stands upright instead of lying on the track.</param>
        /// <param name="drawBelow">Whether to draw the part below the line at all.</param>
        public static void DrawFlatRect(IRenderer renderer, Texture texture, RectangleF rect, in PortPlacement placement, Matrix3 toScreenSpace,
                                        float lane0, float lane1, float flatTop, float flatBottom, ColourInfo colour, bool uprightAbove = false, bool drawBelow = true)
        {
            float height = flatBottom - flatTop;

            if (!(height > 0))
                return;

            float hitY = placement.Stage.HitY;

            // Above the line.
            if (flatTop < hitY)
            {
                float bottom = Math.Min(flatBottom, hitY);
                var part = slice(rect, (flatTop - flatTop) / height, (bottom - flatTop) / height);

                if (uprightAbove)
                {
                    DrawUpright(renderer, texture, part, placement.Projection, toScreenSpace, lane0, lane1,
                        placement.ScreenYUprightAbove(flatTop), placement.ScreenYUprightAbove(bottom), placement.VerticalBelow, colour);
                }
                else
                {
                    float depth0 = placement.DepthOfFlatY(bottom);
                    float depth1 = placement.DepthOfFlatY(flatTop);

                    // Past the scrolling area (or the wall) nothing is drawn: keep the texture where it was.
                    float limit = placement.ScrollLength;

                    if (depth1 > limit && depth1 > depth0)
                    {
                        float keep = (limit - depth0) / (depth1 - depth0);
                        part = slice(part, 1 - keep, 1);
                        depth1 = limit;
                    }

                    if (depth1 > depth0)
                        DrawOnTrack(renderer, texture, part, placement.Projection, toScreenSpace, lane0, lane1, depth0, depth1, colour);
                }
            }

            // Below the line.
            if (drawBelow && flatBottom > hitY)
            {
                float top = Math.Max(flatTop, hitY);
                var part = slice(rect, (top - flatTop) / height, 1);

                DrawUpright(renderer, texture, part, placement.Projection, toScreenSpace, lane0, lane1,
                    placement.ScreenYBelow(top), placement.ScreenYBelow(flatBottom), placement.VerticalBelow, colour);
            }
        }

        /// <summary>
        /// The rows of <paramref name="rect"/> from <paramref name="from"/> to <paramref name="to"/>, as fractions of its height.
        /// </summary>
        private static RectangleF slice(RectangleF rect, float from, float to)
            => new RectangleF(rect.Left, rect.Top + rect.Height * from, rect.Width, rect.Height * (to - from));
    }
}
