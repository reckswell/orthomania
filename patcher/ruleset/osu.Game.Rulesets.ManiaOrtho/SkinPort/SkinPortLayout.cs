// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.SkinPort
{
    /// <summary>
    /// The stage as osu!mania lays it out flat for the current skin, measured in the highway's space (pixels, y down) before
    /// the highway tilts it. The skin porter places every ported image from this, so each lands where the skin puts it.
    /// </summary>
    /// <param name="HitY">The skin's hit position.</param>
    /// <param name="ScrollTop">The top of the scrolling area.</param>
    /// <param name="Width">The stage's width as the skin lays it out (before the highway stretches it sideways).</param>
    /// <param name="UnitSize">Pixels per osu!mania unit (a 768-unit tall playfield), vertically.</param>
    /// <param name="Size">The highway's size.</param>
    public readonly record struct FlatStage(float HitY, float ScrollTop, float Width, float UnitSize, Vector2 Size)
    {
        /// <summary>
        /// How far below the hit position the playfield goes: the skin's key area.
        /// </summary>
        public float BelowLine => Math.Max(0, Size.Y - HitY);

        public float ScrollHeight => HitY - ScrollTop;

        public bool IsValid => Size.X > 0 && Size.Y > 0 && ScrollHeight > 0 && UnitSize > 0 && Width > 0;
    }

    /// <summary>
    /// How the skin porter fits a skin's layout to the highway's camera. Pure maths.
    /// </summary>
    public static class SkinPortLayout
    {
        /// <summary>
        /// Stable's screen is 480 units tall; <c>ColumnStart</c> is measured in those units from the left edge.
        /// </summary>
        public const float STABLE_SCREEN_HEIGHT = 480;

        /// <summary>
        /// Whether <paramref name="keyArea"/> lifts the judgement line to the skin's hit position.
        /// </summary>
        public static bool LiftsLine(SkinKeyArea keyArea) => keyArea == SkinKeyArea.Panel || keyArea == SkinKeyArea.Floor;

        /// <summary>
        /// The camera's <paramref name="layout"/>, changed for the skin: the judgement line lifted to the skin's hit position
        /// when <paramref name="keyArea"/> asks for it and the skin's line is the higher, the track moved (and sized) as
        /// <paramref name="fit"/> asks, and a track the skin puts to one side pulled and leaning towards the screen's centre as
        /// <paramref name="pull"/> and <paramref name="lean"/> ask.
        /// </summary>
        /// <param name="layout">The camera's layout.</param>
        /// <param name="keyArea">Where the key area goes.</param>
        /// <param name="fit">How the track is fitted across the screen.</param>
        /// <param name="stage">The skin's flat stage.</param>
        /// <param name="columnStart">The skin's <c>ColumnStart</c> (stable units), or null when it centres its stage.</param>
        /// <param name="pull">How far a stage put to one side moves towards the screen's centre (0 to 1). 0, the skin's x, unless given.</param>
        /// <param name="lean">
        /// How far its far end leans towards the screen's centre (0 to 1): where its lanes converge. 0, upright, unless given; the
        /// setting's default is <see cref="HighwaySettings.DEFAULT_SIDE_STAGE_LEAN"/>.
        /// </param>
        public static HighwayLayout Adjust(HighwayLayout layout, SkinKeyArea keyArea, SkinStageFit fit, in FlatStage stage, float? columnStart,
                                           float pull = 0, float lean = 0)
        {
            if (!stage.IsValid)
                return layout;

            // Never lower than the camera's line, never higher than the line height setting allows.
            if (LiftsLine(keyArea))
                layout = layout with { JudgementY = Math.Min(layout.JudgementY, Math.Max(1 - HighwaySettings.MAX_JUDGEMENT_LINE_HEIGHT, stage.HitY / stage.Size.Y)) };

            if (fit == SkinStageFit.Camera)
                return layout;

            if (fit == SkinStageFit.SkinLayout)
                layout = layout with { NearWidth = Math.Clamp(stage.Width / stage.Size.X, HighwayLayout.MIN_NEAR_WIDTH, 1) };

            if (columnStart is float start)
            {
                // Below the line the track keeps widening along its lanes unless the key area stands upright, so the bottom
                // row is the widest one drawn.
                float widening = keyArea == SkinKeyArea.Panel ? 1 : WideningToBottom(new HighwayProjection(layout, stage.Size));

                float centre = StableCentre(stage, start);
                centre += (0.5f - centre) * Math.Clamp(pull, 0, 1);

                layout = CentreOn(layout, centre, stage.Width / stage.Size.X, widening);
                layout = layout.LeanTowards(layout.CentreX + (0.5f - layout.CentreX) * Math.Clamp(lean, 0, 1));
            }

            return layout;
        }

        /// <summary>
        /// Where stable centres a stage whose left edge is at <paramref name="columnStart"/> (stable units), as a fraction
        /// of the screen's width: the left edge, then the skin's own width.
        /// </summary>
        public static float StableCentre(in FlatStage stage, float columnStart)
            => (columnStart * stage.Size.Y / STABLE_SCREEN_HEIGHT + stage.Width / 2) / stage.Size.X;

        /// <summary>
        /// <paramref name="layout"/> centred exactly on <paramref name="centre"/> (a fraction of the screen's width), so a
        /// stage the skin puts to one side keeps its lane centres where the skin has them. A track too wide to fit around that
        /// centre is narrowed to fit, never moved (research/topic-off-centre-fields.md O3), but never below
        /// <paramref name="minWidth"/>: the skin's own stage, which fits wherever the skin puts it.
        /// </summary>
        /// <param name="layout">The layout to centre.</param>
        /// <param name="centre">Where the track's centre goes.</param>
        /// <param name="minWidth">The narrowest the track may become, a fraction of the screen's width.</param>
        /// <param name="widening">How much wider than at the judgement line the widest row drawn is.</param>
        public static HighwayLayout CentreOn(HighwayLayout layout, float centre, float minWidth = 0, float widening = 1)
        {
            if (!float.IsFinite(centre))
                return layout;

            centre = Math.Clamp(centre, HighwayLayout.MIN_NEAR_WIDTH / 2, 1 - HighwayLayout.MIN_NEAR_WIDTH / 2);

            float room = Math.Min(centre, 1 - centre) / Math.Max(1, float.IsFinite(widening) ? widening : 1);
            float half = Math.Min(Math.Min(layout.NearWidth, 1) / 2, Math.Max(room, minWidth / 2));

            return layout with { CentreX = centre, NearWidth = 2 * half };
        }

        /// <summary>
        /// How much wider the track is at the bottom of the screen than at the judgement line when its lanes run on below the
        /// line (1 for parallel lanes; at most <see cref="MAX_WIDENING"/>).
        /// </summary>
        public static float WideningToBottom(in HighwayProjection projection)
        {
            float depth = projection.DepthAtY(projection.Size.Y);

            if (!(depth < 0) || depth <= projection.MinDepth)
                return depth < 0 ? MAX_WIDENING : 1;

            return Math.Clamp(projection.ScaleAt(depth), 1, MAX_WIDENING);
        }

        /// <summary>
        /// The most <see cref="WideningToBottom"/> counts, for a camera whose lanes would reach the camera plane on screen.
        /// </summary>
        public const float MAX_WIDENING = 2;

        /// <summary>
        /// The depth a ported note spans when it keeps the skin's proportions: as tall on screen at the judgement line, for the
        /// lane's width there, as the skin draws it for the column's width.
        /// </summary>
        /// <param name="projection">The highway's projection.</param>
        /// <param name="laneWidth">The lane's width in lane space.</param>
        /// <param name="aspect">The note's height over its width, as the skin draws it.</param>
        /// <param name="maxDepth">The deepest a note may be.</param>
        public static float NoteDepth(in HighwayProjection projection, float laneWidth, float aspect, float maxDepth)
        {
            if (!(aspect > 0) || !(laneWidth > 0))
                return 0;

            float lineY = projection.YAt(0);
            float height = aspect * laneWidth * projection.WidthAt(0);
            float depth = projection.DepthAtY(lineY - height);

            // An image taller than the track can show under a strong perspective reaches past the vanishing point, where the
            // inverse turns negative: as deep as allowed.
            return float.IsFinite(depth) && depth > 0 ? Math.Min(depth, maxDepth) : maxDepth;
        }
    }

    /// <summary>
    /// Where the skin porter draws a point of the skin's flat stage on the highway this frame. Rows above the hit position
    /// lie on the track at the depth the warp gives them (so they line up with the notes and bar lines); the key area below
    /// it goes as <see cref="KeyArea"/> says.
    /// </summary>
    public readonly struct PortPlacement
    {
        public readonly HighwayProjection Projection;
        public readonly FlatStage Stage;
        public readonly SkinKeyArea KeyArea;

        /// <summary>
        /// The depth of the top of the scrolling area (<see cref="HighwayScroll.ScrollLength"/>).
        /// </summary>
        public readonly float ScrollLength;

        /// <summary>
        /// Where notes appear: the wall's near edge, or the far edge without a wall.
        /// </summary>
        public readonly float AppearDepth;

        /// <summary>
        /// The judgement line on screen.
        /// </summary>
        public readonly float LineY;

        /// <summary>
        /// How much the key area is squeezed below the line (1 for none).
        /// </summary>
        public readonly float BelowScale;

        public PortPlacement(in HighwayProjection projection, in FlatStage stage, SkinKeyArea keyArea, float scrollLength, float appearDepth)
        {
            Projection = projection;
            Stage = stage;
            KeyArea = keyArea;
            ScrollLength = scrollLength;
            AppearDepth = appearDepth;
            LineY = projection.YAt(0);

            float available = Math.Max(0, projection.Size.Y - LineY);
            BelowScale = keyArea == SkinKeyArea.Squeezed && stage.BelowLine > available && stage.BelowLine > 0 ? available / stage.BelowLine : 1;
        }

        /// <summary>
        /// Whether the key area keeps upright sides below the line (a panel) instead of following the lanes outwards.
        /// </summary>
        public bool VerticalBelow => KeyArea == SkinKeyArea.Panel;

        public bool ShowKeys => KeyArea != SkinKeyArea.Hidden;

        /// <summary>
        /// The track depth of the flat stage's row at <paramref name="flatY"/>, above the line.
        /// </summary>
        public float DepthOfFlatY(float flatY) => Stage.ScrollHeight > 0 ? (Stage.HitY - flatY) / Stage.ScrollHeight * ScrollLength : 0;

        /// <summary>
        /// Where the flat stage's row at <paramref name="flatY"/>, below the hit position, is drawn.
        /// </summary>
        public float ScreenYBelow(float flatY) => LineY + (flatY - Stage.HitY) * BelowScale;

        /// <summary>
        /// Where the flat stage's row at <paramref name="flatY"/>, above the hit position, is drawn when it stands upright
        /// (receptors and key images reaching above the line): at its flat height above the line.
        /// </summary>
        public float ScreenYUprightAbove(float flatY) => LineY - (Stage.HitY - flatY);

        /// <summary>
        /// The screen x of <paramref name="lane"/> at screen height <paramref name="y"/> in the key area.
        /// </summary>
        public float XBelow(float lane, float y) => Projection.XAt(lane, VerticalBelow ? LineY : y);

        /// <summary>
        /// Lane space per flat-stage pixel across: lanes are fractions of the stage's width.
        /// </summary>
        public float LanePerPixel => Stage.Width > 0 ? 1 / Stage.Width : 0;

        /// <summary>
        /// How much wider the track is at the line than the skin's flat stage: ported images that belong to a lane (lighting,
        /// stage bottom) grow with it.
        /// </summary>
        public float WidthScale => Stage.Width > 0 ? Projection.WidthAt(0) / Stage.Width : 1;
    }
}
