// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// How a hit burst looks for one result (research <c>game-chunithm-feedback.md</c> P1 and P2). Perfect and Great must be
    /// easy to tell apart, since with judgement text for them hidden the burst is the only per-note signal.
    /// </summary>
    /// <param name="Colour">The ring's colour.</param>
    /// <param name="Alpha">The burst's peak opacity.</param>
    /// <param name="RingStart">The ring's diameter when it appears, in lane widths.</param>
    /// <param name="RingEnd">The ring's diameter when it has faded, in lane widths.</param>
    /// <param name="Core">Whether a flash fills the ring's centre.</param>
    /// <param name="Sparkles">How many sparkles rise from the burst.</param>
    public readonly record struct HitBurstStyle(Color4 Colour, float Alpha, float RingStart, float RingEnd, bool Core, int Sparkles)
    {
        public static readonly Color4 PERFECT_COLOUR = Color4Extensions.FromHex("#FFD54A");
        public static readonly Color4 GREAT_COLOUR = Color4Extensions.FromHex("#FF7A3D");
        public static readonly Color4 GOOD_COLOUR = Color4Extensions.FromHex("#2CD644");
        public static readonly Color4 MEH_COLOUR = Color4Extensions.FromHex("#7FAF7F");

        /// <summary>
        /// The burst for <paramref name="result"/>, or null for none (a miss, like mania, which shows no explosion for one).
        /// </summary>
        public static HitBurstStyle? For(HitResult result)
        {
            switch (result)
            {
                case HitResult.Perfect:
                    return new HitBurstStyle(PERFECT_COLOUR, 1, 0.6f, 1.3f, true, 8);

                case HitResult.Great:
                    return new HitBurstStyle(GREAT_COLOUR, 1, 0.6f, 1.3f, true, 4);

                case HitResult.Good:
                case HitResult.Ok:
                    // A small white-green ring.
                    return new HitBurstStyle(Interpolation.ValueAt(0.35f, GOOD_COLOUR, Color4.White, 0, 1), 0.9f, 0.5f, 1, false, 0);

                case HitResult.Meh:
                    return new HitBurstStyle(MEH_COLOUR, 0.6f, 0.5f, 1, false, 0);

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// One hit's burst: a ring lying on the track that grows and fades, a flash in its centre, and sparkles rising upright
    /// from it. Laid out in units of <see cref="UNIT"/> per lane width; <see cref="HighwayHitEffectLayer"/> places and scales it.
    /// </summary>
    public partial class HighwayHitBurst : PoolableDrawable
    {
        /// <summary>
        /// One lane width, in the burst's own units.
        /// </summary>
        public const float UNIT = 100;

        public const double RING_DURATION = 350;

        public const double CORE_DURATION = 120;

        /// <summary>
        /// How long a burst lives. Mania's explosions last 200 ms; a longer ring would pile up in dense streams.
        /// </summary>
        public const double LIFETIME = 500;

        public const int MAX_SPARKLES = 8;

        /// <summary>
        /// Also when rewound before it began (a replay seeking back), so the replayed hit does not add a second burst over it.
        /// </summary>
        public override bool RemoveWhenNotAlive => true;

        private const float ring_thickness = 9;
        private const float sparkle_size = 4;

        /// <summary>
        /// The column the burst belongs to, in playfield column order.
        /// </summary>
        public int Column { get; private set; }

        public HitResult Result { get; private set; }

        public HitBurstStyle Style { get; private set; }

        /// <summary>
        /// The ring lying on the track.
        /// </summary>
        public Drawable Ring => ring;

        private readonly Container ring;
        private readonly Circle core;
        private readonly Box[] sparkles = new Box[MAX_SPARKLES];

        private int seed;

        public HighwayHitBurst()
        {
            Origin = Anchor.Centre;
            Size = new Vector2(UNIT);
            Blending = BlendingParameters.Additive;

            var sparkleContainer = new Container { RelativeSizeAxes = Axes.Both };

            for (int i = 0; i < MAX_SPARKLES; i++)
            {
                // Small diamonds: plain rotated boxes batch well, unlike masked circles.
                sparkleContainer.Add(sparkles[i] = new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(sparkle_size),
                    Rotation = 45,
                    Alpha = 0,
                });
            }

            InternalChildren = new Drawable[]
            {
                // Squashed so the ring and flash lie on the track; the sparkles stay upright.
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Scale = new Vector2(1, HighwayHitEffectLayer.FLATTEN),
                    Children = new Drawable[]
                    {
                        core = new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(0.8f * UNIT),
                            Alpha = 0,
                        },
                        ring = new CircularContainer
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(UNIT),
                            Masking = true,
                            BorderThickness = ring_thickness,
                            // Not additive: a freshly hit lane is lit by the skin's press beam, which would wash an additive
                            // ring out to white just when it matters.
                            Blending = BlendingParameters.Mixture,
                            Alpha = 0,
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Alpha = 0,
                                AlwaysPresent = true,
                            },
                        },
                    },
                },
                sparkleContainer,
            };
        }

        /// <param name="column">The column hit, in playfield column order.</param>
        /// <param name="result">The result.</param>
        /// <param name="style">How the burst looks.</param>
        /// <param name="seed">Spreads the sparkles; the same seed always gives the same sparkles.</param>
        public void Apply(int column, HitResult result, HitBurstStyle style, int seed)
        {
            Column = column;
            Result = result;
            Style = style;
            this.seed = seed;
        }

        protected override void PrepareForUse()
        {
            base.PrepareForUse();

            LifetimeStart = Time.Current;

            ClearTransforms(true);
            Alpha = 1;

            ring.BorderColour = Style.Colour;
            ring.Scale = new Vector2(Style.RingStart);
            ring.Alpha = Style.Alpha;
            ring.ScaleTo(Style.RingEnd, RING_DURATION, Easing.OutQuint);
            ring.FadeOut(RING_DURATION);

            core.Colour = Interpolation.ValueAt(0.5f, Style.Colour, Color4.White, 0, 1);
            core.Alpha = 0;

            if (Style.Core)
                core.FadeTo(Style.Alpha * 0.8f, CORE_DURATION / 4).Then().FadeOut(CORE_DURATION * 3 / 4);

            uint random = (uint)seed;

            for (int i = 0; i < MAX_SPARKLES; i++)
            {
                var sparkle = sparkles[i];

                if (i >= Style.Sparkles)
                {
                    sparkle.Alpha = 0;
                    continue;
                }

                float across = next(ref random) - 0.5f;
                float rise = 0.45f + 0.25f * next(ref random);

                sparkle.Colour = Interpolation.ValueAt(0.4f, Style.Colour, Color4.White, 0, 1);
                sparkle.Position = new Vector2(across * 0.9f * UNIT, 0);
                sparkle.Alpha = Style.Alpha;
                sparkle.MoveToY(-rise * UNIT, LIFETIME, Easing.OutQuad);
                sparkle.FadeOut(LIFETIME, Easing.InQuad);
            }

            this.Delay(LIFETIME).Expire();
        }

        /// <summary>
        /// A uniform number in [0, 1) from a small xorshift generator.
        /// </summary>
        private static float next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;

            // xorshift sticks at zero.
            if (state == 0)
                state = 0x9E3779B9;

            return (state & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>
    /// A lane's glow at the judgement line while a hold is held: a soft light lying on the track, and a ring that pulses
    /// outwards on each hold tick (research P6). Laid out like <see cref="HighwayHitBurst"/>.
    /// </summary>
    public partial class HighwayHoldGlow : CompositeDrawable
    {
        /// <summary>
        /// The glow's largest size, in lane widths.
        /// </summary>
        public const float SIZE = 1.2f;

        public static readonly Color4 COLOUR = Color4Extensions.FromHex("#FFE7A0");

        /// <summary>
        /// Whether the lane is lit by a held note.
        /// </summary>
        public bool Holding { get; private set; }

        private readonly Circle light;
        private readonly Container ring;

        public HighwayHoldGlow()
        {
            Origin = Anchor.Centre;
            Size = new Vector2(HighwayHitBurst.UNIT);
            Blending = BlendingParameters.Additive;
            Alpha = 0;

            InternalChild = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Scale = new Vector2(1, HighwayHitEffectLayer.FLATTEN),
                Children = new Drawable[]
                {
                    light = new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(0.9f * HighwayHitBurst.UNIT),
                        Colour = COLOUR,
                        Alpha = 0.3f,
                    },
                    ring = new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(HighwayHitBurst.UNIT),
                        Masking = true,
                        BorderThickness = 6,
                        BorderColour = COLOUR,
                        // Not additive, so it shows over the lane the held key lights (see HighwayHitBurst).
                        Blending = BlendingParameters.Mixture,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            AlwaysPresent = true,
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Lights or dims the glow, and moves the pulse to <paramref name="tickPhase"/> (0 on a hold tick, rising to 1 just
        /// before the next).
        /// </summary>
        public void SetHolding(bool holding, float tickPhase)
        {
            if (holding != Holding)
            {
                Holding = holding;

                // Legacy skins' hold lighting fades in over 80 ms and out over 120 ms.
                if (holding)
                    this.FadeIn(80);
                else
                    this.FadeOut(120);
            }

            if (!holding)
                return;

            // Grows out from the light and fades on each tick.
            float eased = 1 - (1 - tickPhase) * (1 - tickPhase);
            ring.Scale = new Vector2(0.75f + (SIZE - 0.75f) * eased);
            ring.Alpha = 0.9f * (1 - tickPhase);
            light.Alpha = 0.2f + 0.15f * (1 - tickPhase);
        }
    }
}
