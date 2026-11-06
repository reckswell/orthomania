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
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;
using osu.Game.Extensions;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.SkinPort
{
    /// <summary>
    /// A ported skin's stage on the highway, under the notes (<see cref="HighwayTrackStyle.Ported"/>): each column's background
    /// colour and lines, the stage's side images, the hit target (stage hint and judgement line) and each pressed column's
    /// light, from the skin's own images and <c>skin.ini</c> values. Laid out as osu!mania lays the skin out flat, then
    /// tilted with the track (<see cref="PortPlacement"/>).
    /// </summary>
    /// <remarks>
    /// Purely visual: the keys' state is read from the key binding container and nothing here handles input.
    /// </remarks>
    public partial class PortedTrack : Drawable
    {
        /// <summary>
        /// mania's legacy column lines are squashed sideways by this much (<c>LegacyStageBackground</c>).
        /// </summary>
        public const float LINE_SQUASH = 0.74f;

        /// <summary>
        /// The hint is drawn at this many times its image's height (<c>LegacyHitTarget</c>).
        /// </summary>
        public const float HINT_SCALE = 0.9f * 1.6025f;

        /// <summary>
        /// A released column's light fades and shrinks to nothing over this long (<c>LegacyColumnBackground</c>).
        /// </summary>
        public const double LIGHT_FADE = 250;

        private PortedSkin? skin;

        public PortedSkin? Skin
        {
            get => skin;
            set
            {
                skin = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private float backgroundOpacity = 1;

        /// <summary>
        /// The lane backgrounds' opacity, times the skin's own (<see cref="HighwaySettings.TrackOpacity"/>). The stage's sides,
        /// lines, hit target and lights keep theirs.
        /// </summary>
        public float BackgroundOpacity
        {
            get => backgroundOpacity;
            set
            {
                value = Math.Clamp(value, 0, 1);

                if (backgroundOpacity == value)
                    return;

                backgroundOpacity = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        /// <summary>
        /// A lane background as drawn: the skin's colour with its alpha doubled as stable does
        /// (<c>LegacyColourCompatibility.ApplyWithDoubledAlpha</c>), times <see cref="BackgroundOpacity"/> and the drawable's
        /// own alpha.
        /// </summary>
        public static Color4 LaneBackground(Color4 skinColour, float opacity, float drawAlpha) => DoubledAlpha(skinColour, opacity * drawAlpha);

        /// <summary>
        /// <paramref name="colour"/> as stable draws a skin colour: its alpha, then the drawable's alpha set to it too (alpha
        /// squared), times <paramref name="alpha"/>.
        /// </summary>
        public static Color4 DoubledAlpha(Color4 colour, float alpha) => new Color4(colour.R, colour.G, colour.B, colour.A * colour.A * alpha);

        private PortPlacement placement;

        private Vector2[] columnLanes = Array.Empty<Vector2>();
        private Vector2[] stageLanes = Array.Empty<Vector2>();
        private int columnCount;
        private int stageCount;

        private float[] lightLevels = Array.Empty<float>();
        private double[] releaseTimes = Array.Empty<double>();
        private bool[] wasPressed = Array.Empty<bool>();

        private IShader shader = null!;

        public PortedTrack()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders, GameHost host)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);

            // Lights fade in real time, as mania's column backgrounds do: not faster under Double Time, not frozen in a pause.
            this.ApplyGameWideClock(host);
        }

        /// <summary>
        /// Forgets earlier presses: no light is left fading from before.
        /// </summary>
        public void ForgetPresses()
        {
            Array.Clear(wasPressed);
            Array.Fill(releaseTimes, double.NegativeInfinity);
        }

        /// <summary>
        /// Each column's light this frame: 1 while pressed, fading to 0 after release.
        /// </summary>
        public ReadOnlySpan<float> LightLevels => lightLevels.AsSpan(0, columnCount);

        /// <summary>
        /// Places the stage for this frame and steps each column's light. Called by the highway once per frame.
        /// </summary>
        /// <param name="placement">Where the skin's flat stage goes on the highway.</param>
        /// <param name="stages">Each stage's left and right edge in lane space.</param>
        /// <param name="columns">Each column's left and right edge in lane space, in playfield column order.</param>
        /// <param name="pressed">Whether each column's key is down.</param>
        public void Place(in PortPlacement placement, ReadOnlySpan<Vector2> stages, ReadOnlySpan<Vector2> columns, ReadOnlySpan<bool> pressed)
        {
            this.placement = placement;

            if (stageLanes.Length < stages.Length)
                stageLanes = new Vector2[stages.Length];
            stages.CopyTo(stageLanes);
            stageCount = stages.Length;

            if (columnLanes.Length < columns.Length)
            {
                columnLanes = new Vector2[columns.Length];
                Array.Resize(ref lightLevels, columns.Length);
                int old = releaseTimes.Length;
                Array.Resize(ref releaseTimes, columns.Length);
                // Never released yet: no light or pressed key left over from time zero.
                Array.Fill(releaseTimes, double.NegativeInfinity, old, columns.Length - old);
                Array.Resize(ref wasPressed, columns.Length);
            }

            columns.CopyTo(columnLanes);
            columnCount = columns.Length;

            double now = Time.Current;

            for (int i = 0; i < columnCount; i++)
            {
                bool down = i < pressed.Length && pressed[i];

                if (wasPressed[i] && !down)
                    releaseTimes[i] = now;

                wasPressed[i] = down;
                lightLevels[i] = LightLevel(down, now - releaseTimes[i]);
            }

            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>
        /// A column's light: full while pressed, then linearly down to nothing over <see cref="LIGHT_FADE"/> (its alpha and
        /// height both, as mania's legacy light). Rewound time puts it out.
        /// </summary>
        public static float LightLevel(bool pressed, double sinceRelease)
        {
            if (pressed)
                return 1;

            if (!(sinceRelease >= 0))
                return 0;

            return (float)Math.Max(0, 1 - sinceRelease / LIGHT_FADE);
        }

        protected override DrawNode CreateDrawNode() => new PortedTrackDrawNode(this);

        private class PortedTrackDrawNode : DrawNode
        {
            protected new PortedTrack Source => (PortedTrack)base.Source;

            private IShader shader = null!;
            private PortedSkin? skin;
            private PortPlacement placement;
            private float alpha;
            private float backgroundOpacity;
            private double time;

            private Vector2[] columnLanes = Array.Empty<Vector2>();
            private Vector2[] stageLanes = Array.Empty<Vector2>();
            private float[] lightLevels = Array.Empty<float>();
            private int columnCount;
            private int stageCount;

            public PortedTrackDrawNode(PortedTrack source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                skin = Source.skin;
                placement = Source.placement;
                alpha = DrawColourInfo.Colour.MaxAlpha;
                backgroundOpacity = Source.backgroundOpacity;
                time = Source.Time.Current;

                columnCount = Source.columnCount;
                stageCount = Source.stageCount;

                if (columnLanes.Length < columnCount)
                {
                    columnLanes = new Vector2[Source.columnLanes.Length];
                    lightLevels = new float[Source.columnLanes.Length];
                }

                if (stageLanes.Length < stageCount)
                    stageLanes = new Vector2[Source.stageLanes.Length];

                Array.Copy(Source.columnLanes, columnLanes, columnCount);
                Array.Copy(Source.lightLevels, lightLevels, columnCount);
                Array.Copy(Source.stageLanes, stageLanes, stageCount);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (skin == null || alpha <= 0 || !placement.Stage.IsValid)
                    return;

                shader.Bind();

                var stage = placement.Stage;
                var white = renderer.WhitePixel;
                var whiteRect = new RectangleF(0, 0, white.DisplayWidth, white.DisplayHeight);

                int columnIndex = 0;

                for (int s = 0; s < stageCount && s < skin.Stages.Length; s++)
                {
                    var ported = skin.Stages[s];
                    var lanes = stageLanes[s];
                    int first = columnIndex;

                    // Lane backgrounds, the full height of the stage, under everything (alpha doubled, as stable sets colours).
                    for (int c = 0; c < ported.Columns.Length && columnIndex < columnCount; c++, columnIndex++)
                    {
                        var column = ported.Columns[c];
                        Color4 background = LaneBackground(column.Background, backgroundOpacity, alpha);

                        if (background.A > 0)
                        {
                            PortDrawing.DrawFlatRect(renderer, white, whiteRect, placement, DrawInfo.Matrix, columnLanes[columnIndex].X, columnLanes[columnIndex].Y,
                                0, stage.Size.Y, background);
                        }
                    }

                    // The stage's side images, outside its edges, the full height of the stage.
                    drawSide(renderer, ported.Left, lanes.X, true);
                    drawSide(renderer, ported.Right, lanes.Y, false);

                    // Column lines, from the hit position up, thinning with distance (never under a pixel).
                    for (int c = 0; c < ported.Columns.Length && first + c < columnCount; c++)
                    {
                        var column = ported.Columns[c];
                        var columnLane = columnLanes[first + c];
                        Color4 line = DoubledAlpha(column.LineColour, alpha);

                        if (line.A <= 0)
                            continue;

                        if (column.HasLeftLine)
                            drawLine(renderer, columnLane.X, column.LeftLineWidth, true, line);

                        if (column.HasRightLine)
                            drawLine(renderer, columnLane.Y, column.RightLineWidth, false, line);
                    }

                    // The hit target, sandwiched between the lane backgrounds and the lights as in mania's legacy stage.
                    drawHitTarget(renderer, ported, lanes);

                    // Lights rising from each pressed column.
                    for (int c = 0; c < ported.Columns.Length && first + c < columnCount; c++)
                    {
                        float level = lightLevels[first + c];

                        if (level <= 0)
                            continue;

                        var light = PortedNoteShapes.FrameAt(ported.Light, ported.LightFrameLength, time);

                        if (light == null || light.DisplayHeight <= 0)
                            continue;

                        var column = ported.Columns[c];
                        var columnLane = columnLanes[first + c];

                        // Bottom-anchored LightPosition above the bottom of the playfield, the column's width and its image's
                        // height, shrinking towards its bottom as it fades.
                        float bottom = stage.Size.Y - column.LightPosition * stage.UnitSize;
                        float height = light.DisplayHeight * stage.UnitSize * level;

                        PortDrawing.DrawFlatRect(renderer, light, new RectangleF(0, 0, light.DisplayWidth, light.DisplayHeight), placement, DrawInfo.Matrix,
                            columnLane.X, columnLane.Y, bottom - height, bottom, column.LightColour.Opacity(column.LightColour.A * level * alpha));
                    }
                }

                shader.Unbind();
            }

            private void drawSide(IRenderer renderer, Texture? texture, float edge, bool left)
            {
                if (texture == null || texture.DisplayWidth <= 0)
                    return;

                var stage = placement.Stage;
                float width = texture.DisplayWidth * stage.UnitSize * placement.LanePerPixel;
                float lane0 = left ? edge - width : edge;
                float lane1 = left ? edge : edge + width;

                PortDrawing.DrawFlatRect(renderer, texture, new RectangleF(0, 0, texture.DisplayWidth, texture.DisplayHeight), placement, DrawInfo.Matrix,
                    lane0, lane1, 0, stage.Size.Y, Color4.White.Opacity(alpha));
            }

            private void drawLine(IRenderer renderer, float edge, float width, bool left, Color4 colour)
            {
                var stage = placement.Stage;
                float laneWidth = width * LINE_SQUASH * stage.UnitSize * placement.LanePerPixel;

                if (!(laneWidth > 0))
                    return;

                // Inside the column, against its edge.
                float centre = left ? edge + laneWidth / 2 : edge - laneWidth / 2;

                TrackDrawing.GetLaneLine(placement.Projection, centre, laneWidth, placement.ScrollLength, 1, out var near, out var far, out float split);

                ColourInfo lineColour = colour.Opacity(colour.A * alpha);
                renderer.DrawQuad(renderer.WhitePixel, near * DrawInfo.Matrix, lineColour);

                if (split < 1)
                    renderer.DrawQuad(renderer.WhitePixel, far * DrawInfo.Matrix, lineColour);
            }

            private void drawHitTarget(IRenderer renderer, PortedStage ported, Vector2 lanes)
            {
                var stage = placement.Stage;
                float lineY = placement.LineY;
                bool vertical = placement.VerticalBelow;

                // The stage hint, centred on the line, standing upright at its flat height.
                if (ported.Hint is Texture hint && hint.DisplayHeight > 0)
                {
                    float half = hint.DisplayHeight * HINT_SCALE * stage.UnitSize / 2;

                    PortDrawing.DrawUpright(renderer, hint, new RectangleF(0, 0, hint.DisplayWidth, hint.DisplayHeight), placement.Projection, DrawInfo.Matrix,
                        lanes.X, lanes.Y, lineY - half, lineY + half, vertical, Color4.White.Opacity(alpha));
                }

                if (ported.ShowJudgementLine)
                {
                    float thickness = Math.Max(1, stage.UnitSize);
                    var colour = ported.JudgementLineColour;
                    var white = renderer.WhitePixel;

                    PortDrawing.DrawUpright(renderer, white, new RectangleF(0, 0, white.DisplayWidth, white.DisplayHeight), placement.Projection, DrawInfo.Matrix,
                        lanes.X, lanes.Y, lineY, lineY + thickness, vertical, colour.Opacity(colour.A * 0.9f * alpha));
                }
            }
        }
    }
}
