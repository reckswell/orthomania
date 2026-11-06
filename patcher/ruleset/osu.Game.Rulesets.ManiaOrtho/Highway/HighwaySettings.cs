// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;

namespace osu.Game.Rulesets.ManiaOrtho.Highway
{
    public enum HighwayCameraMode
    {
        /// <summary>
        /// True perspective: lanes converge toward a vanishing point above the far edge (Chunithm, SDVX).
        /// </summary>
        Perspective,

        /// <summary>
        /// Foreshortened track with parallel lanes and constant on-screen speed.
        /// </summary>
        [Description("Parallel tilt")]
        ParallelTilt,

        /// <summary>
        /// osu!mania's own layout, unchanged.
        /// </summary>
        Flat,
    }

    public enum HighwayNoteStyle
    {
        /// <summary>
        /// Notes drawn natively on the track with a fixed thickness (<see cref="UI.HighwayNoteLayer"/>), as SPEC 6 asks.
        /// </summary>
        [Description("Highway")]
        Highway,

        /// <summary>
        /// The skin's own note, hold and tail images, drawn sharp on the track by the skin porter
        /// (<see cref="SkinPort.PortedSkin"/>). Stable (legacy) skins only; other skins get <see cref="Highway"/>.
        /// </summary>
        [Description("Skin (ported)")]
        Ported,

        /// <summary>
        /// The skin's notes, drawn on the track with the rest of mania's stage (warped: thicker and softer near the line).
        /// </summary>
        [Description("Skin (warped)")]
        Skin,
    }

    public enum HighwayHitEffectStyle
    {
        /// <summary>
        /// Bursts lying on the track at the judgement line, coloured by result, and a glow while a hold is held
        /// (<see cref="UI.HighwayHitEffectLayer"/>). The skin's hit explosions are hidden.
        /// </summary>
        [Description("Highway")]
        Highway,

        /// <summary>
        /// The skin's own hit lighting and hold lighting images, standing upright at the judgement line at the skin's size
        /// relative to its lane (<see cref="SkinPort.PortedLighting"/>). Stable (legacy) skins only; other skins get
        /// <see cref="Highway"/>.
        /// </summary>
        [Description("Skin (ported)")]
        Ported,

        /// <summary>
        /// The skin's hit explosions, drawn on the track with the rest of mania's stage (warped: stretched near the line).
        /// </summary>
        [Description("Skin (warped)")]
        Skin,
    }

    public enum HighwayTrackStyle
    {
        /// <summary>
        /// The highway's own track (<see cref="UI.HighwayTrack"/>): a near-black floor with dark teal side rails, continuing
        /// below the judgement line, and a beam rising from the line in each pressed lane. The skin's stage background, lane
        /// backgrounds, receptors and stage foreground are hidden.
        /// </summary>
        [Description("Highway")]
        Highway,

        /// <summary>
        /// The skin's stage rebuilt from its images by the skin porter: lane colours and lines, stage edges and lane lighting
        /// on the track, and its hit target, keys and stage bottom upright at the line, laid out by
        /// <see cref="HighwaySettings.KeyArea"/> and <see cref="HighwaySettings.StageFit"/>. Stable (legacy) skins only; other
        /// skins get <see cref="Highway"/>.
        /// </summary>
        [Description("Skin (ported)")]
        Ported,

        /// <summary>
        /// The skin's stage, lane backgrounds, receptors and lane lighting, drawn on the track with the rest of mania's stage
        /// (warped: magnified near the line).
        /// </summary>
        [Description("Skin (warped)")]
        Skin,
    }

    /// <summary>
    /// How far up the track the highway's beam in a pressed lane reaches (<see cref="HighwayTrackStyle.Highway"/> only;
    /// research/game-djmax.md R2).
    /// </summary>
    public enum KeyBeamLength
    {
        /// <summary>
        /// Half the track's depth (<see cref="UI.HighwayTrack.BEAM_LENGTH"/>): most of the track's height on screen under a
        /// tilted camera.
        /// </summary>
        [Description("Long")]
        Long,

        /// <summary>
        /// Half of the track's height on screen, from the judgement line to where the track ends (the wall or the far edge),
        /// as DJMAX's Half beam.
        /// </summary>
        [Description("Half")]
        Half,

        /// <summary>
        /// A quarter of the track's height on screen, as DJMAX's Quarter beam, for players used to IIDX's short beams.
        /// </summary>
        [Description("Quarter")]
        Quarter,
    }

