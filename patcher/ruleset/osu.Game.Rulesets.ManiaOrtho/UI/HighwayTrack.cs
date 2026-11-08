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
    /// The highway's own track, after Chunithm (research <c>game-chunithm.md</c> P4, <c>game-chunithm-feedback.md</c> P5,
    /// P6 and P8): a near-black floor under each stage with a dark teal rail on each side, continuing below the judgement
    /// line to the bottom of the screen, and a beam rising from the line in each pressed lane. Drawn under mania's warped
    /// stage, whose own backgrounds and receptors are hidden meanwhile (<see cref="HighwayTrackStyle.Highway"/>).
    /// </summary>
    /// <remarks>
    /// Purely visual: which lanes are pressed is read from the key binding container and nothing here handles input.
    /// </remarks>
    public partial class HighwayTrack : Drawable
    {
        /// <summary>
        /// Each side rail's width, as a share of its stage's width: Chunithm's one cell beside its sixteen.
        /// </summary>
        public const float RAIL_WIDTH = 1 / 16f;

        public static readonly Color4 FLOOR_COLOUR = Color4Extensions.FromHex("#060606");

        /// <summary>
        /// The floor's opacity: the background barely shows through, as at Chunithm's darker field brightness settings.
        /// </summary>
        public const float FLOOR_ALPHA = 0.9f;

        public static readonly Color4 RAIL_COLOUR = Color4Extensions.FromHex("#0C2A2A");

        /// <summary>
        /// The rails at the far end, darker than at the line, as the arcade's side bands are.
        /// </summary>
        public static readonly Color4 RAIL_FAR_COLOUR = Color4Extensions.FromHex("#061616");

        public const float RAIL_ALPHA = 0.8f;

        public static readonly Color4 BEAM_COLOUR = Color4.White;

        /// <summary>
        /// A pressed lane's beam at the judgement line. It fades linearly to nothing where it ends (<see cref="KeyBeam"/>).
        /// </summary>
        public const float BEAM_ALPHA = 0.3f;

        /// <summary>
        /// How much brighter the beam is while the lane's hold note is held.
        /// </summary>
        public const float HOLD_BEAM_BOOST = 0.15f;

        /// <summary>
        /// How far up the track a <see cref="KeyBeamLength.Long"/> beam reaches, in track depth.
        /// </summary>
        public const float BEAM_LENGTH = 0.5f;

        /// <summary>
        /// A pressed lane's light just below the judgement line (where the receptor would be), fading to nothing at the
        /// bottom of the screen.
        /// </summary>
        public const float KEY_LIGHT_ALPHA = 0.25f;

        public const double BEAM_FADE_IN = 30;

        /// <summary>
        /// The beam fades out over this long after release, easing out (fast at first).
        /// </summary>
        public const double BEAM_FADE_OUT = 150;

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

        private float farDepth = 1;

        /// <summary>
        /// Where the floor and rails end: the wall's near edge, or the far edge without a wall. The wall covers the lanes
        /// beyond, but not the rails beside them.
        /// </summary>
        public float FarDepth
        {
            get => farDepth;
            set
            {
                if (farDepth == value)
                    return;

                farDepth = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private float floorOpacity = 1;

        /// <summary>
        /// The floor's opacity, times <see cref="FLOOR_ALPHA"/> (<see cref="HighwaySettings.TrackOpacity"/>). The rails and beams
        /// keep theirs.
        /// </summary>
        public float FloorOpacity
        {
            get => floorOpacity;
            set
            {
                value = Math.Clamp(value, 0, 1);

                if (floorOpacity == value)
                    return;

                floorOpacity = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private KeyBeamLength keyBeam;

        /// <summary>
        /// How far up the track the beams reach (<see cref="HighwaySettings.KeyBeam"/>).
        /// </summary>
        public KeyBeamLength KeyBeam
        {
            get => keyBeam;
            set
            {
                if (keyBeam == value)
                    return;

                keyBeam = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        /// <summary>
        /// The depth the beams reach on the current layout and <see cref="FarDepth"/>.
        /// </summary>
        public float BeamDepth => GetBeamDepth(new HighwayProjection(layout, DrawSize), farDepth, keyBeam);

        /// <summary>
        /// The depth a beam of <paramref name="length"/> reaches. <see cref="KeyBeamLength.Long"/> is <see cref="BEAM_LENGTH"/>
        /// in depth. Half and Quarter are that share of the track's height on screen, from the judgement line to
        /// <paramref name="farDepth"/> (the wall or the far edge), so they look the same under every camera (research
        /// <c>game-djmax.md</c> R2). Never longer than Long, as the far end is never wider than the line.
        /// </summary>
        public static float GetBeamDepth(in HighwayProjection projection, float farDepth, KeyBeamLength length)
        {
            float screenShare;

            switch (length)
            {
                case KeyBeamLength.Half:
                    screenShare = 0.5f;
                    break;

                case KeyBeamLength.Quarter:
                    screenShare = 0.25f;
                    break;

                default:
                    return BEAM_LENGTH;
            }

            // Measured as a share of the line-to-far-edge height, so that no screen size is needed.
            float t = screenShare * projection.ShareAt(farDepth);

            return Math.Min(BEAM_LENGTH, projection.DepthAtShare(t));
        }

        /// <summary>
        /// The floor's alpha before the drawable's own: <see cref="FLOOR_ALPHA"/> times <see cref="FloorOpacity"/>.
        /// </summary>
        public float FloorAlpha => FLOOR_ALPHA * floorOpacity;

        /// <summary>
        /// The floor's colour as drawn, given <see cref="FloorAlpha"/> and the drawable's own alpha.
        /// </summary>
        public static Color4 FloorColour(float floorAlpha, float drawAlpha) => FLOOR_COLOUR.Opacity(floorAlpha * drawAlpha);

        private Vector2[] stageLanes = Array.Empty<Vector2>();
        private int stageCount;

        private Vector2[] columnLanes = Array.Empty<Vector2>();
        private float[] beamLevels = Array.Empty<float>();
        private float[] holdLevels = Array.Empty<float>();
        private float[] beamAlphas = Array.Empty<float>();
        private int columnCount;

        /// <summary>
        /// Each column's beam strength at the judgement line this frame (0 when unlit), in playfield column order.
        /// </summary>
        public ReadOnlySpan<float> BeamAlphas => beamAlphas.AsSpan(0, columnCount);

        /// <summary>
        /// Each stage's left and right edge in lane space.
        /// </summary>
        public ReadOnlySpan<Vector2> StageLanes => stageLanes.AsSpan(0, stageCount);

        private IShader shader = null!;

        public HighwayTrack()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        /// <summary>
        /// Sets where the stages and their columns are, in lane space. Keeps the storage when it is big enough.
        /// </summary>
        public void SetLanes(ReadOnlySpan<Vector2> stages, ReadOnlySpan<Vector2> columns)
        {
            if (!stages.SequenceEqual(StageLanes))
            {
                if (stageLanes.Length < stages.Length)
                    stageLanes = new Vector2[stages.Length];

                stages.CopyTo(stageLanes);
                stageCount = stages.Length;
                Invalidate(Invalidation.DrawNode);
            }

            if (columns.Length != columnCount)
            {
                if (columnLanes.Length < columns.Length)
                {
                    columnLanes = new Vector2[columns.Length];
                    beamLevels = new float[columns.Length];
                    holdLevels = new float[columns.Length];
                    beamAlphas = new float[columns.Length];
                }

                columnCount = columns.Length;
                Invalidate(Invalidation.DrawNode);
            }

            if (!columns.SequenceEqual(columnLanes.AsSpan(0, columnCount)))
            {
                columns.CopyTo(columnLanes);
                Invalidate(Invalidation.DrawNode);
            }
        }

        /// <summary>
        /// Lights or dims each column's beam for this frame. Called by the highway once per frame, after
        /// <see cref="SetLanes"/>.
        /// </summary>
        /// <param name="pressed">Whether each column's key is down, in playfield column order.</param>
        /// <param name="holding">Whether each column's hold note is being held.</param>
        /// <param name="active">Whether the track is shown. Hidden, every beam goes dark at once.</param>
        public void UpdateBeams(ReadOnlySpan<bool> pressed, ReadOnlySpan<bool> holding, bool active)
        {
            double elapsed = Time.Elapsed;
            bool changed = false;

            for (int i = 0; i < columnCount; i++)
            {
                bool on = active && i < pressed.Length && pressed[i];
                bool held = on && i < holding.Length && holding[i];

                beamLevels[i] = active ? StepBeam(beamLevels[i], on, elapsed) : 0;
                holdLevels[i] = active ? StepBeam(holdLevels[i], held, elapsed) : 0;

                float alpha = BEAM_ALPHA * beamLevels[i] + HOLD_BEAM_BOOST * holdLevels[i];

                if (alpha != beamAlphas[i])
                {
                    beamAlphas[i] = alpha;
                    changed = true;
                }
            }

            if (changed)
                Invalidate(Invalidation.DrawNode);
        }

        /// <summary>
        /// Moves a beam's strength (0 dark, 1 lit) on by <paramref name="elapsed"/> milliseconds: up linearly over
        /// <see cref="BEAM_FADE_IN"/> while <paramref name="on"/>, otherwise down over <see cref="BEAM_FADE_OUT"/>, easing out
        /// (a strength of (1 - p)² at progress p, so it needs no other state). Rewound time settles it at once.
        /// </summary>
        public static float StepBeam(float level, bool on, double elapsed)
        {
            if (!(elapsed >= 0))
                return on ? 1 : 0;

            if (on)
                return Math.Min(1, level + (float)(elapsed / BEAM_FADE_IN));

            float remaining = MathF.Sqrt(Math.Max(level, 0)) - (float)(elapsed / BEAM_FADE_OUT);
            return remaining > 0 ? remaining * remaining : 0;
        }

        /// <summary>
        /// The widths of stage <paramref name="index"/>'s left and right rails, in lane space: <see cref="RAIL_WIDTH"/> of the
        /// stage's width, but never more than half the gap to a neighbouring stage, so a rail never covers another stage.
        /// </summary>
        public static void GetRailWidths(ReadOnlySpan<Vector2> stages, int index, out float left, out float right)
        {
            Vector2 stage = stages[index];
            float width = RAIL_WIDTH * Math.Max(0, stage.Y - stage.X);

            left = index > 0 ? Math.Clamp((stage.X - stages[index - 1].Y) / 2, 0, width) : width;
            right = index < stages.Length - 1 ? Math.Clamp((stages[index + 1].X - stage.Y) / 2, 0, width) : width;
        }

        protected override DrawNode CreateDrawNode() => new HighwayTrackDrawNode(this);

        private class HighwayTrackDrawNode : DrawNode
        {
            protected new HighwayTrack Source => (HighwayTrack)base.Source;

            private IShader shader = null!;
            private HighwayProjection projection;
            private float alpha;
            private float floorAlpha;
            private float farDepth;
            private float beamDepth;

            private Vector2[] stageLanes = Array.Empty<Vector2>();
            private int stageCount;

            private Vector2[] columnLanes = Array.Empty<Vector2>();
            private float[] beamAlphas = Array.Empty<float>();
            private int columnCount;

            public HighwayTrackDrawNode(HighwayTrack source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                projection = new HighwayProjection(Source.layout, Source.DrawSize);
                alpha = DrawColourInfo.Colour.MaxAlpha;
                floorAlpha = Source.FloorAlpha;
                farDepth = Source.farDepth;
                beamDepth = Source.BeamDepth;

                stageCount = Source.stageCount;
                if (stageLanes.Length < stageCount)
                    stageLanes = new Vector2[Source.stageLanes.Length];
                Array.Copy(Source.stageLanes, stageLanes, stageCount);

                columnCount = Source.columnCount;

                if (columnLanes.Length < columnCount)
                {
                    columnLanes = new Vector2[Source.columnLanes.Length];
                    beamAlphas = new float[Source.columnLanes.Length];
                }

                Array.Copy(Source.columnLanes, columnLanes, columnCount);
                Array.Copy(Source.beamAlphas, beamAlphas, columnCount);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (alpha <= 0)
                    return;

                shader.Bind();

                float belowHeight = Math.Max(0, projection.Size.Y - projection.YAt(0));

                Color4 floor = FloorColour(floorAlpha, alpha);
                Color4 railNear = RAIL_COLOUR.Opacity(RAIL_ALPHA * alpha);
                var railColour = ColourInfo.GradientVertical(RAIL_FAR_COLOUR.Opacity(RAIL_ALPHA * alpha), railNear);

                var stages = stageLanes.AsSpan(0, stageCount);

                for (int i = 0; i < stageCount; i++)
                {
                    float left = stages[i].X;
                    float right = stages[i].Y;

                    GetRailWidths(stages, i, out float leftRail, out float rightRail);

                    // One colour: an exact projected quad needs no subdivision.
                    if (floor.A > 0)
                    {
                        draw(renderer, projection.ToScreen(left, right, 0, farDepth), floor);
                        draw(renderer, TrackDrawing.GetBelowLine(projection, left, right, belowHeight), floor);
                    }

                    // A gradient in depth: subdivided, so it stays straight in perspective.
                    TrackDrawing.DrawTrackRectangle(renderer, renderer.WhitePixel, projection, DrawInfo.Matrix, left - leftRail, left, 0, farDepth, railColour);
                    TrackDrawing.DrawTrackRectangle(renderer, renderer.WhitePixel, projection, DrawInfo.Matrix, right, right + rightRail, 0, farDepth, railColour);
                    draw(renderer, TrackDrawing.GetBelowLine(projection, left - leftRail, left, belowHeight), railNear);
                    draw(renderer, TrackDrawing.GetBelowLine(projection, right, right + rightRail, belowHeight), railNear);
                }

                for (int i = 0; i < columnCount; i++)
                {
                    float beam = beamAlphas[i] * alpha;

                    if (beam <= 0)
                        continue;

                    float left = columnLanes[i].X;
                    float right = columnLanes[i].Y;

                    // Linear in depth, not in screen height: subdivided.
                    TrackDrawing.DrawTrackRectangle(renderer, renderer.WhitePixel, projection, DrawInfo.Matrix, left, right, 0, beamDepth,
                        ColourInfo.GradientVertical(BEAM_COLOUR.Opacity(0), BEAM_COLOUR.Opacity(beam)));

                    // Below the line the track is flat on screen, so a plain vertical gradient is exact.
                    float keyLight = KEY_LIGHT_ALPHA / BEAM_ALPHA * beam;
                    draw(renderer, TrackDrawing.GetBelowLine(projection, left, right, belowHeight),
                        ColourInfo.GradientVertical(BEAM_COLOUR.Opacity(keyLight), BEAM_COLOUR.Opacity(0)));
                }

                shader.Unbind();
            }

            private void draw(IRenderer renderer, Quad quad, ColourInfo colour)
                => renderer.DrawQuad(renderer.WhitePixel, quad * DrawInfo.Matrix, colour);
        }
    }
}
