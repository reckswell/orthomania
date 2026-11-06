// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// Hit lighting at the judgement line (SPEC 5 and 7; research <c>game-chunithm-feedback.md</c> P1, P2 and P6): a burst
    /// lying flat on the track in the lane's projected position for each hit, coloured by result, the lane's piece of the
    /// judgement line lit in the same colour (<see cref="SlotLights"/>, research <c>game-sekai.md</c> K7), and a glow in each
    /// lane while a hold is held. Shown under <see cref="HighwayHitEffectStyle.Highway"/>, where it replaces the skin's hit
    /// explosions (which the warp would stretch near the line).
    /// </summary>
    /// <remarks>
    /// Results arrive through each column's own event, with the same conditions as mania's explosions (see
    /// <see cref="Column"/>'s <c>OnNewResult</c>), so a burst appears exactly when mania would show an explosion, and never on
    /// a miss. Nothing here feeds back into judgement or scoring.
    /// </remarks>
    public partial class HighwayHitEffectLayer : CompositeDrawable
    {
        /// <summary>
        /// The largest a ring may grow, as a share of the track's width at the line, so 4K bursts do not swamp the field.
        /// </summary>
        public const float MAX_RING_SHARE = 0.3f;

        /// <summary>
        /// How much rings and glows are squashed vertically, so they read as lying on the tilted track.
        /// </summary>
        public const float FLATTEN = 0.4f;

        /// <summary>
        /// Bursts made ready per column when gameplay loads, so a dense section does not build them mid-play (notes 75 ms
        /// apart in one lane, 1/16 at 200 BPM, keep up to seven alive; mania readies five explosions per column).
        /// </summary>
        public const int BURSTS_PER_COLUMN = 6;

        /// <summary>
        /// The range a hold's pulse interval is folded into (see <see cref="HoldTickInterval"/>).
        /// </summary>
        public const double MIN_TICK_INTERVAL = 100;

        public const double MAX_TICK_INTERVAL = 300;

        /// <summary>
        /// How long a lane's piece of the judgement line stays lit after a hit, fading linearly: Sekai's slot light
        /// (research <c>game-sekai.md</c> K7), shortened from its 500 ms so a 1/4 stream lights three or four lanes at once,
        /// not the whole line (tune).
        /// </summary>
        public const double SLOT_DURATION = 300;

        /// <summary>
        /// A slot's opacity just after a Perfect or Great (other results scale it by their burst's): strong enough to still
        /// show once the burst over it has faded (tune). Notes cross the line over it.
        /// </summary>
        public const float SLOT_ALPHA = 0.8f;

        /// <summary>
        /// A Perfect's slot: a pale gold, close to white. The burst's gold would vanish into the judgement line's own gold glow
        /// and read as a thicker glow, not as the lane lit.
        /// </summary>
        public static readonly Color4 PERFECT_SLOT_COLOUR = Color4Extensions.FromHex("#FFF4D0");

        /// <summary>
        /// Whether the highway's hit effects are shown this frame (and the skin's explosions hidden).
        /// </summary>
        public bool Active { get; private set; }

        /// <summary>
        /// The bursts on screen, oldest first.
        /// </summary>
        public IReadOnlyList<HighwayHitBurst> Bursts => bursts.Children;

        /// <summary>
        /// One hold glow per column, in playfield column order.
        /// </summary>
        public IReadOnlyList<HighwayHoldGlow> HoldGlows => glows.Children;

        /// <summary>
        /// Each column's slot light this frame, in playfield column order: its last hit's colour at the light's current
        /// strength, transparent when unlit. Drawn by <see cref="HighwaySlotLights"/>.
        /// </summary>
        public ReadOnlySpan<Color4> SlotLights => slotLights;

        /// <summary>
        /// How many hits have lit a slot since this layer loaded.
        /// </summary>
        public int SlotsLit { get; private set; }

        private readonly List<Column> columns = new List<Column>();
        private readonly List<Action<DrawableHitObject, JudgementResult>> handlers = new List<Action<DrawableHitObject, JudgementResult>>();
        private readonly ControlPointInfo controlPoints;
        private readonly DrawablePool<HighwayHitBurst> pool;
        private readonly Container<HighwayHoldGlow> glows;
        private readonly Container<HighwayHitBurst> bursts;

        private readonly double[] slotTimes;
        private readonly Color4[] slotColours;
        private readonly float[] slotPeaks;
        private readonly Color4[] slotLights;

        /// <summary>
        /// Whether the skin's explosions are currently hidden by this layer.
        /// </summary>
        private bool explosionsHidden;

        public HighwayHitEffectLayer(ManiaPlayfield playfield, ControlPointInfo controlPoints)
        {
            this.controlPoints = controlPoints;

            RelativeSizeAxes = Axes.Both;

            // The playfield's stages and columns are built with it, before anything loads.
            foreach (var stage in playfield.Stages)
                columns.AddRange(stage.Columns);

            InternalChildren = new Drawable[]
            {
                pool = new DrawablePool<HighwayHitBurst>(BURSTS_PER_COLUMN * Math.Max(1, columns.Count)),
                glows = new Container<HighwayHoldGlow> { RelativeSizeAxes = Axes.Both },
                bursts = new Container<HighwayHitBurst> { RelativeSizeAxes = Axes.Both },
            };

            for (int i = 0; i < columns.Count; i++)
                glows.Add(new HighwayHoldGlow());

            slotTimes = new double[columns.Count];
            slotColours = new Color4[columns.Count];
            slotPeaks = new float[columns.Count];
            slotLights = new Color4[columns.Count];
            Array.Fill(slotTimes, double.NaN);
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

            if (HitBurstStyle.For(result.Type) is not HitBurstStyle style)
                return;

            // Deterministic (unlike HashCode), so the same play always draws the same sparkles.
            int seed = unchecked(index * 73856093 ^ (int)Math.Round(result.HitObject.StartTime) * 19349663);

            // Applied after Get (no closure per hit): the burst prepares itself on its first update.
            var burst = pool.Get();
            burst.Apply(index, result.Type, style, seed);
            bursts.Add(burst);

            slotTimes[index] = Time.Current;
            slotColours[index] = SlotColour(result.Type, style);
            slotPeaks[index] = SlotPeak(style);
            SlotsLit++;
        }

        /// <summary>
        /// A slot light's colour: the result's burst colour, but <see cref="PERFECT_SLOT_COLOUR"/> for a Perfect.
        /// </summary>
        public static Color4 SlotColour(HitResult result, HitBurstStyle style) => result == HitResult.Perfect ? PERFECT_SLOT_COLOUR : style.Colour;

        /// <summary>
        /// A slot light's opacity on the hit: <see cref="SLOT_ALPHA"/> scaled as the result's burst is (a Meh's is dimmer).
        /// </summary>
        public static float SlotPeak(HitBurstStyle style) => SLOT_ALPHA * style.Alpha;

        /// <summary>
        /// A slot light's strength <paramref name="elapsed"/> milliseconds after its hit: 1 on the hit, falling linearly to 0 at
        /// <see cref="SLOT_DURATION"/>. Nothing before the hit (time rewound past it) or without one (NaN).
        /// </summary>
        public static float SlotLevel(double elapsed) => elapsed >= 0 && elapsed < SLOT_DURATION ? (float)(1 - elapsed / SLOT_DURATION) : 0;

        /// <summary>
        /// Moves the effects to their lanes on the highway and shows or hides them. Called by the highway once per frame,
        /// after the stage has been laid out. Everything is in this layer's space, which is the highway's.
        /// </summary>
        /// <param name="projection">The highway's projection.</param>
        /// <param name="columnLanes">Each column's left and right edge in lane space, in playfield column order.</param>
        /// <param name="active">Whether the highway's effects are wanted (a tilted camera and <see cref="HighwayHitEffectStyle.Highway"/>).</param>
        /// <param name="hideSkinExplosions">Whether the skin's explosions are hidden: with the highway's effects, or while the
        /// skin porter draws the skin's lighting itself.</param>
        public void Place(in HighwayProjection projection, ReadOnlySpan<Vector2> columnLanes, bool active, bool hideSkinExplosions)
        {
            if (Active && !active)
            {
                // Hidden, this layer's children are not updated, so bursts would not expire; return them to the pool now.
                bursts.Clear(false);
                Array.Fill(slotTimes, double.NaN);
            }

            Active = active;
            Alpha = active ? 1 : 0;

            if (explosionsHidden != hideSkinExplosions)
            {
                explosionsHidden = hideSkinExplosions;

                for (int i = 0; i < columns.Count; i++)
                    columns[i].HitObjectArea.Explosions.Alpha = hideSkinExplosions ? 0 : 1;
            }

            if (hideSkinExplosions)
            {
                // Mania keeps adding an explosion on every hit. Hidden, they are never updated, so they would never prepare,
                // expire or go back to their pool; hand them back as they arrive (before they are drawn this frame).
                for (int i = 0; i < columns.Count; i++)
                {
                    var explosions = columns[i].HitObjectArea.Explosions;

                    if (explosions.Count > 0)
                        explosions.Clear(false);
                }
            }

            float trackWidth = projection.WidthAt(0);

            for (int i = 0; i < bursts.Count; i++)
            {
                var burst = bursts[i];

                if (burst.Column < columnLanes.Length)
                    place(burst, projection, columnLanes[burst.Column], trackWidth, burst.Style.RingEnd);
            }

            for (int i = 0; i < slotTimes.Length; i++)
            {
                // Rewound past the hit (a replay seeking back), the lane is dark until the replayed hit lights it again.
                float level = active ? SlotLevel(Time.Current - slotTimes[i]) : 0;
                slotLights[i] = level > 0 ? slotColours[i].Opacity(slotPeaks[i] * level) : Color4.Transparent;
            }

            float phase = HoldTickPhase(Time.Current, controlPoints.TimingPointAt(Time.Current));

            for (int i = 0; i < glows.Count && i < columns.Count; i++)
            {
                var glow = glows[i];

                glow.SetHolding(active && IsHolding(columns[i]), phase);

                if (i < columnLanes.Length)
                    place(glow, projection, columnLanes[i], trackWidth, HighwayHoldGlow.SIZE);
            }
        }

        /// <summary>
        /// Puts an effect on its column's centre at the judgement line, sized to the lane there, so that its largest size
        /// (<paramref name="largestScale"/> lane widths) stays within <see cref="MAX_RING_SHARE"/> of the track.
        /// </summary>
        private static void place(Drawable effect, in HighwayProjection projection, Vector2 lanes, float trackWidth, float largestScale)
        {
            float laneWidth = (lanes.Y - lanes.X) * trackWidth;
            float unit = Math.Min(laneWidth, MAX_RING_SHARE * trackWidth / Math.Max(largestScale, 1));

            if (!(unit > 0) || !float.IsFinite(unit))
                return;

            effect.Position = projection.ToScreen((lanes.X + lanes.Y) / 2, 0);
            effect.Scale = new Vector2(unit / HighwayHitBurst.UNIT);
        }

        /// <summary>
        /// Whether a hold note in <paramref name="column"/> is being held.
        /// </summary>
        public static bool IsHolding(Column column)
        {
            foreach (var entry in column.HitObjectContainer.AliveEntries)
            {
                if (entry.Value is DrawableHoldNote hold && hold.IsHolding.Value)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// How often a held note's glow pulses, at Chunithm's hold tick cadence (research P6): 1/16 notes below 120 BPM, 1/8
        /// notes up to 240 BPM, 1/4 notes above, so it pulses four to eight times a second at most tempos. Gimmick tempos
        /// are folded by halves or doubles into <see cref="MIN_TICK_INTERVAL"/>..<see cref="MAX_TICK_INTERVAL"/>, so the
        /// pulse neither flickers faster than frames nor stops.
        /// </summary>
        public static double HoldTickInterval(double beatLength)
        {
            if (!(beatLength > 0) || !double.IsFinite(beatLength))
                return 125;

            double bpm = 60000 / beatLength;
            double interval = bpm < 120 ? beatLength / 4 : bpm < 240 ? beatLength / 2 : beatLength;

            while (interval < MIN_TICK_INTERVAL)
                interval *= 2;

            while (interval > MAX_TICK_INTERVAL)
                interval /= 2;

            return interval;
        }

        /// <summary>
        /// Where <paramref name="time"/> falls between two hold ticks: 0 on a tick, rising towards 1 just before the next.
        /// Ticks are counted from the timing point's start, so every lane pulses together on the beat grid.
        /// </summary>
        public static float HoldTickPhase(double time, TimingControlPoint timing)
        {
            double phase = (time - timing.Time) / HoldTickInterval(timing.BeatLength) % 1;
            return (float)(phase < 0 ? phase + 1 : phase);
        }

        protected override void Dispose(bool isDisposing)
        {
            // Before the base call, so no result reaches the pool while it is being disposed (as in mania's Column).
            for (int i = 0; i < handlers.Count; i++)
                columns[i].NewResult -= handlers[i];

            handlers.Clear();

            base.Dispose(isDisposing);
        }
    }
}