    /// <summary>
    /// Where a ported skin's key area (everything below its hit position: keys, receptors, stage bottom) goes. Matters most
    /// for skins whose hit position sits well above the bottom of the screen, as IIDX-style skins do.
    /// </summary>
    public enum SkinKeyArea
    {
        /// <summary>
        /// The judgement line is lifted to the skin's hit position (when that is higher than the camera's), and the key area
        /// stands upright below it as a flat panel, the width of the track at the line, as IIDX's key panel does.
        /// </summary>
        [Description("Panel under a lifted line")]
        Panel,

        /// <summary>
        /// The judgement line is lifted as for <see cref="Panel"/>, and the key area lies on the track below it, widening with
        /// the lanes towards the bottom of the screen.
        /// </summary>
        [Description("Floor under a lifted line")]
        Floor,

        /// <summary>
        /// The judgement line stays at the camera's height, and the key area is squeezed into the space below it.
        /// </summary>
        [Description("Squeezed below the line")]
        Squeezed,

        /// <summary>
        /// The judgement line stays at the camera's height and the key area keeps its size, so only its top (the receptors)
        /// fits on screen.
        /// </summary>
        [Description("Receptors only")]
        Receptors,

        /// <summary>
        /// No keys: the line stays at the camera's height with the skin's hit target on it.
        /// </summary>
        [Description("Hidden")]
        Hidden,
    }

    /// <summary>
    /// Where a ported skin's track sits across the screen.
    /// </summary>
    public enum SkinStageFit
    {
        /// <summary>
        /// The camera's track width, centred.
        /// </summary>
        [Description("Camera")]
        Camera,

        /// <summary>
        /// The camera's track width, centred where the skin centres its stage (its <c>ColumnStart</c>), so a stage the skin
        /// puts to one side stays there.
        /// </summary>
        [Description("Skin position")]
        SkinPosition,

        /// <summary>
        /// The skin's own stage width at the judgement line and its position, so narrow, wide and off-centre stages keep their
        /// layout and every lane is as wide at the line as the skin draws it (the default, Alpha 2026-11-04).
        /// </summary>
        [Description("Skin position and width")]
        SkinLayout,
    }

    public enum HighwayPreset
    {
        Chunithm,
        Voltex,
        Parallel,
        Classic,
        Custom,
    }

