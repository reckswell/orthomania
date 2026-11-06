// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Mania.Skinning;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.SkinPort
{
    /// <summary>
    /// Everything the skin porter draws, read from a stable (legacy) skin's mania images and its <c>skin.ini</c> [Mania]
    /// section, the same way osu!mania's legacy skin pieces read them (lookups, image names and fallbacks), so a skin looks
    /// on the highway as it does flat. Nothing is pre-rendered: the highway draws these textures in perspective every frame.
    /// </summary>
    /// <remarks>
    /// Sizes are in osu!mania's units (a 768-unit tall playfield; stable's 480-unit values times 1.6), as mania's own legacy
    /// pieces use them.
    /// </remarks>
    public sealed class PortedSkin
    {
        /// <summary>
        /// Stable's default <c>ColumnStart</c>: a skin that leaves it out gets this, and is centred like lazer centres it.
        /// </summary>
        public const float DEFAULT_COLUMN_START = 136;

        /// <summary>
        /// One entry per stage, in playfield order.
        /// </summary>
        public readonly PortedStage[] Stages;

        /// <summary>
        /// Every column of every stage, in playfield column order.
        /// </summary>
        public readonly PortedColumn[] Columns;

        private PortedSkin(PortedStage[] stages, PortedColumn[] columns)
        {
            Stages = stages;
            Columns = columns;
        }

        /// <summary>
        /// Reads the skin's mania assets for <paramref name="playfield"/>'s stages and columns.
        /// </summary>
        public static PortedSkin Create(ISkinSource skin, ManiaPlayfield playfield)
        {
            var stages = new List<PortedStage>();
            var columns = new List<PortedColumn>();

            decimal version = skin.GetConfig<SkinConfiguration.LegacySetting, decimal>(SkinConfiguration.LegacySetting.Version)?.Value ?? 1;
            int totalColumns = playfield.TotalColumns;
            float? columnStart = playfield.Stages.Count == 1 ? ReadColumnStart(skin, totalColumns) : null;

            foreach (var stage in playfield.Stages)
            {
                var stageColumns = new PortedColumn[stage.Columns.Length];

                for (int i = 0; i < stage.Columns.Length; i++)
                {
                    var column = stage.Columns[i];
                    stageColumns[i] = readColumn(skin, column, i, stage.Columns.Length, version);
                    columns.Add(stageColumns[i]);
                }

                stages.Add(readStage(skin, stageColumns, columnStart));
            }

            return new PortedSkin(stages.ToArray(), columns.ToArray());
        }

        private static PortedStage readStage(ISkinSource skin, PortedColumn[] columns, float? columnStart)
        {
            int lightFps = skin.GetManiaSkinConfig<int>(LegacyManiaSkinConfigurationLookups.LightFramePerSecond)?.Value ?? 60;

            return new PortedStage
            {
                Columns = columns,
                Left = skin.GetTexture(stageImage(skin, LegacyManiaSkinConfigurationLookups.LeftStageImage, "mania-stage-left")),
                Right = skin.GetTexture(stageImage(skin, LegacyManiaSkinConfigurationLookups.RightStageImage, "mania-stage-right")),
                Hint = skin.GetTexture(stageImage(skin, LegacyManiaSkinConfigurationLookups.HitTargetImage, "mania-stage-hint")),
                Bottom = frames(skin, stageImage(skin, LegacyManiaSkinConfigurationLookups.BottomStageImage, "mania-stage-bottom")).FirstOrNull(),
                Light = frames(skin, stageImage(skin, LegacyManiaSkinConfigurationLookups.LightImage, "mania-stage-light")),
                LightFrameLength = 1000d / Math.Max(1, lightFps),
                HitPosition = skin.GetManiaSkinConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition)?.Value ?? Stage.HIT_TARGET_POSITION,
                ShowJudgementLine = skin.GetManiaSkinConfig<bool>(LegacyManiaSkinConfigurationLookups.ShowJudgementLine)?.Value ?? true,
                JudgementLineColour = LegacyColourCompatibility.DisallowZeroAlpha(
                    skin.GetManiaSkinConfig<Color4>(LegacyManiaSkinConfigurationLookups.JudgementLineColour)?.Value ?? Color4.White),
                BodyStyle = skin.GetConfig<ManiaSkinConfigurationLookup, LegacyManiaSkinConfiguration.LegacyNoteBodyStyle>(
                    new ManiaSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteBodyStyle))?.Value ?? LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.RepeatBottom,
                WidthForNoteHeightScale = skin.GetConfig<ManiaSkinConfigurationLookup, float>(
                    new ManiaSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale))?.Value is float w && w > 0 ? w : null,
                ColumnStart = columnStart,
            };
        }

        private static PortedColumn readColumn(ISkinSource skin, Column column, int indexInStage, int stageColumns, decimal version)
        {
            int index = column.Index;

            // As LegacyManiaColumnElement: "S" for the special column, otherwise 1 and 2 alternating inwards from both edges.
            string fallback = column.IsSpecial ? "S" : Math.Min(indexInStage, stageColumns - 1 - indexInStage) % 2 == 0 ? "1" : "2";

            string image(LegacyManiaSkinConfigurationLookups lookup, string defaultName)
                => skin.GetManiaSkinConfig<string>(lookup, index)?.Value ?? defaultName;

            var note = frames(skin, image(LegacyManiaSkinConfigurationLookups.NoteImage, $"mania-note{fallback}"));
            var head = frames(skin, image(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, $"mania-note{fallback}H"));
            var tail = frames(skin, image(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, $"mania-note{fallback}T"));

            // As LegacyHoldNoteHeadPiece and LegacyHoldNoteTailPiece: a missing head is the note, a missing tail the head.
            if (head.Length == 0)
                head = note;
            if (tail.Length == 0)
                tail = head;

            var explosion = frames(skin, image(LegacyManiaSkinConfigurationLookups.ExplosionImage, "lightingN"));
            var holdLight = frames(skin, image(LegacyManiaSkinConfigurationLookups.HoldNoteLightImage, "lightingL"));

            float leftLine = skin.GetManiaSkinConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth, index)?.Value ?? 1;
            float rightLine = skin.GetManiaSkinConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth, index)?.Value ?? 1;
            bool isLast = indexInStage == stageColumns - 1;

            return new PortedColumn
            {
                Note = note,
                Head = head,
                Tail = tail,
                Body = frames(skin, image(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, $"mania-note{fallback}L")),
                KeyUp = skin.GetTexture(image(LegacyManiaSkinConfigurationLookups.KeyImage, $"mania-key{fallback}"), WrapMode.ClampToEdge, default),
                KeyDown = skin.GetTexture(image(LegacyManiaSkinConfigurationLookups.KeyImageDown, $"mania-key{fallback}D"), WrapMode.ClampToEdge, default),
                Background = skin.GetManiaSkinConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, index)?.Value ?? Color4.Black,
                LightColour = LegacyColourCompatibility.DisallowZeroAlpha(
                    skin.GetManiaSkinConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, index)?.Value ?? Color4.White),
                LineColour = skin.GetManiaSkinConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour, index)?.Value ?? Color4.White,
                LeftLineWidth = leftLine,
                RightLineWidth = rightLine,
                HasLeftLine = leftLine > 0,
                // As LegacyStageBackground: right lines only from skin version 2.4, except the stage's last column.
                HasRightLine = (rightLine > 0 && version >= 2.4m) || isLast,
                KeysUnderNotes = skin.GetManiaSkinConfig<bool>(LegacyManiaSkinConfigurationLookups.KeysUnderNotes, index)?.Value ?? false,
                LightPosition = skin.GetManiaSkinConfig<float>(LegacyManiaSkinConfigurationLookups.LightPosition, index)?.Value ?? 0,
                Explosion = explosion,
                ExplosionFrameLength = effectFrameLength(explosion.Length),
                ExplosionScale = skin.GetManiaSkinConfig<float>(LegacyManiaSkinConfigurationLookups.ExplosionScale, index)?.Value ?? 1,
                HoldLight = holdLight,
                HoldLightFrameLength = effectFrameLength(holdLight.Length),
                HoldLightScale = skin.GetManiaSkinConfig<float>(LegacyManiaSkinConfigurationLookups.HoldNoteLightScale, index)?.Value ?? 1,
            };
        }

        /// <summary>
        /// As mania's legacy hit explosion and hold light: the whole animation in 170 ms, but no faster than 60 frames a second.
        /// </summary>
        private static double effectFrameLength(int frameCount) => frameCount > 0 ? Math.Max(1000 / 60.0, 170.0 / frameCount) : 1000 / 60.0;

        private static string stageImage(ISkinSource skin, LegacyManiaSkinConfigurationLookups lookup, string defaultName)
            => skin.GetManiaSkinConfig<string>(lookup)?.Value ?? defaultName;

        /// <summary>
        /// An image's animation frames (<c>name-0</c>, <c>name-1</c>, ...) or the single image, from the first skin source that
        /// has either, as osu!'s legacy animations are looked up. Empty when no source has it.
        /// </summary>
        private static Texture[] frames(ISkinSource skin, string name)
            => skin.GetTextures(name, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _);

        private static readonly FieldInfo? mania_configurations = typeof(LegacySkin).GetField("ManiaConfigurations", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// The skin's <c>ColumnStart</c> for <paramref name="totalColumns"/> keys, in stable's 480-unit tall screen space from
        /// the left edge, or null when the skin leaves it at the default. osu! (lazer) parses it but always centres the stage,
        /// so it is only reachable through the parsed configuration (an internal field; <c>SkinPortLayoutTest</c> fails if it is
        /// renamed).
        /// </summary>
        public static float? ReadColumnStart(ISkinSource skin, int totalColumns)
        {
            var legacy = fullLegacySkin(skin);
            return legacy == null ? null : ReadColumnStart(legacy, totalColumns);
        }

        /// <summary>
        /// <paramref name="skin"/>'s <c>ColumnStart</c> for <paramref name="totalColumns"/> keys, or null when it is the default
        /// or not set for that key count.
        /// </summary>
        public static float? ReadColumnStart(LegacySkin skin, int totalColumns)
        {
            if (configurationFor(skin, totalColumns) is LegacyManiaSkinConfiguration config
                && Math.Abs(config.ColumnStart - DEFAULT_COLUMN_START) > 0.01f
                && float.IsFinite(config.ColumnStart))
            {
                return config.ColumnStart;
            }

            return null;
        }

        /// <summary>
        /// The skin's <c>SpecialStyle</c> for <paramref name="totalColumns"/> keys (which side of an N+1 map holds its special
        /// column), or null when the skin is not a stable skin or sets none. osu! (lazer) parses it but does not use it.
        /// </summary>
        public static LegacyManiaSkinConfiguration.LegacySpecialStyle? ReadSpecialStyle(ISkinSource skin, int totalColumns)
        {
            var legacy = fullLegacySkin(skin);
            return legacy == null ? null : ReadSpecialStyle(legacy, totalColumns);
        }

        /// <inheritdoc cref="ReadSpecialStyle(ISkinSource, int)"/>
        public static LegacyManiaSkinConfiguration.LegacySpecialStyle? ReadSpecialStyle(LegacySkin skin, int totalColumns)
            => configurationFor(skin, totalColumns)?.SpecialStyle;

        /// <summary>
        /// The first full stable skin among <paramref name="skin"/>'s sources (beatmap skins carry no mania layout), or null.
        /// </summary>
        private static LegacySkin? fullLegacySkin(ISkinSource skin)
        {
            foreach (var source in skin.AllSources)
            {
                ISkin s = source;

                while (s is ISkinTransformer transformer)
                    s = transformer.Skin;

                if (s is LegacySkin legacy && s is not LegacyBeatmapSkin)
                    return legacy;
            }

            return null;
        }

        /// <summary>
        /// osu!'s parse of the skin's <c>[Mania]</c> section for <paramref name="totalColumns"/> keys, or null without one
        /// (an internal field; <c>SkinPortLayoutTest</c> fails if it is renamed).
        /// </summary>
        private static LegacyManiaSkinConfiguration? configurationFor(LegacySkin skin, int totalColumns)
            => mania_configurations?.GetValue(skin) is IDictionary configurations && configurations.Contains(totalColumns)
                ? configurations[totalColumns] as LegacyManiaSkinConfiguration
                : null;

        /// <summary>
        /// Whether reading <c>ColumnStart</c> still works with this osu! build (see <see cref="ReadColumnStart(LegacySkin, int)"/>).
        /// </summary>
        public static bool CanReadColumnStart => mania_configurations?.FieldType.IsAssignableTo(typeof(IDictionary)) == true;
    }

    /// <summary>
    /// A stage's ported assets.
    /// </summary>
    public sealed class PortedStage
    {
        public PortedColumn[] Columns { get; init; } = Array.Empty<PortedColumn>();

        /// <summary>
        /// <c>mania-stage-left</c> and <c>-right</c>: drawn outside the stage's edges, the full stage height.
        /// </summary>
        public Texture? Left { get; init; }

        public Texture? Right { get; init; }

        /// <summary>
        /// <c>mania-stage-hint</c>: the band across the stage at the hit position.
        /// </summary>
        public Texture? Hint { get; init; }

        /// <summary>
        /// <c>mania-stage-bottom</c>: drawn at the bottom centre, 1.6 times its size.
        /// </summary>
        public Texture? Bottom { get; init; }

        /// <summary>
        /// <c>mania-stage-light</c>: a pressed column's light, rising from <see cref="PortedColumn.LightPosition"/>.
        /// </summary>
        public Texture[] Light { get; init; } = Array.Empty<Texture>();

        public double LightFrameLength { get; init; } = 1000 / 60d;

        /// <summary>
        /// The hit position's height above the bottom of the playfield, in mania units.
        /// </summary>
        public float HitPosition { get; init; } = Stage.HIT_TARGET_POSITION;

        public bool ShowJudgementLine { get; init; } = true;

        public Color4 JudgementLineColour { get; init; } = Color4.White;

        public LegacyManiaSkinConfiguration.LegacyNoteBodyStyle BodyStyle { get; init; } = LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.RepeatBottom;

        /// <summary>
        /// The width notes take their height from, in mania units, instead of the column's own width.
        /// </summary>
        public float? WidthForNoteHeightScale { get; init; }

        /// <summary>
        /// Where stable puts the stage's left edge, in stable's 480-unit tall screen space, or null when centred.
        /// </summary>
        public float? ColumnStart { get; init; }
    }

    /// <summary>
    /// A column's ported assets.
    /// </summary>
    public sealed class PortedColumn
    {
        public Texture[] Note { get; init; } = Array.Empty<Texture>();
        public Texture[] Head { get; init; } = Array.Empty<Texture>();
        public Texture[] Body { get; init; } = Array.Empty<Texture>();
        public Texture[] Tail { get; init; } = Array.Empty<Texture>();

        /// <summary>
        /// The key (receptor) images, bottom-anchored, the column's width and their own height.
        /// </summary>
        public Texture? KeyUp { get; init; }

        public Texture? KeyDown { get; init; }

        /// <summary>
        /// The column's background colour (<c>ColourN</c>), drawn with its alpha doubled as stable does.
        /// </summary>
        public Color4 Background { get; init; } = Color4.Black;

        public Color4 LightColour { get; init; } = Color4.White;

        public Color4 LineColour { get; init; } = Color4.White;

        /// <summary>
        /// Column line widths, in mania units before stable's 0.74 squash.
        /// </summary>
        public float LeftLineWidth { get; init; }

        public float RightLineWidth { get; init; }

        public bool HasLeftLine { get; init; }

        public bool HasRightLine { get; init; }

        public bool KeysUnderNotes { get; init; }

        /// <summary>
        /// The light's bottom edge, in mania units above the bottom of the playfield (<c>LightPosition</c>, as mania places it).
        /// </summary>
        public float LightPosition { get; init; }

        public Texture[] Explosion { get; init; } = Array.Empty<Texture>();

        public double ExplosionFrameLength { get; init; } = 1000 / 60d;

        public float ExplosionScale { get; init; } = 1;

        public Texture[] HoldLight { get; init; } = Array.Empty<Texture>();

        public double HoldLightFrameLength { get; init; } = 1000 / 60d;

        public float HoldLightScale { get; init; } = 1;
    }

    internal static class TextureArrayExtensions
    {
        public static Texture? FirstOrNull(this Texture[] textures) => textures.Length > 0 ? textures[0] : null;
    }
}
