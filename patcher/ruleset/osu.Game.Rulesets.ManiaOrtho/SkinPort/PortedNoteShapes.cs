// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.SkinPort
{
    /// <summary>
    /// How deep each column's ported note images are on the track this frame, from the skin's image proportions and the
    /// camera (<see cref="HighwaySettings.SkinNoteProportions"/>). Read by <see cref="UI.HighwayNoteLayer"/>.
    /// </summary>
    public sealed class PortedNoteShapes
    {
        /// <summary>
        /// The deepest a ported note image may be, so a square or tall image (circle notes) cannot cover the track.
        /// </summary>
        public const float MAX_NOTE_DEPTH = 0.15f;

        /// <summary>
        /// mania's legacy hold bodies animate at 30 ms a frame while held.
        /// </summary>
        public const double BODY_FRAME_LENGTH = 30;

        /// <summary>
        /// Past this many repeats on screen a hold body is stretched instead (a tiny repeat height).
        /// </summary>
        public const int MAX_BODY_TILES = 200;

        public readonly PortedColumn[] Columns;

        public int Count => Columns.Length;

        /// <summary>
        /// Each column's note, hold head and hold tail depth, and the depth of one repeat of its hold body (0 to stretch it).
        /// </summary>
        public readonly float[] NoteDepth;

        public readonly float[] HeadDepth;
        public readonly float[] TailDepth;
        public readonly float[] BodyTile;

        /// <summary>
        /// Whether repeated bodies are counted from the tail (stable's "repeat from the top").
        /// </summary>
        public bool BodyFromTail { get; }

        /// <summary>
        /// Notes animate at 60 frames a second, as osu!'s legacy animations default to.
        /// </summary>
        public double NoteFrameLength => LegacySkinExtensions.SIXTY_FRAME_TIME;

        private readonly bool stretchBodies;
        private readonly float? widthForNoteHeightScale;

        public PortedNoteShapes(PortedSkin skin)
        {
            Columns = skin.Columns;
            NoteDepth = new float[Columns.Length];
            HeadDepth = new float[Columns.Length];
            TailDepth = new float[Columns.Length];
            BodyTile = new float[Columns.Length];

            var style = skin.Stages.Length > 0 ? skin.Stages[0].BodyStyle : LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.RepeatBottom;

            stretchBodies = style == LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.Stretch;
            BodyFromTail = style == LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.RepeatTop;
            widthForNoteHeightScale = skin.Stages.Length > 0 ? skin.Stages[0].WidthForNoteHeightScale : null;
        }

        /// <summary>
        /// Works out every column's depths for this frame.
        /// </summary>
        /// <param name="projection">The highway's projection.</param>
        /// <param name="columnLanes">Each column's left and right edge in lane space, in playfield column order.</param>
        /// <param name="stage">The skin's flat stage.</param>
        /// <param name="skinProportions">Whether notes keep the skin's proportions; otherwise taps are <paramref name="noteThickness"/> deep.</param>
        /// <param name="noteThickness">The camera's note thickness.</param>
        public void Update(in HighwayProjection projection, ReadOnlySpan<Vector2> columnLanes, in FlatStage stage, bool skinProportions, float noteThickness)
        {
            for (int i = 0; i < Columns.Length && i < columnLanes.Length; i++)
            {
                var column = Columns[i];
                float lane = columnLanes[i].Y - columnLanes[i].X;

                // The column's width in mania units, as the skin lays it out.
                float columnWidth = stage.UnitSize > 0 ? lane * stage.Width / stage.UnitSize : 0;

                float note = depthOf(projection, lane, columnWidth, first(column.Note));
                float head = depthOf(projection, lane, columnWidth, first(column.Head));
                float tail = depthOf(projection, lane, columnWidth, first(column.Tail));
                float body = stretchBodies ? 0 : depthOf(projection, lane, columnWidth, first(column.Body));

                // The camera's thickness scales the whole set, so heads, tails and bodies keep their proportions to taps.
                float scale = 1;

                if (!skinProportions && note > 0)
                    scale = noteThickness / note;

                NoteDepth[i] = clamp(note * scale);
                HeadDepth[i] = clamp(head * scale);
                TailDepth[i] = clamp(tail * scale);
                BodyTile[i] = body * scale;
            }

            static float clamp(float depth) => Math.Clamp(depth, 0, MAX_NOTE_DEPTH);
        }

        /// <summary>
        /// The depth of <paramref name="texture"/> drawn as mania draws a note: the column's width (or the skin's
        /// <c>WidthForNoteHeightScale</c>) sets its height.
        /// </summary>
        private float depthOf(in HighwayProjection projection, float lane, float columnWidth, Texture? texture)
        {
            if (texture == null || texture.DisplayWidth <= 0 || columnWidth <= 0)
                return 0;

            float heightBase = widthForNoteHeightScale ?? columnWidth;
            float aspect = texture.DisplayHeight * heightBase / texture.DisplayWidth / columnWidth;

            return SkinPortLayout.NoteDepth(projection, lane, aspect, 1);
        }

        private static Texture? first(Texture[] frames) => frames.Length > 0 ? frames[0] : null;

        /// <summary>
        /// The frame of an animation shown at <paramref name="time"/>, looping, or null without frames.
        /// </summary>
        public static Texture? FrameAt(Texture[] frames, double frameLength, double time)
        {
            if (frames.Length == 0)
                return null;

            if (frames.Length == 1 || !(frameLength > 0))
                return frames[0];

            long index = (long)Math.Floor(time / frameLength) % frames.Length;
            return frames[index < 0 ? index + frames.Length : index];
        }
    }
}