    /// <summary>
    /// The user-facing highway settings, as one value. Fractions are of the playfield size.
    /// </summary>
    public readonly record struct HighwaySettings
    {
        public HighwayCameraMode CameraMode { get; init; }

        /// <summary>
        /// Far-edge width over judgement-line width (perspective only).
        /// </summary>
        public float FarWidthRatio { get; init; }

        /// <summary>
        /// The judgement line's height above the bottom of the playfield (IIDX "LIFT" is a higher value).
        /// </summary>
        public float JudgementLineHeight { get; init; }

        /// <summary>
        /// The highest <see cref="JudgementLineHeight"/> can be set, and the highest a ported skin may lift the line.
        /// </summary>
        public const float MAX_JUDGEMENT_LINE_HEIGHT = 0.4f;

        /// <summary>
        /// The far edge's distance below the top of the playfield.
        /// </summary>
        public float FarEdgeHeight { get; init; }

        /// <summary>
        /// The track's width at the judgement line.
        /// </summary>
        public float TrackWidth { get; init; }

        /// <summary>
        /// The fraction of the track, from the far edge, hidden behind the wall (Chunithm's field wall, IIDX SUDDEN+).
        /// </summary>
        public float Wall { get; init; }

        /// <summary>
        /// Chunithm's TAP: 2.55% of the visible track (research/game-chunithm.md P5).
        /// </summary>
        public const float DEFAULT_NOTE_THICKNESS = 0.0255f;

        /// <summary>
        /// How thick notes are, in track depth (native notes only). Part of a camera: the same depth looks about twice as
        /// thick near the line under SDVX's camera as under Chunithm's.
        /// </summary>
        public float NoteThickness { get; init; }

        /// <summary>
        /// When true (default), moving the wall keeps the visible time: notes slow down on the track. When false, notes keep
        /// their speed and are simply visible for less time.
        /// </summary>
        public bool WallKeepsVisibleTime { get; init; }

        /// <summary>
        /// The units the wall is set, stepped and shown in (<see cref="Highway.WallScale"/>). Display only: the wall itself is
        /// always <see cref="Wall"/>.
        /// </summary>
        public WallScale WallScale { get; init; }

        /// <summary>
        /// Whether to draw guide lines along the lane boundaries (Chunithm's guidelines). Tilted cameras only.
        /// </summary>
        public bool LaneGuides { get; init; }

        /// <summary>
        /// Whether to keep the skin's hit target at the judgement line instead of the highway's own judgement line. Under a
        /// tilted camera the perspective magnifies the skin's hit target several times near the line. Tilted cameras only.
        /// </summary>
        public bool SkinHitTarget { get; init; }

        /// <summary>
        /// How notes are drawn under a tilted camera. Read when gameplay starts.
        /// </summary>
        public HighwayNoteStyle NoteStyle { get; init; }

        /// <summary>
        /// How hits light up the judgement line under a tilted camera.
        /// </summary>
        public HighwayHitEffectStyle HitEffects { get; init; }

        /// <summary>
        /// What the track itself looks like under a tilted camera: the highway's floor and lane beams, or the skin's stage.
        /// </summary>
        public HighwayTrackStyle Track { get; init; }

        /// <summary>
        /// Where a ported skin's key area goes (<see cref="HighwayTrackStyle.Ported"/> only).
        /// </summary>
        public SkinKeyArea KeyArea { get; init; }

        /// <summary>
        /// Where a ported skin's track sits across the screen (<see cref="HighwayTrackStyle.Ported"/> only).
        /// </summary>
        public SkinStageFit StageFit { get; init; }

        /// <summary>
        /// How far a ported stage the skin puts to one side moves towards the screen's centre, from 0 (exactly where the skin
        /// puts it) to 1 (centred) (<see cref="HighwayTrackStyle.Ported"/> only).
        /// </summary>
        public float SideStagePull { get; init; }

        /// <summary>
        /// The default <see cref="SideStageLean"/>: halfway, so a side stage's far lanes come in towards the middle without the
        /// whole stage leaning as far as a camera centred on the screen would draw it (Alpha 2026-11-04, "rather than a
        /// complete shift").
        /// </summary>
        public const float DEFAULT_SIDE_STAGE_LEAN = 0.5f;

        /// <summary>
        /// How far the far end of a ported stage the skin puts to one side leans towards the screen's centre: where its lanes
        /// converge, from 0 (above the stage, upright) to 1 (above the screen's centre). The judgement line stays put
        /// (research/topic-off-centre-fields.md O2; <see cref="HighwayTrackStyle.Ported"/> only).
        /// </summary>
        public float SideStageLean { get; init; }

        /// <summary>
        /// Whether ported notes keep the skin's proportions (as tall at the judgement line, relative to their lane, as the skin
        /// draws them flat) instead of the camera's <see cref="NoteThickness"/> (<see cref="HighwayNoteStyle.Ported"/> only).
        /// </summary>
        public bool SkinNoteProportions { get; init; }

        /// <summary>
        /// The longest <see cref="FarFade"/>: 30% of the visible track (research/game-yarg-clonehero.md Y3; YARG's default is 25%).
        /// </summary>
        public const float MAX_FAR_FADE = 0.3f;

        /// <summary>
        /// The share of the visible track, down from the far edge or the wall's edge, over which notes fade in (YARG). 0, the
        /// default, shows them at full opacity as soon as they appear, as Chunithm and SDVX do. Cosmetic: it multiplies
        /// mania's own Hidden and Fade In, never replaces them. Notes drawn on the track only (Highway and Skin (ported)).
        /// </summary>
        public float FarFade { get; init; }

        /// <summary>
        /// The faintest <see cref="HoldOpacity"/>: Sekai's Long Note Transparency stops at 10% (research/game-sekai.md K6).
        /// </summary>
        public const float MIN_HOLD_OPACITY = 0.1f;

        /// <summary>
        /// How solid hold bodies are, times the opacity of their state (idle, held, dropped). 1, the default, draws them as
        /// designed. Heads and tails keep their strength. Notes drawn on the track only (Highway and Skin (ported)), as Sekai's
        /// Long Note Transparency (research/game-sekai.md K6).
        /// </summary>
        public float HoldOpacity { get; init; }

        /// <summary>
        /// How solid the lanes' background is, times its own opacity: the highway's floor, or a ported skin's lane colours. 1,
        /// the default, draws it as designed; 0 shows the beatmap's background through the lanes. Rails, lines, beams and
        /// lighting keep their strength (Sekai's Lane Transparency, research/game-sekai.md K9).
        /// </summary>
        public float TrackOpacity { get; init; }

        /// <summary>
        /// How far up the track the beam in a pressed lane reaches (<see cref="HighwayTrackStyle.Highway"/> only). A ported or
        /// warped skin's lane lights keep the skin's length.
        /// </summary>
        public KeyBeamLength KeyBeam { get; init; }

        /// <summary>
        /// How far the far edge swings at <see cref="KiaiSway"/> 100%, as a fraction of the screen width.
        /// </summary>
        public const float MAX_KIAI_SWAY = 0.03f;

        /// <summary>
        /// How strongly the track sways with the beat in kiai sections (<see cref="Highway.KiaiSway"/>), from 0 (off, the
        /// default) to 1 (the far edge swinging <see cref="MAX_KIAI_SWAY"/> of the screen width either way). Cosmetic; the
        /// judgement line never moves (SPEC 2). Tilted cameras only.
        /// </summary>
        public float KiaiSway { get; init; }

        public HighwaySettings()
        {
            CameraMode = HighwayCameraMode.Perspective;
            FarWidthRatio = 0.5f;
            JudgementLineHeight = 0.12f;
            FarEdgeHeight = 0;
            TrackWidth = 0.5f;
            Wall = 0;
            NoteThickness = DEFAULT_NOTE_THICKNESS;
            WallKeepsVisibleTime = true;
            LaneGuides = true;
            SkinHitTarget = false;
            NoteStyle = HighwayNoteStyle.Ported;
            HitEffects = HighwayHitEffectStyle.Ported;
            Track = HighwayTrackStyle.Ported;
            KeyArea = SkinKeyArea.Panel;
            StageFit = SkinStageFit.SkinLayout;
            SideStagePull = 0;
            SideStageLean = DEFAULT_SIDE_STAGE_LEAN;
            SkinNoteProportions = true;
            FarFade = 0;
            HoldOpacity = 1;
            TrackOpacity = 1;
            KeyBeam = KeyBeamLength.Long;
            KiaiSway = 0;
            WallScale = WallScale.Percent;
        }

        /// <summary>
        /// Where the track sits on screen. Not meaningful for <see cref="HighwayCameraMode.Flat"/>, which keeps mania's layout.
        /// </summary>
        public HighwayLayout ToLayout() => new HighwayLayout
        {
            JudgementY = 1 - JudgementLineHeight,
            FarY = FarEdgeHeight,
            NearWidth = TrackWidth,
            FarWidthRatio = CameraMode == HighwayCameraMode.Perspective ? FarWidthRatio : 1,
            CentreX = 0.5f,
        };

        /// <summary>
        /// How time maps onto the track, given the wall.
        /// </summary>
        public HighwayScroll ToScroll() => new HighwayScroll(Wall, WallKeepsVisibleTime);
    }

