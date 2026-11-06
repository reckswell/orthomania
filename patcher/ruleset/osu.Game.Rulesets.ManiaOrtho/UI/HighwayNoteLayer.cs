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
using osu.Framework.Utils;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.SkinPort;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI.Scrolling.Algorithms;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// Draws mania's notes natively on the track (SPEC 6, research/game-chunithm.md P5): flat bars with a fixed thickness in
    /// track units, so they keep Chunithm's proportions at every depth and stay sharp, instead of mania's skin pieces warped
    /// with the stage.
    /// </summary>
    /// <remarks>
    /// Mania's drawable hit objects stay in the stage and do all the judging; only their drawing is taken over (the column
    /// hit object containers are proxied away, see <see cref="HighwayPlayfieldAdjustmentContainer"/>). Every frame this reads
    /// where they are from the hit objects' times and mania's own scroll algorithm, and their state (alpha, colour, held,
    /// broken) from the drawables.
    /// </remarks>
    public partial class HighwayNoteLayer : Drawable
    {
        /// <summary>
        /// The default note thickness in track depth: Chunithm's TAP is 2.55% of the visible track (research P5). Each camera
        /// preset sets its own (<see cref="HighwaySettings.NoteThickness"/>).
        /// </summary>
        public const float NOTE_THICKNESS = HighwaySettings.DEFAULT_NOTE_THICKNESS;

        /// <summary>
        /// Note (and hold head) thickness in track depth. A hold's tail cap is half of it.
        /// </summary>
        public float NoteThickness { get; set; } = NOTE_THICKNESS;

        /// <summary>
        /// Where no lane line is drawn beside it, the gap on each side of a note, as a fraction of its lane (a 98% wide note),
        /// but never under <see cref="MIN_GAP_PIXELS"/>. Where lines are drawn, notes meet them instead.
        /// </summary>
        public const float NOTE_GAP = 0.01f;

        /// <summary>
        /// The least gap on each side of a note, in screen pixels: far away, and on narrow lanes at high key counts, 1% of a lane
        /// is under a pixel and neighbouring notes merge into one bar (research/topic-wide-keys.md W3).
        /// </summary>
        public const float MIN_GAP_PIXELS = 1;

        /// <summary>
        /// The gap never takes more than this share of a lane from each side, so a note on a very narrow lane still shows.
        /// </summary>
        private const float max_gap = 0.25f;

        /// <summary>
        /// Hold bodies are narrower than their heads, as a fraction of the note width.
        /// </summary>
        public const float BODY_WIDTH = 0.8f;

        /// <summary>
        /// The bright rim at a note's near edge, as a fraction of its thickness.
        /// </summary>
        private const float rim_fraction = 0.2f;

        private static readonly Color4 broken_colour = new Color4(0.45f, 0.45f, 0.45f, 1);

        /// <summary>
        /// The built-in colours, after Alpha's own skin (research/baseline-alpha.md): white outer lanes, pink inner lanes,
        /// counted inwards from both edges, and amber for mania's special (centre) column.
        /// </summary>
        public static readonly Color4 OUTER_COLOUR = Color4.White;

        public static readonly Color4 INNER_COLOUR = Color4Extensions.FromHex("#ff8290");

        public static readonly Color4 SPECIAL_COLOUR = Color4Extensions.FromHex("#ffc528");

        /// <summary>
        /// Use each note's timing-based (snap) colour, as mania's "Timing-based note colouring" setting does.
        /// </summary>
        public bool SnapColours { get; set; }

        /// <summary>
        /// Hold bodies' opacity, times their state's (<see cref="HighwaySettings.HoldOpacity"/>). Heads and tails keep theirs.
        /// </summary>
        public float HoldOpacity { get; set; } = 1;

        private HighwayLayout layout = new HighwayLayout();

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

        private NoteQuad[] quads = new NoteQuad[256];

        /// <summary>
        /// The number of quads gathered this frame.
        /// </summary>
        public int Count { get; private set; }

        public NoteQuad GetQuad(int index) => quads[index];

        private IShader shader = null!;

        public HighwayNoteLayer()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders)
        {
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        /// <summary>
        /// The cover of the column being gathered.
        /// </summary>
        private DepthCover cover;

        /// <summary>
        /// The far fade over every column (<see cref="DepthCover.FarFade"/>), on top of each column's own cover. Cosmetic.
        /// </summary>
        public DepthCover FarFade { get; set; }

        /// <summary>
        /// The camera this frame's notes are gathered for, and how many screen pixels one of this layer's units is.
        /// </summary>
        private HighwayProjection projection;

        private float pixelsPerUnit = 1;

        /// <summary>
        /// When set, notes are drawn with the skin's own images (<see cref="HighwayNoteStyle.Ported"/>) instead of plain bars.
        /// </summary>
        public PortedNoteShapes? Ported { get; set; }

        /// <summary>
        /// The lane lines beside the column being gathered, when they are drawn.
        /// </summary>
        private NoteSides? sides;

        /// <summary>
        /// Collects every alive note of <paramref name="playfield"/>, in the lanes <paramref name="columnLanes"/> gives for each
        /// column (in playfield column order, lane space), at the depths <paramref name="scroll"/> maps their times to, under
        /// each column's cover in <paramref name="columnCovers"/> (Hidden, Fade In, Cover; empty or <see cref="DepthCover.None"/>
        /// for none). Plain bars reach the <paramref name="laneLines"/> drawn beside them (<see cref="HighwayGuides.Lines"/>, in
        /// the same order); with none (empty), they keep a gap inside their column (<see cref="NoteGap"/>).
        /// </summary>
        public void Gather(ManiaPlayfield playfield, ReadOnlySpan<Vector2> columnLanes, ReadOnlySpan<DepthCover> columnCovers,
                           ReadOnlySpan<HighwayGuides.GuideLine> laneLines, IScrollAlgorithm algorithm, double timeRange, HighwayScroll scroll,
                           double currentTime)
        {
            Count = 0;
            Invalidate(Invalidation.DrawNode);

            projection = new HighwayProjection(layout, DrawSize);
            pixelsPerUnit = MathF.Sqrt(DrawInfo.Matrix.M11 * DrawInfo.Matrix.M11 + DrawInfo.Matrix.M12 * DrawInfo.Matrix.M12);

            var stages = playfield.Stages;
            int columnIndex = 0;
            int lineIndex = 0;

            for (int i = 0; i < stages.Count; i++)
            {
                var columns = stages[i].Columns;

                for (int j = 0; j < columns.Length; j++, columnIndex++, lineIndex++)
                {
                    if (columnIndex >= columnLanes.Length)
                        return;

                    var column = columns[j];

                    Vector2 lanes = columnLanes[columnIndex];

                    // Each stage has a line before each of its columns and one after its last.
                    sides = lineIndex + 1 < laneLines.Length
                        ? new NoteSides(laneLines[lineIndex], laneLines[lineIndex + 1])
                        : null;

                    if (Ported is PortedNoteShapes ported && columnIndex < ported.Count)
                    {
                        cover = columnIndex < columnCovers.Length ? columnCovers[columnIndex] : DepthCover.None;

                        foreach (var entry in column.HitObjectContainer.AliveEntries)
                        {
                            switch (entry.Value)
                            {
                                case DrawableHoldNote hold:
                                    addPortedHold(hold, ported, columnIndex, lanes, algorithm, timeRange, scroll, currentTime);
                                    break;

                                case DrawableNote note:
                                    addPortedNote(note, ported, columnIndex, lanes, algorithm, timeRange, scroll, currentTime);
                                    break;
                            }
                        }

                        continue;
                    }

                    Color4 colour = ColumnColour(column.AccentColour.Value, j, columns.Length, column.IsSpecial);
                    cover = columnIndex < columnCovers.Length ? columnCovers[columnIndex] : DepthCover.None;

                    foreach (var entry in column.HitObjectContainer.AliveEntries)
                    {
                        switch (entry.Value)
                        {
                            case DrawableHoldNote hold:
                                addHold(hold, lanes, colour, algorithm, timeRange, scroll, currentTime);
                                break;

                            case DrawableNote note:
                                addNote(note, lanes, colour, algorithm, timeRange, scroll, currentTime);
                                break;
                        }
                    }
                }

                // Past the stage's right edge.
                lineIndex++;
            }
        }

        /// <summary>
        /// A column's note colour: the skin's column colour when it is meant for notes (argon and triangles colour their
        /// notes with it), otherwise the built-in colours. Legacy skins' column colour is the lane background, usually
        /// black or faint, and their notes are textures, so it says nothing about how notes should look.
        /// </summary>
        public static Color4 ColumnColour(Color4 accent, int index, int columnCount, bool isSpecial)
        {
            if (accent.A >= 0.5f && Math.Max(accent.R, Math.Max(accent.G, accent.B)) >= 0.35f)
                return accent.Opacity(1f);

            if (isSpecial)
                return SPECIAL_COLOUR;

            int fromEdge = Math.Min(index, columnCount - 1 - index);
            return fromEdge % 2 == 0 ? OUTER_COLOUR : INNER_COLOUR;
        }

        /// <summary>
        /// The gap on each side of a note in a lane <paramref name="laneWidth"/> wide (lane space), where the whole track (one
        /// unit of lane space) is <paramref name="trackPixels"/> screen pixels wide: <see cref="NOTE_GAP"/> of the lane, at
        /// least <see cref="MIN_GAP_PIXELS"/>, at most a quarter of the lane.
        /// </summary>
        public static float NoteGap(float laneWidth, float trackPixels)
        {
            float gap = laneWidth * NOTE_GAP;

            if (trackPixels > 0)
                gap = Math.Max(gap, MIN_GAP_PIXELS / trackPixels);

            return Math.Min(gap, laneWidth * max_gap);
        }

        /// <summary>
        /// The colour <paramref name="snapSource"/>'s note is drawn in: its snap colour when timing-based colouring is on
        /// (mania sets it as the drawable's colour, white otherwise), else its column's colour.
        /// </summary>
        private Color4 noteColour(DrawableNote snapSource, Color4 columnColour)
            => SnapColours ? snapSource.Colour.TopLeft.SRGB.Opacity(1f) : columnColour;

        private void addNote(DrawableNote note, Vector2 lanes, Color4 columnColour, IScrollAlgorithm algorithm, double timeRange, HighwayScroll scroll,
                             double currentTime)
        {
            if (note.Alpha <= 0)
                return;

            float depth = scroll.DepthAt(algorithm, note.HitObject.StartTime, currentTime, timeRange);
            addBar(note.HitObject, NoteQuadKind.Note, lanes, depth, NoteThickness, noteColour(note, columnColour), note.Alpha);
        }

        private void addHold(DrawableHoldNote hold, Vector2 lanes, Color4 columnColour, IScrollAlgorithm algorithm, double timeRange, HighwayScroll scroll,
                             double currentTime)
        {
            if (hold.Alpha <= 0)
                return;

            double startTime = hold.HitObject.StartTime;

            float headDepth = scroll.DepthAt(algorithm, startTime, currentTime, timeRange);
            float tailDepth = headDepth + scroll.LengthBetween(algorithm, startTime, hold.HitObject.EndTime, timeRange);

            // As in mania, a held note's head stops at the judgement line and the body shortens from there, unless it was hit
            // early (it waits for its time) or the hold was dropped. Held past its end, the head stays on the line (with no
            // body left) until the release is judged, up to the tail's late window after the end. A hold with no length
            // passes the line like a tap, as mania only pins heads of holds it draws with a height.
            if (tailDepth > headDepth && currentTime >= startTime && hold.Head.IsHit && !hold.Result.DroppedHoldAfter(startTime))
                headDepth = Math.Max(headDepth, 0);

            bool broken = hold.MissingStartTime.Value != null;
            Color4 colour = broken ? broken_colour : noteColour(hold.Head, columnColour);

            // The body is a share of the narrowest the column's bars get on the track, centred on it, so it is never wider than
            // its head or tail wherever they are, and keeps one width as it scrolls. Their sides differ a little with depth: a
            // line's pixel floor or the gap's narrows them far away, a group divider's feathered core near the line. Both grow
            // with one over the track's width, as the larger of a constant and a linear term, so the narrowest is at one end.
            Vector2 near = barSides(lanes, 0, 0);
            Vector2 far = barSides(lanes, 1, 0);
            float bodyLeft = Math.Max(near.X, far.X);
            float bodyRight = Math.Min(near.Y, far.Y);
            float bodyInset = Math.Max(0, bodyRight - bodyLeft) * (1 - BODY_WIDTH) / 2;
            float bodyAlpha = broken ? 0.35f : hold.IsHolding.Value ? 0.85f : 0.55f;
            float bodyStart = headDepth + NoteThickness / 2;

            if (tailDepth > bodyStart && bodyRight > bodyLeft)
            {
                Color4 body = colour.Opacity(bodyAlpha * HoldOpacity * hold.Alpha);
                add(new NoteQuad(hold.HitObject, NoteQuadKind.HoldBody, bodyLeft + bodyInset, bodyRight - bodyInset, bodyStart, tailDepth, body, body));
            }

            addBar(hold.HitObject, NoteQuadKind.HoldTail, lanes, tailDepth, NoteThickness / 2, colour, hold.Alpha * hold.Tail.Alpha);
            addBar(hold.HitObject, NoteQuadKind.HoldHead, lanes, headDepth, NoteThickness, colour, hold.Alpha * hold.Head.Alpha);
        }

        /// <summary>
        /// The tint a ported image is drawn with: white (the image's own colours), or the snap colour with timing-based colouring
        /// on, which mania applies to legacy skins' notes the same way.
        /// </summary>
        private Color4 portedTint(DrawableNote snapSource) => SnapColours ? snapSource.Colour.TopLeft.SRGB.Opacity(1f) : Color4.White;

        private void addPortedNote(DrawableNote note, PortedNoteShapes ported, int column, Vector2 lanes, IScrollAlgorithm algorithm, double timeRange,
                                   HighwayScroll scroll, double currentTime)
        {
            if (note.Alpha <= 0)
                return;

            var texture = PortedNoteShapes.FrameAt(ported.Columns[column].Note, ported.NoteFrameLength, currentTime);

            if (texture == null)
                return;

            float depth = scroll.DepthAt(algorithm, note.HitObject.StartTime, currentTime, timeRange);
            Color4 colour = portedTint(note).Opacity(note.Alpha);

            addImage(note.HitObject, NoteQuadKind.Note, texture, lanes.X, lanes.Y, depth, depth + ported.NoteDepth[column], colour, false);
        }

        private void addPortedHold(DrawableHoldNote hold, PortedNoteShapes ported, int column, Vector2 lanes, IScrollAlgorithm algorithm, double timeRange,
                                   HighwayScroll scroll, double currentTime)
        {
            if (hold.Alpha <= 0)
                return;

            var images = ported.Columns[column];
            double startTime = hold.HitObject.StartTime;

            float headDepth = scroll.DepthAt(algorithm, startTime, currentTime, timeRange);
            float tailDepth = headDepth + scroll.LengthBetween(algorithm, startTime, hold.HitObject.EndTime, timeRange);

            // Where the head would be if it were not pinned: a repeated body pattern keeps moving with the note under the
            // pinned head, as mania's masked body does.
            float scrollingHeadDepth = headDepth;

            // Pinned to the line while held, as for highway notes (see addHold).
            if (tailDepth > headDepth && currentTime >= startTime && hold.Head.IsHit && !hold.Result.DroppedHoldAfter(startTime))
                headDepth = Math.Max(headDepth, 0);

            // As mania's legacy pieces: a hold whose head was missed darkens.
            bool broken = hold.MissingStartTime.Value != null;
            Color4 tint = broken ? Color4.DarkGray : portedTint(hold.Head);

            float headThickness = ported.HeadDepth[column];
            float tailThickness = ported.TailDepth[column];

            // As mania's legacy layout: the body runs from the middle of the head to the middle of the tail, which lies past
            // the hold's end, upside down.
            float bodyStart = headDepth + headThickness / 2;
            float bodyEnd = tailDepth + tailThickness / 2;

            // The body's animation (if any) plays only while held, at mania's 30 ms a frame.
            var body = hold.IsHolding.Value
                ? PortedNoteShapes.FrameAt(images.Body, PortedNoteShapes.BODY_FRAME_LENGTH, currentTime)
                : images.Body.Length > 0 ? images.Body[0] : null;

            if (body != null && bodyEnd > bodyStart)
            {
                float anchor = ported.BodyFromTail ? bodyEnd : scrollingHeadDepth + headThickness / 2;
                addBody(hold.HitObject, body, lanes, bodyStart, bodyEnd, ported.BodyTile[column], anchor, tint.Opacity(HoldOpacity * hold.Alpha));
            }

            var tail = PortedNoteShapes.FrameAt(images.Tail, ported.NoteFrameLength, currentTime);
            if (tail != null)
                addImage(hold.HitObject, NoteQuadKind.HoldTail, tail, lanes.X, lanes.Y, tailDepth, tailDepth + tailThickness, tint.Opacity(hold.Alpha * hold.Tail.Alpha), true);

            var head = PortedNoteShapes.FrameAt(images.Head, ported.NoteFrameLength, currentTime);
            if (head != null)
                addImage(hold.HitObject, NoteQuadKind.HoldHead, head, lanes.X, lanes.Y, headDepth, headDepth + headThickness, tint.Opacity(hold.Alpha * hold.Head.Alpha), false);
        }

        /// <summary>
        /// A hold body from <paramref name="start"/> to <paramref name="end"/>: the image stretched over it, or repeated every
        /// <paramref name="tile"/> of depth from <paramref name="anchor"/> (a depth that scrolls with the note, so the pattern
        /// moves with it and does not slide while the body shortens).
        /// </summary>
        private void addBody(HitObject hitObject, Texture texture, Vector2 lanes, float start, float end, float tile, float anchor, Color4 colour)
        {
            if (colour.A <= 0)
                return;

            float height = texture.DisplayHeight;

            // Only what is on the track: tiles past either end are never drawn.
            float visibleStart = Math.Max(start, 0);
            float visibleEnd = Math.Min(end, 1);

            if (!(tile > 0) || (visibleEnd - visibleStart) / tile > PortedNoteShapes.MAX_BODY_TILES)
            {
                add(new NoteQuad(hitObject, NoteQuadKind.HoldBody, lanes.X, lanes.Y, start, end, colour, colour, texture, height, 0));
                return;
            }

            if (visibleEnd <= visibleStart)
                return;

            // Tile edges sit at anchor + k * tile. Counted in whole tiles, so a tile too small to move a float still ends.
            float firstTile = MathF.Floor((visibleStart - anchor) / tile);

            for (int k = 0; k <= PortedNoteShapes.MAX_BODY_TILES + 1; k++)
            {
                float tileStart = anchor + (firstTile + k) * tile;

                if (tileStart >= visibleEnd)
                    break;

                float d0 = Math.Max(tileStart, start);
                float d1 = Math.Min(tileStart + tile, end);

                if (d1 <= d0)
                    continue;

                // Each tile shows the image near edge at its bottom row; a cut tile keeps the rows it would show.
                float texNear = height * (1 - (d0 - tileStart) / tile);
                float texFar = height * (1 - (d1 - tileStart) / tile);

                add(new NoteQuad(hitObject, NoteQuadKind.HoldBody, lanes.X, lanes.Y, d0, d1, colour, colour, texture, texNear, texFar));
            }
        }

        /// <summary>
        /// A ported image lying on the track from <paramref name="depth0"/> (its bottom edge, which crosses the judgement line
        /// at its time) to <paramref name="depth1"/>, upside down when <paramref name="flip"/> (hold tails, as mania draws them).
        /// </summary>
        private void addImage(HitObject hitObject, NoteQuadKind kind, Texture texture, float lane0, float lane1, float depth0, float depth1, Color4 colour, bool flip)
        {
            if (colour.A <= 0 || !(depth1 > depth0))
                return;

            float height = texture.DisplayHeight;
            add(new NoteQuad(hitObject, kind, lane0, lane1, depth0, depth1, colour, colour, texture, flip ? 0 : height, flip ? height : 0));
        }

        /// <summary>
        /// A note-like bar from <paramref name="depth"/> (its leading edge, which crosses the judgement line at its time) to
        /// <paramref name="depth"/> + <paramref name="thickness"/>, with a bright rim at the near edge, across its column
        /// (<paramref name="lanes"/>, lane space) as <see cref="barSides"/> places its sides.
        /// </summary>
        private void addBar(HitObject hitObject, NoteQuadKind kind, Vector2 lanes, float depth, float thickness, Color4 colour, float alpha)
        {
            if (alpha <= 0)
                return;

            Vector2 bar = barSides(lanes, depth, thickness);
            float rim = depth + thickness * rim_fraction;

            add(new NoteQuad(hitObject, kind, bar.X, bar.Y, rim, depth + thickness, colour.Lighten(0.25f).Opacity(alpha), colour.Opacity(alpha), Sides: sides));
            add(new NoteQuad(hitObject, kind, bar.X, bar.Y, depth, rim, Color4.White.Opacity(alpha), Color4.White.Opacity(alpha), Sides: sides));
        }

        /// <summary>
        /// The sides, in lane space, of a bar from <paramref name="depth"/> to <paramref name="depth"/> +
        /// <paramref name="thickness"/> in the column at <paramref name="lanes"/>, as measured at its far side (or the track's
        /// end, where it is cut). Where lane lines are drawn, the bar is drawn with each corner on the solid edge of the line
        /// beside it (<see cref="NoteSides"/>); these are its far corners. Without lines, the column's edges less the gap on
        /// each side (<see cref="NoteGap"/>).
        /// </summary>
        private Vector2 barSides(Vector2 lanes, float depth, float thickness)
        {
            float trackPixels = projection.WidthAt(Math.Clamp(depth + thickness, 0, 1)) * pixelsPerUnit;

            if (sides is NoteSides lines)
                return lines.LanesAt(trackPixels);

            float gap = NoteGap(lanes.Y - lanes.X, trackPixels);
            return new Vector2(lanes.X + gap, lanes.Y - gap);
        }

        /// <summary>
        /// The lane lines on each side of a plain bar, which its sides meet (Alpha 2026-11-04: notes as seamless as possible).
        /// </summary>
        /// <remarks>
        /// Notes are drawn over the lines, so a side must never cross into one, nor stop short of it: a 1 px line loses its only
        /// pixel to a note a hundredth of a pixel too wide, and a line on a pixel boundary (the centre line, at an even width)
        /// shows a dark pixel beside a note a hundredth too narrow. So each corner is placed on the line's edge at its own depth
        /// (<see cref="LanesAt"/>): a line's edge is straight on screen (its width grows with the track's, linearly in screen
        /// height, until its pixel floor, then stays), and so is the bar's side between two of its points.
        /// </remarks>
        public readonly record struct NoteSides(HighwayGuides.GuideLine Left, HighwayGuides.GuideLine Right)
        {
            /// <summary>
            /// The sides, in lane space, at a depth where the track is <paramref name="trackPixels"/> screen pixels wide: each on
            /// the solid edge of its line (<see cref="HighwayGuides.SolidHalfWidth"/>), but a line never takes more than a quarter
            /// of the lane, so a note on a tiny lane still shows.
            /// </summary>
            public Vector2 LanesAt(float trackPixels)
            {
                float cell = Right.Lane - Left.Lane;
                float left = Math.Min(HighwayGuides.SolidHalfWidth(Left.Kind, trackPixels), cell * max_gap);
                float right = Math.Min(HighwayGuides.SolidHalfWidth(Right.Kind, trackPixels), cell * max_gap);

                return new Vector2(Left.Lane + left, Right.Lane - right);
            }
        }

        /// <summary>
        /// Adds <paramref name="quad"/>, cut where the column's cover or the far fade changes so each piece fades as mania's
        /// cover would fade it, and the far fade on top (the two multiply). Fully covered pieces are left out.
        /// </summary>
        private void add(in NoteQuad quad)
        {
            var fade = FarFade;

            if (!cover.IsActive && !fade.IsActive)
            {
                append(quad);
                return;
            }

            float from = quad.Depth0;

            while (from < quad.Depth1)
            {
                // An inactive cover cuts nowhere and shows everything.
                float to = Math.Min(cover.NextCut(from, quad.Depth1), fade.NextCut(from, quad.Depth1));
                float middle = (from + to) / 2;

                float visibleFrom = cover.VisibilityAt(from, middle) * fade.VisibilityAt(from, middle);
                float visibleTo = cover.VisibilityAt(to, middle) * fade.VisibilityAt(to, middle);

                if (visibleFrom > 0 || visibleTo > 0)
                {
                    Color4 near = colourAt(quad, from);
                    Color4 far = colourAt(quad, to);

                    append(quad with
                    {
                        Depth0 = from,
                        Depth1 = to,
                        Near = near.Opacity(near.A * visibleFrom),
                        Far = far.Opacity(far.A * visibleTo),
                        TexNear = textureAt(quad, from),
                        TexFar = textureAt(quad, to),
                    });
                }

                from = to;
            }
        }

        private static Color4 colourAt(in NoteQuad quad, float depth)
            => quad.Depth1 > quad.Depth0 ? Interpolation.ValueAt(depth, quad.Near, quad.Far, quad.Depth0, quad.Depth1) : quad.Near;

        /// <summary>
        /// The texture row a ported image shows at <paramref name="depth"/>: linear in depth, as it is drawn.
        /// </summary>
        private static float textureAt(in NoteQuad quad, float depth)
            => quad.Depth1 > quad.Depth0 ? quad.TexNear + (quad.TexFar - quad.TexNear) * (depth - quad.Depth0) / (quad.Depth1 - quad.Depth0) : quad.TexNear;

        private void append(in NoteQuad quad)
        {
            if (Count == quads.Length)
                Array.Resize(ref quads, quads.Length * 2);

            quads[Count++] = quad;
        }

        protected override DrawNode CreateDrawNode() => new HighwayNoteLayerDrawNode(this);

        public enum NoteQuadKind
        {
            Note,
            HoldHead,
            HoldBody,
            HoldTail,
        }

        /// <summary>
        /// One flat quad on the track, from <see cref="Lane0"/> to <see cref="Lane1"/> across and <see cref="Depth0"/> (near) to
        /// <see cref="Depth1"/> (far) along it: a plain colour, or with <see cref="Texture"/>, a skin image whose rows
        /// <see cref="TexNear"/> and <see cref="TexFar"/> (texture pixels from its top) lie at the near and far edges. With
        /// <see cref="Sides"/>, its corners are on the lane lines' edges instead, at each corner's depth (<see cref="NoteSides"/>),
        /// and the lanes are those at its bar's far side.
        /// </summary>
        public readonly record struct NoteQuad(HitObject HitObject, NoteQuadKind Kind, float Lane0, float Lane1, float Depth0, float Depth1, Color4 Near, Color4 Far,
                                               Texture? Texture = null, float TexNear = 0, float TexFar = 0, NoteSides? Sides = null)
        {
            /// <summary>
            /// The sides, in lane space, at a depth where the track is <paramref name="trackPixels"/> screen pixels wide.
            /// </summary>
            public Vector2 LanesAt(float trackPixels) => Sides is NoteSides sides ? sides.LanesAt(trackPixels) : new Vector2(Lane0, Lane1);

            /// <summary>
            /// Where this piece is drawn on screen (in <paramref name="projection"/>'s space), from <paramref name="depth0"/> to
            /// <paramref name="depth1"/>, where one of its units is <paramref name="pixelsPerUnit"/> screen pixels.
            /// </summary>
            public Quad ToScreen(in HighwayProjection projection, float pixelsPerUnit, float depth0, float depth1)
            {
                if (Sides == null)
                    return projection.ToScreen(Lane0, Lane1, depth0, depth1);

                Vector2 near = LanesAt(projection.WidthAt(depth0) * pixelsPerUnit);
                Vector2 far = LanesAt(projection.WidthAt(depth1) * pixelsPerUnit);

                return new Quad(projection.ToScreen(far.X, depth1), projection.ToScreen(far.Y, depth1), projection.ToScreen(near.X, depth0), projection.ToScreen(near.Y, depth0));
            }
        }

        private class HighwayNoteLayerDrawNode : DrawNode
        {
            protected new HighwayNoteLayer Source => (HighwayNoteLayer)base.Source;

            private IShader shader = null!;
            private HighwayProjection projection;
            private NoteQuad[] quads = Array.Empty<NoteQuad>();
            private int count;
            private float alpha;
            private float pixelsPerUnit;

            public HighwayNoteLayerDrawNode(HighwayNoteLayer source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                projection = new HighwayProjection(Source.layout, Source.DrawSize);
                alpha = DrawColourInfo.Colour.MaxAlpha;
                pixelsPerUnit = MathF.Sqrt(DrawInfo.Matrix.M11 * DrawInfo.Matrix.M11 + DrawInfo.Matrix.M12 * DrawInfo.Matrix.M12);

                count = Source.Count;
                if (quads.Length < count)
                    quads = new NoteQuad[Source.quads.Length];
                Array.Copy(Source.quads, quads, count);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (alpha <= 0 || count == 0)
                    return;

                shader.Bind();

                // Bodies first, so heads and tails are drawn over them.
                for (int i = 0; i < count; i++)
                {
                    if (quads[i].Kind == NoteQuadKind.HoldBody)
                        draw(renderer, quads[i]);
                }

                for (int i = 0; i < count; i++)
                {
                    if (quads[i].Kind != NoteQuadKind.HoldBody)
                        draw(renderer, quads[i]);
                }

                shader.Unbind();
            }

            private void draw(IRenderer renderer, in NoteQuad quad)
            {
                // Notes are drawn from the judgement line to the far edge: past the line they are gone, as in Chunithm.
                float depth0 = Math.Max(quad.Depth0, 0);
                float depth1 = Math.Min(quad.Depth1, 1);

                if (depth1 <= depth0)
                    return;

                // Keep the colours where they were on the unclipped quad.
                float length = quad.Depth1 - quad.Depth0;
                Color4 near = length > 0 ? Interpolation.ValueAt(depth0, quad.Near, quad.Far, quad.Depth0, quad.Depth1) : quad.Near;
                Color4 far = length > 0 ? Interpolation.ValueAt(depth1, quad.Near, quad.Far, quad.Depth0, quad.Depth1) : quad.Far;

                var colour = ColourInfo.GradientVertical(far.Opacity(far.A * alpha), near.Opacity(near.A * alpha));

                if (quad.Texture is Texture texture)
                {
                    float texNear = textureAt(quad, depth0);
                    float texFar = textureAt(quad, depth1);
                    bool flip = texNear < texFar;

                    var rect = new RectangleF(0, Math.Min(texNear, texFar), texture.DisplayWidth, Math.Abs(texNear - texFar));

                    if (rect.Height > 0)
                        PortDrawing.DrawOnTrack(renderer, texture, rect, projection, DrawInfo.Matrix, quad.Lane0, quad.Lane1, depth0, depth1, colour, flip);

                    return;
                }

                var screen = quad.ToScreen(projection, pixelsPerUnit, depth0, depth1);

                renderer.DrawQuad(renderer.WhitePixel, screen * DrawInfo.Matrix, colour);
            }
        }
    }
}
