// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Testing;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// Shows one stage's judgements upright at their place on the highway (SPEC 1 and 7: judgement text stays in screen
    /// space near the judgement line). Replaces the stage's own judgement display, which would otherwise be drawn tilted
    /// inside the warp.
    /// </summary>
    /// <remarks>
    /// The judgements are mania's own drawables, in a frame that reproduces the stage's flat layout, so every skin places
    /// them as it does in mania. Each frame the frame is moved so that the judgement keeps its height above the judgement
    /// line in screen pixels, sits over the stage's lanes at that height, and is shrunk by the perspective there.
    /// Results arrive through the same event and conditions as mania's own display; nothing here feeds back into
    /// judgement or scoring.
    /// </remarks>
    public partial class HighwayJudgementLayer : CompositeDrawable
    {
        public Stage Stage { get; }

        /// <summary>
        /// The judgement on screen, or null.
        /// </summary>
        public HighwayManiaJudgement? CurrentJudgement => judgements.Count > 0 ? judgements[^1] : null;

        /// <summary>
        /// The area the judgements are laid out in: the stage's flat judgement area, moved and scaled onto the highway.
        /// </summary>
        public Drawable JudgementArea => judgements;

        private readonly Container frame;
        private readonly JudgementContainer<HighwayManiaJudgement> judgements;
        private readonly JudgementPooler<HighwayManiaJudgement> pooler;

        public HighwayJudgementLayer(Stage stage)
        {
            Stage = stage;
            RelativeSizeAxes = Axes.Both;

            var hitWindows = new ManiaHitWindows();

            InternalChildren = new Drawable[]
            {
                pooler = new JudgementPooler<HighwayManiaJudgement>(Enum.GetValues<HitResult>().Where(hitWindows.IsHitResultAllowed)),
                frame = new Container
                {
                    Child = judgements = new JudgementContainer<HighwayManiaJudgement> { RelativeSizeAxes = Axes.Both },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Mania's own display keeps receiving results but is hidden, so each judgement is drawn once, upright.
            foreach (var maniaJudgements in Stage.ChildrenOfType<JudgementContainer<DrawableManiaJudgement>>())
                maniaJudgements.Hide();

            Stage.NewResult += onNewResult;
        }

        private void onNewResult(DrawableHitObject judgedObject, JudgementResult result)
        {
            // The same conditions as mania's Stage.OnNewResult.
            if (!judgedObject.DisplayResult || !Stage.DisplayJudgements.Value)
                return;

            var judgement = pooler.Get(result.Type, j => j.Apply(result, judgedObject));

            if (judgement == null)
                return;

            judgements.Clear(false);
            judgements.Add(judgement);
        }

        /// <summary>
        /// Moves the judgement to its place on the highway. Called by the highway once per frame, after the stage has been
        /// laid out. Everything is in this layer's space, which is the highway's.
        /// </summary>
        /// <param name="projection">The highway's projection.</param>
        /// <param name="flat">Whether the highway is drawn flat (no warp).</param>
        /// <param name="source">The warped area: every stage's lanes, from the top of the scrolling area to the judgement line.</param>
        /// <param name="stretch">How far the stages are stretched sideways inside the warp.</param>
        public void Place(in HighwayProjection projection, bool flat, RectangleF source, float stretch)
        {
            var columns = Stage.Columns;

            if (columns.Length == 0 || source.Width <= 0 || stretch <= 0)
                return;

            var first = columns[0].HitObjectContainer;
            var last = columns[^1].HitObjectContainer;

            if (first.DrawHeight <= 0)
                return;

            Vector2 topLeft = ToLocalSpace(first.ScreenSpaceDrawQuad.TopLeft);
            Vector2 bottomLeft = ToLocalSpace(first.ScreenSpaceDrawQuad.BottomLeft);
            Vector2 topRight = ToLocalSpace(last.ScreenSpaceDrawQuad.TopRight);

            // Mania's uniform scale for the stage (from fitting 1024x768 to the screen), and the stage's flat size in its units.
            float scale = (bottomLeft.Y - topLeft.Y) / first.DrawHeight;

            if (scale <= 0)
                return;

            float stageCentreX = (topLeft.X + topRight.X) / 2;
            float frameWidth = (topRight.X - topLeft.X) / stretch / scale;

            // Columns not laid out yet.
            if (!(frameWidth > 0) || !float.IsFinite(frameWidth))
                return;

            frame.Size = new Vector2(frameWidth, first.DrawHeight);

            // The judgement's centre in the frame; the middle of the hit position when nothing is shown.
            Vector2 anchor = new Vector2(frame.Width / 2, frame.Height);
            var piece = CurrentJudgement?.Piece;

            if (piece?.IsLoaded == true)
            {
                Vector2 pieceCentre = piece.ToSpaceOfOtherDrawable(piece.DrawSize / 2, frame);

                // A piece animating from zero scale has no usable centre for a frame.
                if (float.IsFinite(pieceCentre.X) && float.IsFinite(pieceCentre.Y))
                    anchor = pieceCentre;
            }

            // Offsets from the middle of the hit position, in screen pixels (up and right are positive).
            float above = (frame.Height - anchor.Y) * scale;
            float across = (anchor.X - frame.Width / 2) * scale;

            Vector2 position;
            float size;

            if (flat)
            {
                position = new Vector2(stageCentreX + across, bottomLeft.Y - above);
                size = scale;
            }
            else
            {
                float y = projection.YAt(0) - above;

                // The track's depth at that height sets how far the lanes have narrowed, and so the judgement's size.
                float minDepth = Math.Max(projection.MinDepth, -1) / 2;
                float depth = Math.Clamp(projection.DepthAtY(y), minDepth, 1);
                float lane = (stageCentreX - source.Left + across * stretch) / source.Width;

                position = new Vector2(projection.ToScreen(lane, depth).X, y);
                size = scale * projection.ScaleAt(depth);
            }

            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !(size > 0) || !float.IsFinite(size))
                return;

            // The frame scales about its top-left corner, so this puts the anchor at the position.
            frame.Scale = new Vector2(size);
            frame.Position = position - anchor * size;
        }

        protected override void Dispose(bool isDisposing)
        {
            // Before the base call, so no result reaches the pool while it is being disposed (as in mania's Stage).
            Stage.NewResult -= onNewResult;

            base.Dispose(isDisposing);
        }
    }

    /// <summary>
    /// Mania's judgement drawable, with access to the skin's piece so the highway can find where the skin put it.
    /// </summary>
    public partial class HighwayManiaJudgement : DrawableManiaJudgement
    {
        /// <summary>
        /// The skin's judgement drawable, once loaded.
        /// </summary>
        public Drawable? Piece => JudgementBody?.Drawable;
    }
}