    /// <summary>
    /// Named starting points for the highway settings. Values marked (tune) are placeholders until the research notes in
    /// the osu fork's docs/ortho/research/ give measured values; every change is recorded in its DECISIONS.md.
    /// </summary>
    public static class HighwayPresets
    {
        public const HighwayPreset DEFAULT = HighwayPreset.Chunithm;

        public static HighwaySettings Get(HighwayPreset preset)
        {
            switch (preset)
            {
                case HighwayPreset.Chunithm:
                    // Measured from the arcade (research/game-chunithm.md): judgement line 7.4% above the bottom, track
                    // 0.61 of the screen wide there, far/near width 0.24. The arcade's far edge sits 16% below the top,
                    // under its HUD frame; here it is stretched to the top edge so notes enter from the top (SPEC 2,
                    // QUESTIONS Q7). Same width for every key count, as in the arcade.
                    return new HighwaySettings
                    {
                        CameraMode = HighwayCameraMode.Perspective,
                        FarWidthRatio = 0.24f,
                        JudgementLineHeight = 0.074f,
                        FarEdgeHeight = 0,
                        TrackWidth = 0.61f,
                    };

                case HighwayPreset.Voltex:
                    // The default camera of the open-source SDVX clone USC in a landscape window, projected
                    // (research/game-sdvx.md P1; no arcade frame could be measured): judgement line 5.9% above the bottom,
                    // highway 0.435 of the screen wide there, far/near width 0.106. Its far edge sits 28% below the top;
                    // here it is stretched to the top edge, as for Chunithm (QUESTIONS Q7). Notes 1.25% of the track
                    // deep, USC's note depth: at 2.55% this camera draws them twice Chunithm's height near the line.
                    return new HighwaySettings
                    {
                        CameraMode = HighwayCameraMode.Perspective,
                        FarWidthRatio = 0.106f,
                        JudgementLineHeight = 0.059f,
                        FarEdgeHeight = 0,
                        TrackWidth = 0.435f,
                        NoteThickness = 0.0125f,
                    };

                case HighwayPreset.Parallel:
                    return new HighwaySettings
                    {
                        CameraMode = HighwayCameraMode.ParallelTilt,
                        FarWidthRatio = 1,
                        JudgementLineHeight = 0.12f,
                        FarEdgeHeight = 0,
                        TrackWidth = 0.45f,
                    };

                case HighwayPreset.Classic:
                    return new HighwaySettings { CameraMode = HighwayCameraMode.Flat };

                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, "Custom has no fixed values.");
            }
        }
    }
}
