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
using osu.Framework.Layout;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.Mods
{
    /// <summary>
    /// osu!mania's Flashlight, with its lit band drawn where the highway draws the stage rows mania lights: notes show for
    /// exactly the same part of their approach as in mania, at whatever height the camera puts that part on screen.
    /// </summary>
    /// <remarks>
    /// Only the drawing changes. Everything that scores (acronym, multiplier, rank, incompatibilities) is mania's, by
    /// inheritance; the ruleset lists this in place of <see cref="ManiaModFlashlight"/>.
    /// </remarks>
    public partial class ManiaOrthoModFlashlight : ManiaModFlashlight
    {
        private DrawableRuleset<ManiaHitObject>? applyingTo;

        public override void ApplyToDrawableRuleset(DrawableRuleset<ManiaHitObject> drawableRuleset)
        {
            // The base creates the flashlight within this call. The mod outlives gameplay (the score keeps it), so it must not
            // keep the drawable ruleset alive afterwards.
            applyingTo = drawableRuleset;
            base.ApplyToDrawableRuleset(drawableRuleset);
            applyingTo = null;
        }

        // The playfield adjustment container exists by now (the base adds a tracker to it).
        protected override Flashlight CreateFlashlight() => new HighwayFlashlight(this, applyingTo?.Playfield, (applyingTo as DrawableManiaOrthoRuleset)?.Highway);

        /// <summary>
        /// Mania's rectangular flashlight (a full-width lit band at the playfield's vertical centre, soft at its edges), with
        /// each row of the band moved to where the highway draws that row of the flat stage.
        /// </summary>
        public partial class HighwayFlashlight : Flashlight
        {
            /// <summary>
            /// Strips each soft edge is drawn in. Mania's soft edge is a smoothstep over 10% of the band's half height.
            /// </summary>
            public const int EDGE_STRIPS = 6;

            private readonly LayoutValue flashlightProperties = new LayoutValue(Invalidation.DrawSize);

            private readonly HighwayPlayfieldAdjustmentContainer? highway;

            /// <summary>
            /// Mania measures the band in the playfield's units (it scales with the stage, e.g. in portrait), not in this
            /// drawable's.
            /// </summary>
            private readonly Drawable? playfield;

            /// <summary>
            /// The band's soft edges as drawn, top to bottom, in this drawable's space: <see cref="EDGE_STRIPS"/> + 1 points
            /// for the top edge (dark to lit), then as many for the bottom edge (lit to dark).
            /// </summary>
            private readonly float[] edgeY = new float[2 * (EDGE_STRIPS + 1)];

            /// <summary>
            /// How dark each point of <see cref="edgeY"/> is, from 0 (lit) to 1.
            /// </summary>
            private readonly float[] edgeDarkness = new float[2 * (EDGE_STRIPS + 1)];

            private IShader textureShader = null!;

            public HighwayFlashlight(ManiaModFlashlight modFlashlight, Drawable? playfield, HighwayPlayfieldAdjustmentContainer? highway)
                : base(modFlashlight)
            {
                this.playfield = playfield;
                this.highway = highway;

                // As mania's flashlight.
                FlashlightSize = new Vector2(DrawWidth, GetSize());
                AddLayout(flashlightProperties);
            }

            [BackgroundDependencyLoader]
            private void load(ShaderManager shaders)
            {
                textureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
            }

            /// <summary>
            /// The y (this drawable's space) of edge point <paramref name="index"/>, see <see cref="EdgePointCount"/>.
            /// </summary>
            public float GetEdgeY(int index) => edgeY[index];

            public float GetEdgeDarkness(int index) => edgeDarkness[index];

            public int EdgePointCount => edgeY.Length;

            /// <summary>
            /// The lit band's centre and half height in this drawable's space, before the highway moves it (mania's band).
            /// </summary>
            public Vector2 FlatBand => new Vector2(FlashlightPosition.Y, FlashlightSize.Y * playfieldScale);

            /// <summary>
            /// Playfield units per unit of this drawable, vertically: mania's draw node scales the band by the playfield's
            /// scale on screen.
            /// </summary>
            private float playfieldScale
            {
                get
                {
                    float ownScale = DrawInfo.Matrix.ExtractScale().Y;
                    return playfield == null || ownScale <= 0 ? 1 : playfield.DrawInfo.Matrix.ExtractScale().Y / ownScale;
                }
            }

            protected override void Update()
            {
                base.Update();

                if (!flashlightProperties.IsValid)
                {
                    FlashlightSize = new Vector2(DrawWidth, FlashlightSize.Y);
                    FlashlightPosition = DrawPosition + DrawSize / 2;
                    flashlightProperties.Validate();
                }

                updateEdges();
            }

            protected override void UpdateFlashlightSize(float size)
            {
                this.TransformTo(nameof(FlashlightSize), new Vector2(DrawWidth, size), FLASHLIGHT_FADE_DURATION);
            }

            protected override string FragmentShader => "RectangularFlashlight";

            private void updateEdges()
            {
                var band = FlatBand;
                float centre = band.X;
                float halfHeight = band.Y;
                float softHeight = halfHeight * (FlashlightSmoothness - 1);

                for (int i = 0; i <= EDGE_STRIPS; i++)
                {
                    float t = (float)i / EDGE_STRIPS;

                    // Mania's shader: darkness = smoothstep(halfHeight, halfHeight * smoothness, |y - centre|).
                    float darkness = 1 - (3 * t * t - 2 * t * t * t);

                    // Top edge from dark (t = 0) to lit (t = 1), bottom edge mirrored.
                    setEdge(i, centre - halfHeight - softHeight * (1 - t), darkness);
                    setEdge(2 * EDGE_STRIPS + 1 - i, centre + halfHeight + softHeight * (1 - t), darkness);
                }
            }

            private void setEdge(int index, float flatY, float darkness)
            {
                float y = toDrawn(flatY);

                if (edgeY[index] == y && edgeDarkness[index] == darkness)
                    return;

                edgeY[index] = y;
                edgeDarkness[index] = darkness;
                Invalidate(Invalidation.DrawNode);
            }

            /// <summary>
            /// Where the highway draws the flat stage row that is at <paramref name="flatY"/> in this drawable's space.
            /// </summary>
            private float toDrawn(float flatY)
            {
                if (highway == null || !highway.IsLoaded)
                    return flatY;

                float x = DrawWidth / 2;
                float highwayY = highway.ToLocalSpace(ToScreenSpace(new Vector2(x, flatY))).Y;
                float drawnY = highway.DrawnYOfFlatY(highwayY);

                return ToLocalSpace(highway.ToScreenSpace(new Vector2(highway.DrawWidth / 2, drawnY))).Y;
            }

            protected override DrawNode CreateDrawNode() => new HighwayFlashlightDrawNode(this);

            private class HighwayFlashlightDrawNode : DrawNode
            {
                protected new HighwayFlashlight Source => (HighwayFlashlight)base.Source;

                private IShader shader = null!;
                private readonly float[] edgeY = new float[2 * (EDGE_STRIPS + 1)];
                private readonly float[] edgeDarkness = new float[2 * (EDGE_STRIPS + 1)];
                private Vector2 size;
                private float dim;
                private Color4 colour;

                public HighwayFlashlightDrawNode(HighwayFlashlight source)
                    : base(source)
                {
                }

                public override void ApplyState()
                {
                    base.ApplyState();

                    shader = Source.textureShader;
                    size = Source.DrawSize;
                    dim = Source.FlashlightDim;
                    colour = DrawColourInfo.Colour.TopLeft.SRGB;

                    Array.Copy(Source.edgeY, edgeY, edgeY.Length);
                    Array.Copy(Source.edgeDarkness, edgeDarkness, edgeDarkness.Length);
                }

                protected override void Draw(IRenderer renderer)
                {
                    base.Draw(renderer);

                    if (colour.A <= 0)
                        return;

                    shader.Bind();

                    int last = edgeY.Length - 1;

                    // Dark above the band, the top soft edge, the lit band (dimmed, as mania's shader does), the bottom soft
                    // edge, dark below.
                    draw(renderer, 0, edgeY[0], 1, 1);

                    for (int i = 0; i < EDGE_STRIPS; i++)
                        draw(renderer, edgeY[i], edgeY[i + 1], edgeDarkness[i], edgeDarkness[i + 1]);

                    draw(renderer, edgeY[EDGE_STRIPS], edgeY[EDGE_STRIPS + 1], 0, 0);

                    for (int i = EDGE_STRIPS + 1; i < last; i++)
                        draw(renderer, edgeY[i], edgeY[i + 1], edgeDarkness[i], edgeDarkness[i + 1]);

                    draw(renderer, edgeY[last], size.Y, 1, 1);

                    shader.Unbind();
                }

                private void draw(IRenderer renderer, float top, float bottom, float topDarkness, float bottomDarkness)
                {
                    top = Math.Max(top, 0);
                    bottom = Math.Min(bottom, size.Y);

                    if (bottom <= top)
                        return;

                    float topAlpha = topDarkness + (1 - topDarkness) * dim;
                    float bottomAlpha = bottomDarkness + (1 - bottomDarkness) * dim;

                    if (topAlpha <= 0 && bottomAlpha <= 0)
                        return;

                    var quad = new Quad(0, top, size.X, bottom - top) * DrawInfo.Matrix;

                    renderer.DrawQuad(renderer.WhitePixel, quad,
                        ColourInfo.GradientVertical(colour.Opacity(colour.A * topAlpha), colour.Opacity(colour.A * bottomAlpha)));
                }
            }
        }
    }
}
