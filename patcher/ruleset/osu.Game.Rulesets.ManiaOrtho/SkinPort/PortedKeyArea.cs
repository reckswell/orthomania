// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;
using osu.Game.Extensions;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.SkinPort
{
    /// <summary>
    /// A ported skin's key area: each column's key image (pressed or not) and the stage bottom, standing upright at and below
    /// the judgement line where <see cref="Highway.SkinKeyArea"/> puts them. Over the notes, or under them for skins that ask for
    /// keys under notes (<see cref="PortedColumn.KeysUnderNotes"/>).
    /// </summary>
    public partial class PortedKeyArea : Drawable
    {
        /// <summary>
        /// A released key shows its pressed image a little longer (mania's legacy key area waits for the explosion's fade-in).
        /// </summary>
        public const double RELEASE_DELAY = 80;

        /// <summary>
        /// The stage bottom image is drawn at 1.6 times its size (<c>LegacyStageForeground</c>).
        /// </summary>
        public const float BOTTOM_SCALE = 1.6f;

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

        private PortPlacement placement;
        private Vector2[] columnLanes = Array.Empty<Vector2>();
        private Vector2[] stageLanes = Array.Empty<Vector2>();
        private bool[] keysDown = Array.Empty<bool>();
        private bool[] wasPressed = Array.Empty<bool>();
        private double[] releaseTimes = Array.Empty<double>();
        private int columnCount;
        private int stageCount;

        private IShader shader = null!;

        public PortedKeyArea()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders, GameHost host)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);

            // Keys follow real time, as mania's key area does: not faster under Double Time, not frozen in a pause.
            this.ApplyGameWideClock(host);
        }

        /// <summary>
        /// Forgets earlier presses: no key is left showing pressed from before.
        /// </summary>
        public void ForgetPresses()
        {
            Array.Clear(wasPressed);
            Array.Clear(keysDown);
            Array.Fill(releaseTimes, double.NegativeInfinity);
        }

        /// <summary>
        /// Whether each column shows its pressed key image this frame.
        /// </summary>
        public ReadOnlySpan<bool> KeysDown => keysDown.AsSpan(0, columnCount);

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
                Array.Resize(ref keysDown, columns.Length);
                Array.Resize(ref wasPressed, columns.Length);
                int old = releaseTimes.Length;
                Array.Resize(ref releaseTimes, columns.Length);
                // Never released yet: no light or pressed key left over from time zero.
                Array.Fill(releaseTimes, double.NegativeInfinity, old, columns.Length - old);
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

                double since = now - releaseTimes[i];
                keysDown[i] = down || (since >= 0 && since < RELEASE_DELAY);
            }

            Invalidate(Invalidation.DrawNode);
        }

        protected override DrawNode CreateDrawNode() => new PortedKeyAreaDrawNode(this);

        private class PortedKeyAreaDrawNode : DrawNode
        {
            protected new PortedKeyArea Source => (PortedKeyArea)base.Source;

            private IShader shader = null!;
            private PortedSkin? skin;
            private PortPlacement placement;
            private float alpha;

            private Vector2[] columnLanes = Array.Empty<Vector2>();
            private Vector2[] stageLanes = Array.Empty<Vector2>();
            private bool[] keysDown = Array.Empty<bool>();
            private int columnCount;
            private int stageCount;

            public PortedKeyAreaDrawNode(PortedKeyArea source)
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

                columnCount = Source.columnCount;
                stageCount = Source.stageCount;

                if (columnLanes.Length < columnCount)
                {
                    columnLanes = new Vector2[Source.columnLanes.Length];
                    keysDown = new bool[Source.columnLanes.Length];
                }

                if (stageLanes.Length < stageCount)
                    stageLanes = new Vector2[Source.stageLanes.Length];

                Array.Copy(Source.columnLanes, columnLanes, columnCount);
                Array.Copy(Source.keysDown, keysDown, columnCount);
                Array.Copy(Source.stageLanes, stageLanes, stageCount);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (skin == null || alpha <= 0 || !placement.Stage.IsValid || !placement.ShowKeys)
                    return;

                shader.Bind();

                var stage = placement.Stage;
                Color4 colour = Color4.White.Opacity(alpha);

                for (int i = 0; i < columnCount && i < skin.Columns.Length; i++)
                {
                    var column = skin.Columns[i];
                    var key = keysDown[i] ? column.KeyDown ?? column.KeyUp : column.KeyUp;

                    if (key == null || key.DisplayHeight <= 0)
                        continue;

                    // Bottom-anchored at the bottom of the playfield, the column's width and its own height, as mania's legacy
                    // key area. A key reaching above the hit position stands upright above the line (as receptors do).
                    float top = stage.Size.Y - key.DisplayHeight * stage.UnitSize;

                    PortDrawing.DrawFlatRect(renderer, key, new RectangleF(0, 0, key.DisplayWidth, key.DisplayHeight), placement, DrawInfo.Matrix,
                        columnLanes[i].X, columnLanes[i].Y, top, stage.Size.Y, colour, uprightAbove: true);
                }

                for (int s = 0; s < stageCount && s < skin.Stages.Length; s++)
                {
                    if (skin.Stages[s].Bottom is not Texture bottom || bottom.DisplayHeight <= 0)
                        continue;

                    var lanes = stageLanes[s];

                    // At the bottom centre of the stage at 1.6 times its size, keeping its proportions: its flat size on a wider
                    // track, shrunk with a narrower one.
                    float scale = BOTTOM_SCALE * stage.UnitSize * Math.Min(1, placement.WidthScale);
                    float halfWidth = bottom.DisplayWidth * scale / 2 / Math.Max(1e-3f, placement.Projection.WidthAt(0));
                    float centre = (lanes.X + lanes.Y) / 2;

                    PortDrawing.DrawFlatRect(renderer, bottom, new RectangleF(0, 0, bottom.DisplayWidth, bottom.DisplayHeight), placement, DrawInfo.Matrix,
                        centre - halfWidth, centre + halfWidth, stage.Size.Y - bottom.DisplayHeight * scale, stage.Size.Y, colour, uprightAbove: true);
                }

                shader.Unbind();
            }
        }
    }
}
