// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.SkinPort
{
    /// <summary>
    /// A ported skin's hit lighting (<c>lightingN</c>) on each hit and hold lighting (<c>lightingL</c>) while a hold is held,
    /// standing upright on the judgement line in the lane, at the skin's size relative to the lane, additively blended
    /// (<see cref="Highway.HighwayHitEffectStyle.Ported"/>). Timed as mania's legacy pieces: the hit animation plays once over 170 ms
    /// while fading in for 80 ms and out for 120 ms; the hold animation loops.
    /// </summary>
    /// <remarks>
    /// Results arrive through each column's own event with the same conditions as mania's explosions, so a light appears
    /// exactly when mania would show one. Nothing here feeds back into judgement or scoring. Like mania, which pools a few
    /// explosions per column and lets them overlap, each column keeps its last <see cref="HITS_PER_COLUMN"/> hits, so a hit that
    /// lands while the previous explosion still plays (a hold's release soon after its press, a fast trill) adds a second one
    /// instead of restarting the first.
    /// </remarks>
    public partial class PortedLighting : Drawable
    {
        public const double FADE_IN = 80;
        public const double FADE_OUT = 120;

        /// <summary>
        /// How many explosions may overlap in one column. One lasts <see cref="FADE_IN"/> + <see cref="FADE_OUT"/> (200 ms), so
        /// four cover hits 50 ms apart in one lane.
        /// </summary>
        public const int HITS_PER_COLUMN = 4;

        private readonly List<Column> columns = new List<Column>();
        private readonly List<Action<DrawableHitObject, JudgementResult>> handlers = new List<Action<DrawableHitObject, JudgementResult>>();

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

        /// <summary>
        /// Whether lighting is shown (and so whether hits are recorded).
        /// </summary>
        public bool Active { get; set; }

        private readonly double[] hitTimes;
        private readonly int[] nextHit;
        private readonly double[] holdStarts;
        private readonly double[] holdEnds;
        private readonly bool[] holding;

        private PortPlacement placement;
        private Vector2[] columnLanes = Array.Empty<Vector2>();
        private int columnCount;

        private IShader shader = null!;

        public PortedLighting(ManiaPlayfield playfield)
        {
            RelativeSizeAxes = Axes.Both;
            Blending = BlendingParameters.Additive;

            foreach (var stage in playfield.Stages)
                columns.AddRange(stage.Columns);

            hitTimes = new double[columns.Count * HITS_PER_COLUMN];
            nextHit = new int[columns.Count];
            holdStarts = new double[columns.Count];
            holdEnds = new double[columns.Count];
            holding = new bool[columns.Count];

            Array.Fill(hitTimes, double.NegativeInfinity);
            Array.Fill(holdEnds, double.NegativeInfinity);
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            for (int i = 0; i < columns.Count; i++)
            {
                int index = i;
                var column = columns[i];

                void handler(DrawableHitObject judgedObject, JudgementResult result) => onNewResult(index, column, judgedObject, result);

                column.NewResult += handler;
                handlers.Add(handler);
            }
        }

        private void onNewResult(int index, Column column, DrawableHitObject judgedObject, JudgementResult result)
        {
            // The same conditions as mania's Column.OnNewResult, which adds a hit explosion.
            if (!Active || !result.IsHit || !judgedObject.DisplayResult || !column.DisplayJudgements.Value)
                return;

            RecordHit(hitTimes, nextHit, index, Time.Current);
        }

        /// <summary>
        /// Records a hit in <paramref name="column"/> at <paramref name="time"/>, over that column's oldest one.
        /// </summary>
        /// <param name="hitTimes">Every column's recent hit times, <see cref="HITS_PER_COLUMN"/> per column.</param>
        /// <param name="nextHit">Each column's slot to fill next.</param>
        /// <param name="column">The column hit.</param>
        /// <param name="time">When.</param>
        public static void RecordHit(Span<double> hitTimes, Span<int> nextHit, int column, double time)
        {
            hitTimes[column * HITS_PER_COLUMN + nextHit[column]] = time;
            nextHit[column] = (nextHit[column] + 1) % HITS_PER_COLUMN;
        }

        /// <summary>
        /// Forgets the hits after <paramref name="now"/>: rewound past, they have not happened yet.
        /// </summary>
        public static void ForgetHitsAfter(Span<double> hitTimes, double now)
        {
            for (int i = 0; i < hitTimes.Length; i++)
            {
                if (hitTimes[i] > now)
                    hitTimes[i] = double.NegativeInfinity;
            }
        }

        /// <summary>
        /// The times of each column's recent lit hits (<see cref="HITS_PER_COLUMN"/> per column, in playfield column order;
        /// negative infinity for none).
        /// </summary>
        public ReadOnlySpan<double> HitTimes => hitTimes;

        public void Place(in PortPlacement placement, ReadOnlySpan<Vector2> lanes)
        {
            this.placement = placement;

            if (columnLanes.Length < lanes.Length)
                columnLanes = new Vector2[lanes.Length];

            lanes.CopyTo(columnLanes);
            columnCount = Math.Min(lanes.Length, columns.Count);

            double now = Time.Current;

            ForgetHitsAfter(hitTimes, now);

            for (int i = 0; i < columns.Count; i++)
            {
                // Rewound past a release: it has not happened yet.
                if (holdEnds[i] > now)
                    holdEnds[i] = double.NegativeInfinity;
                if (holdStarts[i] > now)
                    holdStarts[i] = now;

                bool held = Active && HighwayHitEffectLayer.IsHolding(columns[i]);

                if (held && !holding[i])
                    holdStarts[i] = now;
                else if (!held && holding[i])
                    holdEnds[i] = now;

                holding[i] = held;
            }

            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>
        /// Forgets earlier hits and holds (while lighting is off nothing is recorded): nothing is left lit when shown again.
        /// </summary>
        public void ForgetHits()
        {
            Array.Fill(hitTimes, double.NegativeInfinity);
            Array.Fill(holdEnds, double.NegativeInfinity);
            Array.Clear(holding);
        }

        /// <summary>
        /// A hit light's opacity <paramref name="sinceHit"/> after the hit: up over <see cref="FADE_IN"/>, then down over
        /// <see cref="FADE_OUT"/>.
        /// </summary>
        public static float HitAlpha(double sinceHit)
        {
            if (!(sinceHit >= 0))
                return 0;

            if (sinceHit < FADE_IN)
                return (float)(sinceHit / FADE_IN);

            return (float)Math.Max(0, 1 - (sinceHit - FADE_IN) / FADE_OUT);
        }

        protected override DrawNode CreateDrawNode() => new PortedLightingDrawNode(this);

        protected override void Dispose(bool isDisposing)
        {
            for (int i = 0; i < handlers.Count; i++)
                columns[i].NewResult -= handlers[i];

            handlers.Clear();

            base.Dispose(isDisposing);
        }

        private class PortedLightingDrawNode : DrawNode
        {
            protected new PortedLighting Source => (PortedLighting)base.Source;

            private IShader shader = null!;
            private PortedSkin? skin;
            private PortPlacement placement;
            private float alpha;
            private double time;

            private Vector2[] columnLanes = Array.Empty<Vector2>();
            private double[] hitTimes = Array.Empty<double>();
            private double[] holdStarts = Array.Empty<double>();
            private double[] holdEnds = Array.Empty<double>();
            private bool[] holding = Array.Empty<bool>();
            private int columnCount;

            public PortedLightingDrawNode(PortedLighting source)
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
                time = Source.Time.Current;
                columnCount = Source.columnCount;

                if (columnLanes.Length < columnCount)
                {
                    columnLanes = new Vector2[columnCount];
                    hitTimes = new double[columnCount * HITS_PER_COLUMN];
                    holdStarts = new double[columnCount];
                    holdEnds = new double[columnCount];
                    holding = new bool[columnCount];
                }

                Array.Copy(Source.columnLanes, columnLanes, columnCount);
                Array.Copy(Source.hitTimes, hitTimes, columnCount * HITS_PER_COLUMN);
                Array.Copy(Source.holdStarts, holdStarts, columnCount);
                Array.Copy(Source.holdEnds, holdEnds, columnCount);
                Array.Copy(Source.holding, holding, columnCount);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (skin == null || alpha <= 0 || !placement.Stage.IsValid)
                    return;

                shader.Bind();

                for (int i = 0; i < columnCount && i < skin.Columns.Length; i++)
                {
                    var column = skin.Columns[i];

                    // Hold lighting, under the hit lighting.
                    double holdTime = holding[i] ? time - holdStarts[i] : time - holdEnds[i];
                    float holdAlpha = holding[i]
                        ? (float)Math.Clamp(holdTime / FADE_IN, 0, 1)
                        : (float)Math.Clamp(1 - holdTime / FADE_OUT, 0, 1);

                    if (holdAlpha > 0 && PortedNoteShapes.FrameAt(column.HoldLight, column.HoldLightFrameLength, time - holdStarts[i]) is Texture holdLight)
                        draw(renderer, holdLight, columnLanes[i], column.HoldLightScale, holdAlpha);

                    if (column.Explosion.Length == 0)
                        continue;

                    for (int h = 0; h < HITS_PER_COLUMN; h++)
                    {
                        double sinceHit = time - hitTimes[i * HITS_PER_COLUMN + h];
                        float hitAlpha = HitAlpha(sinceHit);

                        if (!(hitAlpha > 0))
                            continue;

                        // Plays once, then holds its last frame while it fades.
                        int frame = (int)Math.Min(column.Explosion.Length - 1, Math.Max(0, Math.Floor(sinceHit / column.ExplosionFrameLength)));
                        draw(renderer, column.Explosion[frame], columnLanes[i], column.ExplosionScale, hitAlpha);
                    }
                }

                shader.Unbind();
            }

            /// <summary>
            /// Centred on the lane at the judgement line, at the image's size in mania units times the skin's scale: its flat
            /// size on a wider track (skins draw lighting several lanes wide, so growing it with the lanes would swamp the
            /// field), shrunk with the track on a narrower one.
            /// </summary>
            private void draw(IRenderer renderer, Texture texture, Vector2 lanes, float scale, float opacity)
            {
                var stage = placement.Stage;
                float size = scale * stage.UnitSize * Math.Min(1, placement.WidthScale);
                float width = texture.DisplayWidth * size;
                float height = texture.DisplayHeight * size;

                if (!(width > 0) || !(height > 0))
                    return;

                float x = placement.Projection.XAt((lanes.X + lanes.Y) / 2, placement.LineY);
                float y = placement.LineY;

                var quad = new Quad(x - width / 2, y - height / 2, width, height);
                renderer.DrawQuad(texture, quad * DrawInfo.Matrix, Color4.White.Opacity(opacity * alpha));
            }
        }
    }
}
