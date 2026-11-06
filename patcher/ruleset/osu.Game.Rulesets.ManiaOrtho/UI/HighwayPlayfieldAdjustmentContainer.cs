// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.SkinPort;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.ManiaOrtho.UI
{
    /// <summary>
    /// Holds mania's own playfield layout inside a <see cref="HighwayWarpContainer"/>, keeps the warp in step with the
    /// highway settings and the stage's position, and offers a screen-space <see cref="Overlay"/> for things that must not
    /// tilt (SPEC section 7).
    /// </summary>
    public partial class HighwayPlayfieldAdjustmentContainer : PlayfieldAdjustmentContainer
    {
        /// <summary>
        /// Limits on how far the stage is stretched sideways inside the buffer.
        /// </summary>
        private const float min_stage_scale = 0.25f;

        private const float max_stage_scale = 4;

        protected override Container<Drawable> Content => mania;

        /// <summary>
        /// Drawn on top of the highway, untilted, in the same space as the highway.
        /// </summary>
        public Container Overlay { get; }

        public HighwayWarpContainer Warp { get; }

        /// <summary>
        /// The highway's own floor, rails and lane beams, under the warped stage. Shown under a tilted camera with
        /// <see cref="HighwayTrackStyle.Highway"/>.
        /// </summary>
        public HighwayTrack Track { get; }

        /// <summary>
        /// Whether the highway's track is shown this frame, and the skin's stage parts (<see cref="SkinStageParts"/>) hidden.
        /// </summary>
        public bool TrackShown { get; private set; }

        /// <summary>
        /// The skin's stage rebuilt from its images (<see cref="HighwayTrackStyle.Ported"/>), under the warped stage.
        /// </summary>
        public PortedTrack PortedTrack { get; }

        /// <summary>
        /// The ported skin's keys and stage bottom, in <see cref="Overlay"/> over the notes (or under them, as the skin asks).
        /// </summary>
        public PortedKeyArea PortedKeys { get; }

        /// <summary>
        /// The ported skin's hit and hold lighting, in <see cref="Overlay"/> over the notes.
        /// </summary>
        public PortedLighting PortedLighting { get; private set; } = null!;

        /// <summary>
        /// The skin's mania images and layout, read by the skin porter, or null when the skin is not a stable (legacy) skin
        /// osu!mania draws with its legacy pieces. Rebuilt when the skin changes.
        /// </summary>
        public PortedSkin? Port { get; private set; }

        /// <summary>
        /// Whether the ported stage is shown this frame (<see cref="HighwayTrackStyle.Ported"/> with a legacy skin).
        /// </summary>
        public bool PortedTrackShown { get; private set; }

        /// <summary>
        /// Whether ported notes are drawn this frame (<see cref="HighwayNoteStyle.Ported"/> with a legacy skin).
        /// </summary>
        public bool PortedNotesShown { get; private set; }

        /// <summary>
        /// Whether the ported hit lighting is shown this frame (<see cref="HighwayHitEffectStyle.Ported"/> with a legacy skin).
        /// </summary>
        public bool PortedLightingShown { get; private set; }

        /// <summary>
        /// The skin's stage as osu!mania lays it out flat, measured last frame.
        /// </summary>
        public FlatStage FlatStage { get; private set; }

        /// <summary>
        /// Where the porter put the skin's stage this frame.
        /// </summary>
        public PortPlacement PortPlacement { get; private set; }

        private PortedNoteShapes? portedNotes;

        /// <summary>
        /// Frames left in which to look at the skin again. A skin change rebuilds mania's pieces, and so the port.
        /// </summary>
        private int portSearches = 1;

        /// <summary>
        /// Whether the port has a hit target of its own (a stage hint or judgement line) to mark the line with.
        /// </summary>
        private bool portHasHitTarget;

        /// <summary>
        /// Whether the port draws a judgement line of its own (<c>JudgementLine: 1</c>), not only a stage hint.
        /// </summary>
        private bool portHasJudgementLine;

        /// <summary>
        /// Each column's top-level container (mania's legacy hold lighting and the explosion proxy), hidden while the ported
        /// lighting replaces them.
        /// </summary>
        private readonly List<Drawable> columnTopLevels = new List<Drawable>();

        private bool columnTopLevelsHidden;

        /// <summary>
        /// The skin's stage background and foreground for each stage, and the lane background and key area (receptor) for
        /// each column: hidden while <see cref="TrackShown"/>. Found once the playfield has loaded (see
        /// <see cref="findSkinStageParts"/>).
        /// </summary>
        public IReadOnlyList<Drawable> SkinStageParts => skinStageParts;

        private readonly List<Drawable> skinStageParts = new List<Drawable>();

        private bool skinStagePartsFound;

        /// <summary>
        /// Whether <see cref="SkinStageParts"/> are hidden.
        /// </summary>
        private bool skinStagePartsHidden;

        /// <summary>
        /// Every column of every stage, in playfield column order.
        /// </summary>
        private Column[] playfieldColumns = Array.Empty<Column>();

        private bool[] pressedColumns = Array.Empty<bool>();
        private bool[] holdingColumns = Array.Empty<bool>();

        /// <summary>
        /// Whether one of the skin's hit targets is drawn inside one of its stage parts (legacy skins with key textures),
        /// so that hiding the stage hides the skin's judgement line too.
        /// </summary>
        private bool hitTargetInSkinStage;

        /// <summary>
        /// The field wall, in <see cref="Overlay"/> under the judgements.
        /// </summary>
        public HighwayWall Wall { get; }

        /// <summary>
        /// The lane guides and judgement line, in <see cref="Overlay"/> under the wall.
        /// </summary>
        public HighwayGuides Guides { get; }

        /// <summary>
        /// Each lane's piece of the judgement line lit after a hit (with the highway's hit effects), in <see cref="Overlay"/>
        /// over the guides and under the notes.
        /// </summary>
        public HighwaySlotLights SlotLights { get; }

        /// <summary>
        /// Notes drawn natively on the track, in <see cref="Overlay"/> over the guides and under the wall. Only used with
        /// <see cref="HighwayNoteStyle.Highway"/>.
        /// </summary>
        public HighwayNoteLayer NoteLayer { get; }

        /// <summary>
        /// Whether this play draws notes natively (<see cref="HighwayNoteStyle.Highway"/>), fixed when gameplay loads.
        /// </summary>
        public bool NativeNotes { get; private set; }

        /// <summary>
        /// When notes are drawn natively: one holder per column with a proxy of its hit object container, so mania's own
        /// notes are not drawn in the stage. Each holder sits right behind the container it proxies, in the same parent, so
        /// under the flat camera (the only time holders are shown) the notes are drawn exactly where mania draws them: under
        /// a cover mod's cover, inside the column's masking, under the key area and explosions.
        /// </summary>
        public IReadOnlyList<Drawable> NoteProxyHolders => noteProxyHolders;

        private readonly List<Drawable> noteProxyHolders = new List<Drawable>();

        /// <summary>
        /// Holds proxies whose container is not in a <see cref="Container{T}"/> (none known; a later mod might do it).
        /// </summary>
        private readonly Container fallbackNoteProxies;

        private bool noteProxiesCreated;

        /// <summary>
        /// Each column's cover from a cover mod (Hidden, Fade In, Cover), in playfield column order, or empty without one.
        /// </summary>
        private CoverBoxes?[] coverBoxes = Array.Empty<CoverBoxes?>();

        /// <summary>
        /// Each column's cover in track depth this frame, in playfield column order. Empty without a cover mod.
        /// </summary>
        public ReadOnlySpan<DepthCover> ColumnCovers => columnCovers;

        private DepthCover[] columnCovers = Array.Empty<DepthCover>();

        /// <summary>
        /// Each column's left and right edge in lane space, in playfield column order.
        /// </summary>
        public ReadOnlySpan<Vector2> ColumnLanes => columnLanes;

        private Vector2[] columnLanes = Array.Empty<Vector2>();

        /// <summary>
        /// Each stage's left and right edge in lane space.
        /// </summary>
        private Vector2[] stageLanes = Array.Empty<Vector2>();

        [Resolved]
        private IScrollingInfo scrollingInfo { get; set; } = null!;

        /// <summary>
        /// The skin's hit targets: one wrapper per column, plus legacy skins' own (see <see cref="updateHitTargets"/>).
        /// </summary>
        public IReadOnlyList<Drawable> HitTargets => hitTargets;

        private readonly List<Drawable> hitTargets = new List<Drawable>();

        /// <summary>
        /// Frames left in which to look for hit targets again. A skin change rebuilds them.
        /// </summary>
        private int hitTargetSearches = 1;

        private ISkinSource? skinSource;

        /// <summary>
        /// Each stage's special column, for an N+1 map (<see cref="LaneGroups.SpecialSide"/>): read with the skin.
        /// </summary>
        public IReadOnlyList<SpecialColumnSide> SpecialSides => specialSides;

        private SpecialColumnSide[] specialSides = Array.Empty<SpecialColumnSide>();

        /// <summary>
        /// One upright judgement display per stage, in <see cref="Overlay"/>.
        /// </summary>
        public IReadOnlyList<HighwayJudgementLayer> JudgementLayers => judgementLayers;

        private readonly List<HighwayJudgementLayer> judgementLayers = new List<HighwayJudgementLayer>();

        /// <summary>
        /// Hit bursts and hold glows at the judgement line, in <see cref="Overlay"/> over the notes and under the judgements.
        /// </summary>
        public HighwayHitEffectLayer HitEffects { get; private set; } = null!;

        /// <summary>
        /// The current highway settings, refreshed every frame.
        /// </summary>
        public HighwaySettings Settings { get; private set; } = new HighwaySettings();

        /// <summary>
        /// The current projection in this container's local space.
        /// </summary>
        public HighwayProjection Projection { get; private set; }

        private readonly DrawableManiaOrthoRuleset drawableRuleset;

        private KiaiSway? kiaiSway;
        private readonly ManiaPlayfieldAdjustmentContainer mania;

        private HighwayConfigManager? config;

        /// <summary>
        /// How close (in track depth) a cover's edge must be to an end of the column to count as reaching it.
        /// </summary>
        private const float cover_edge_tolerance = 1e-3f;

        /// <summary>
        /// Rows far above the stage map towards the vanishing point; past this depth they are all drawn there.
        /// </summary>
        private const float max_mapped_depth = 1000;

        private readonly Bindable<bool> timingBasedColouring = new Bindable<bool>();

        public HighwayPlayfieldAdjustmentContainer(DrawableManiaOrthoRuleset drawableRuleset)
        {
            this.drawableRuleset = drawableRuleset;

            InternalChildren = new Drawable[]
            {
                // Under the warped stage: the stage draws its notes, bar lines and effects over the floor.
                Track = new HighwayTrack(),
                PortedTrack = new PortedTrack { Alpha = 0 },
                Warp = new HighwayWarpContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        mania = new ManiaPlayfieldAdjustmentContainer(drawableRuleset)
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                        },
                        fallbackNoteProxies = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                        },
                    },
                },
                Overlay = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        Guides = new HighwayGuides(),
                        SlotLights = new HighwaySlotLights { Alpha = 0 },
                        NoteLayer = new HighwayNoteLayer { Alpha = 0 },
                        PortedKeys = new PortedKeyArea { Alpha = 0 },
                        Wall = new HighwayWall(),
                    },
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load(ISkinSource skinSource, ManiaRulesetConfigManager? maniaConfig)
        {
            // The drawable ruleset's dependencies (and so its config) are set up before its children load.
            config = drawableRuleset.HighwayConfig;

            this.skinSource = skinSource;
            skinSource.SourceChanged += onSkinSourceChanged;

            // Highway and ported notes are both drawn by the note layer; a non-legacy skin's ported notes are highway notes.
            NativeNotes = config?.Current.NoteStyle != HighwayNoteStyle.Skin;

            maniaConfig?.BindWith(ManiaRulesetSetting.TimingBasedNoteColouring, timingBasedColouring);

            // The playfield (and so its stages) is created with this container, before it loads.
            Overlay.Add(HitEffects = new HighwayHitEffectLayer(drawableRuleset.Playfield, drawableRuleset.Beatmap.ControlPointInfo));
            kiaiSway = new KiaiSway(drawableRuleset.Beatmap.ControlPointInfo);
            Overlay.Add(PortedLighting = new PortedLighting(drawableRuleset.Playfield) { Alpha = 0 });

            foreach (var stage in drawableRuleset.Playfield.Stages)
            {
                var layer = new HighwayJudgementLayer(stage);
                judgementLayers.Add(layer);
                Overlay.Add(layer);
            }
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (config != null)
                Settings = config.Current;

            bool flat = Settings.CameraMode == HighwayCameraMode.Flat;
            bool loaded = drawableRuleset.IsLoaded && drawableRuleset.Playfield.Stages.Count > 0;

            if (loaded)
                updatePort();

            PortedTrackShown = !flat && Port != null && Settings.Track == HighwayTrackStyle.Ported;
            PortedNotesShown = !flat && Port != null && NativeNotes && Settings.NoteStyle == HighwayNoteStyle.Ported;
            PortedLightingShown = !flat && Port != null && Settings.HitEffects == HighwayHitEffectStyle.Ported;

            var layout = Settings.ToLayout();

            // The skin's layout moves the camera's track: its line lifted for the key area, its stage's position and width.
            if (PortedTrackShown && Port != null)
                layout = SkinPortLayout.Adjust(layout, Settings.KeyArea, Settings.StageFit, FlatStage, Port.Stages.Length > 0 ? Port.Stages[0].ColumnStart : null,
                    pull: Settings.SideStagePull, lean: Settings.SideStageLean);

            // Cosmetic only, and a function of the (frame-stable) time, so a seek shows the same pose as playing through.
            if (!flat && Settings.KiaiSway > 0 && kiaiSway != null)
                layout = layout with { Sway = Settings.KiaiSway * HighwaySettings.MAX_KIAI_SWAY * kiaiSway.At(Time.Current) };

            Projection = new HighwayProjection(layout, DrawSize);

            Warp.Warp = !flat;
            Warp.Layout = Projection.Layout;
            Warp.Scroll = Settings.ToScroll();
            Wall.Place(Projection, Warp.Scroll, flat);

            Guides.Layout = Projection.Layout;
            Guides.Alpha = flat ? 0 : 1;
            // A ported skin draws its own column lines and hit target.
            Guides.ShowLaneLines = Settings.LaneGuides && !PortedTrackShown;
            // When the skin's own line went with its hidden stage, keep the highway's, so the track always has one. Skins
            // often draw their receptor line in the key images (Minty: a blank stage hint, no judgement line), so a hidden
            // key area keeps the highway's line unless the skin draws a judgement line of its own.
            Guides.ShowJudgementLine = PortedTrackShown
                ? Settings.KeyArea == SkinKeyArea.Hidden ? !portHasJudgementLine : !portHasHitTarget
                : !Settings.SkinHitTarget || (TrackShown && hitTargetInSkinStage);

            Track.Layout = Projection.Layout;

            if (!loaded)
            {
                Track.Alpha = 0;
                return;
            }

            // Ported styles with a skin the porter cannot read fall back to the highway's own.
            bool highwayTrack = !flat && (Settings.Track == HighwayTrackStyle.Highway || (Settings.Track == HighwayTrackStyle.Ported && Port == null));
            bool highwayEffects = !flat && (Settings.HitEffects == HighwayHitEffectStyle.Highway || (Settings.HitEffects == HighwayHitEffectStyle.Ported && Port == null));

            updateTrack(highwayTrack, highwayTrack || PortedTrackShown);
            updateHitTargets(flat || (Settings.SkinHitTarget && !PortedTrackShown));

            if (NativeNotes && !noteProxiesCreated)
            {
                noteProxiesCreated = true;
                createNoteProxies();
            }

            for (int i = 0; i < noteProxyHolders.Count; i++)
                noteProxyHolders[i].Alpha = flat ? 1 : 0;

            NoteLayer.Alpha = NativeNotes && !flat ? 1 : 0;

            RectangleF stage = stageBounds();

            // Render the stage in the buffer at about the width it will have at the judgement line, so the near end is
            // not magnified (blurry). The far end is minified instead (see DECISIONS on far-end shimmer).
            float currentScale = mania.Scale.X;
            float targetScale = 1;

            if (!flat && stage.Width > 0)
            {
                float unscaledWidth = stage.Width / currentScale;
                targetScale = Math.Clamp(Projection.WidthAt(0) / unscaledWidth, min_stage_scale, max_stage_scale);
            }

            if (Math.Abs(targetScale - currentScale) > 1e-3f)
            {
                // Measure again at the new stretch, so this frame is not drawn from a stale stage rectangle.
                mania.Scale = new Vector2(targetScale, 1);
                stage = stageBounds();
            }

            Warp.Source = stage;

            updateGuideLines(stage);

            Track.SetLanes(stageLanes, columnLanes);
            Wall.SetStages(stageLanes);
            Track.FarDepth = Warp.Scroll.AppearDepth;
            Track.FloorOpacity = Settings.TrackOpacity;
            Track.KeyBeam = Settings.KeyBeam;
            PortedTrack.BackgroundOpacity = Settings.TrackOpacity;
            updateBeams();

            FlatStage = measureFlatStage(stage);
            PortPlacement = new PortPlacement(Projection, FlatStage, Settings.KeyArea, Warp.Scroll.ScrollLength, Warp.Scroll.AppearDepth);

            PortedTrack.Alpha = PortedTrackShown ? 1 : 0;
            PortedKeys.Alpha = PortedTrackShown ? 1 : 0;

            if (PortedTrackShown)
            {
                PortedTrack.Place(PortPlacement, stageLanes, columnLanes, pressedColumns);
                PortedKeys.Place(PortPlacement, stageLanes, columnLanes, pressedColumns);
            }
            else
            {
                // Presses and releases while hidden are not seen: start clean when shown again.
                PortedTrack.ForgetPresses();
                PortedKeys.ForgetPresses();
            }

            if (portedNotes != null && PortedNotesShown)
                portedNotes.Update(Projection, columnLanes, FlatStage, Settings.SkinNoteProportions, Settings.NoteThickness);

            if (NativeNotes && !flat)
            {
                NoteLayer.Layout = Projection.Layout;
                NoteLayer.NoteThickness = Settings.NoteThickness;
                NoteLayer.SnapColours = timingBasedColouring.Value;
                NoteLayer.Ported = PortedNotesShown ? portedNotes : null;
                NoteLayer.FarFade = DepthCover.FarFade(Warp.Scroll.AppearDepth, Settings.FarFade);
                NoteLayer.HoldOpacity = Settings.HoldOpacity;
                measureCovers(stage);
                // Plain bars meet the lane lines when they are drawn.
                var laneLines = Guides.ShowLaneLines && Guides.Alpha > 0 ? Guides.Lines : ReadOnlySpan<HighwayGuides.GuideLine>.Empty;
                NoteLayer.Gather(drawableRuleset.Playfield, columnLanes, columnCovers, laneLines, scrollingInfo.Algorithm.Value, scrollingInfo.TimeRange.Value, Warp.Scroll,
                    Time.Current);
            }

            HitEffects.Place(Projection, columnLanes, highwayEffects, highwayEffects || PortedLightingShown);

            SlotLights.Layout = Projection.Layout;
            SlotLights.Alpha = highwayEffects ? 1 : 0;
            SlotLights.SetLights(columnLanes, HitEffects.SlotLights);

            PortedLighting.Active = PortedLightingShown;
            PortedLighting.Alpha = PortedLightingShown ? 1 : 0;

            if (PortedLightingShown)
                PortedLighting.Place(PortPlacement, columnLanes);
            else
                PortedLighting.ForgetHits();

            hideColumnTopLevels(PortedLightingShown);

            for (int i = 0; i < judgementLayers.Count; i++)
                judgementLayers[i].Place(Projection, flat, stage, mania.Scale.X);
        }

        /// <summary>
        /// Mania's notes keep judging in the stage; proxying their containers away only stops them being drawn there. Runs
        /// after mods have been applied, so a cover mod's wrapper is already in place.
        /// </summary>
        private void createNoteProxies()
        {
            var stages = drawableRuleset.Playfield.Stages;
            var covers = new List<CoverBoxes?>();

            for (int i = 0; i < stages.Count; i++)
            {
                foreach (var column in stages[i].Columns)
                {
                    var hitObjects = column.HitObjectContainer;
                    covers.Add(findCover(hitObjects));

                    var holder = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        Child = hitObjects.CreateProxy(),
                    };

                    if (hitObjects.Parent is Container<Drawable> parent)
                    {
                        // Drawn just before the container itself, which draws nothing now: before anything added after it,
                        // such as a cover.
                        holder.Depth = MathF.BitIncrement(hitObjects.Depth);
                        parent.Add(holder);
                    }
                    else
                        fallbackNoteProxies.Add(holder);

                    noteProxyHolders.Add(holder);
                }
            }

            if (covers.Any(c => c != null))
            {
                coverBoxes = covers.ToArray();
                columnCovers = new DepthCover[coverBoxes.Length];
            }
        }

        /// <summary>
        /// The two boxes of a <see cref="PlayfieldCoveringWrapper"/>'s cover: the gradient (opaque at its own bottom edge) and
        /// the fully covered part.
        /// </summary>
        private readonly record struct CoverBoxes(Drawable Gradient, Drawable Filled);

        /// <summary>
        /// Mania's cover mods wrap each column's notes in a <see cref="PlayfieldCoveringWrapper"/>: a buffer holding the notes
        /// and the cover, a container of a gradient box and a filled box that the wrapper sizes every frame. Reading the drawn
        /// boxes keeps native notes covered exactly as mania covers its own (coverage changes with combo, eases in, legacy
        /// hit positions, direction), without copying any of that.
        /// </summary>
        private static CoverBoxes? findCover(Drawable hitObjects)
        {
            if (hitObjects.Parent is not Container<Drawable> buffer || buffer.Parent is not PlayfieldCoveringWrapper)
                return null;

            foreach (var child in buffer.Children)
            {
                if (child is not Container<Drawable> cover || cover.Children.Count != 2 || !cover.Children.All(c => c is Box))
                    continue;

                var gradient = cover.Children.FirstOrDefault(c => !c.Colour.TryExtractSingleColour(out _));
                var filled = cover.Children.FirstOrDefault(c => c.Colour.TryExtractSingleColour(out _));

                if (gradient != null && filled != null)
                    return new CoverBoxes(gradient, filled);
            }

            return null;
        }

        /// <summary>
        /// The track depth the warp gives the flat stage's row at <paramref name="flatY"/> (this container's space; mania's
        /// stage is laid out flat in it before the warp): 0 at the hit position, the scroll length at the top of the
        /// scrolling area, linearly in between, so notes keep mania's timing. Negative below the line.
        /// </summary>
        public float DepthAtFlatY(float flatY)
        {
            var source = Warp.Source;
            return source.Height > 0 ? (source.Bottom - flatY) / source.Height * Warp.Scroll.ScrollLength : 0;
        }

        /// <summary>
        /// Where the flat stage's row at <paramref name="flatY"/> is drawn, in this container's space: on the track at its
        /// depth above the judgement line, at its flat height below it (as the warp draws the receptor area), unchanged under
        /// the flat camera. Never decreases as <paramref name="flatY"/> grows.
        /// </summary>
        public float DrawnYOfFlatY(float flatY)
        {
            var source = Warp.Source;

            if (!Warp.Warp || source.Height <= 0)
                return flatY;

            if (flatY > source.Bottom)
                return Projection.YAt(0) + (flatY - source.Bottom);

            return Projection.YAt(Math.Min(DepthAtFlatY(flatY), max_mapped_depth));
        }

        /// <summary>
        /// Converts each column's cover, as mania draws it in the flat stage, to track depth through the warp's mapping (the
        /// stage's rows map linearly to depth 0 at the hit position up to the scroll length at the top).
        /// </summary>
        private void measureCovers(RectangleF source)
        {
            if (source.Height <= 0)
                return;

            float scrollLength = Warp.Scroll.ScrollLength;

            for (int i = 0; i < coverBoxes.Length; i++)
            {
                if (coverBoxes[i] is not CoverBoxes boxes)
                {
                    columnCovers[i] = DepthCover.None;
                    continue;
                }

                float coveredFrom = 0, coveredTo = 0;

                if (boxes.Filled.IsPresent && boxes.Filled.DrawHeight > 0)
                {
                    var quad = boxes.Filled.ScreenSpaceDrawQuad;
                    float a = DepthAtFlatY(Warp.ToLocalSpace(quad.TopLeft).Y);
                    float b = DepthAtFlatY(Warp.ToLocalSpace(quad.BottomLeft).Y);

                    coveredFrom = Math.Min(a, b);
                    coveredTo = Math.Max(a, b);

                    // A cover reaching an end of the column also covers what is past that end: mania's column masks it.
                    if (coveredFrom <= cover_edge_tolerance)
                        coveredFrom = float.MinValue;
                    if (coveredTo >= scrollLength - cover_edge_tolerance)
                        coveredTo = float.MaxValue;
                }

                float fadeFrom = 0, fadeTo = 0, fadeAlpha = 0;

                if (boxes.Gradient.IsPresent && boxes.Gradient.DrawHeight > 0)
                {
                    var gradient = boxes.Gradient;

                    fadeFrom = DepthAtFlatY(Warp.ToLocalSpace(gradient.ToScreenSpace(new Vector2(0, gradient.DrawHeight))).Y);
                    fadeTo = DepthAtFlatY(Warp.ToLocalSpace(gradient.ToScreenSpace(Vector2.Zero)).Y);
                    fadeAlpha = gradient.Alpha;
                }

                columnCovers[i] = new DepthCover(coveredFrom, coveredTo, fadeFrom, fadeTo, fadeAlpha);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (skinSource != null)
                skinSource.SourceChanged -= onSkinSourceChanged;

            base.Dispose(isDisposing);
        }

        // Skinnable drawables rebuild in a scheduled call on the same event; look again after they have (this frame and the
        // next, in case the rebuild lands a frame later).
        private void onSkinSourceChanged() => Schedule(() =>
        {
            hitTargetSearches = 2;
            portSearches = 2;
        });

        /// <summary>
        /// Shows or hides the skin's hit targets. Under a tilted camera the warp magnifies them several times near the line
        /// (they sit just above it in the flat stage), so the highway draws its own judgement line instead by default.
        /// </summary>
        /// <remarks>
        /// Each column has a skinnable hit target, the only skinnable drawable directly inside its hit object area. Legacy
        /// skins with key textures leave that empty and draw a <see cref="LegacyHitTarget"/> (stage hint and judgement
        /// line) inside their stage background instead, which is rebuilt when the skin changes.
        /// </remarks>
        private void updateHitTargets(bool show)
        {
            if (hitTargetSearches > 0)
            {
                hitTargetSearches--;
                hitTargets.Clear();

                var stages = drawableRuleset.Playfield.Stages;

                for (int i = 0; i < stages.Count; i++)
                {
                    foreach (var column in stages[i].Columns)
                    {
                        foreach (var skinnable in column.HitObjectArea.ChildrenOfType<SkinnableDrawable>())
                        {
                            if (skinnable.Parent == column.HitObjectArea)
                                hitTargets.Add(skinnable);
                        }
                    }

                    hitTargets.AddRange(stages[i].ChildrenOfType<LegacyHitTarget>());
                }

                hitTargetInSkinStage = false;

                for (int i = 0; i < hitTargets.Count; i++)
                    hitTargetInSkinStage |= isInSkinStage(hitTargets[i]);
            }

            float alpha = show ? 1 : 0;

            for (int i = 0; i < hitTargets.Count; i++)
                hitTargets[i].Alpha = alpha;
        }

        /// <summary>
        /// Shows the highway's track and hides the skin's stage parts, or the other way round.
        /// </summary>
        /// <remarks>
        /// Hidden parts stay present (<see cref="Drawable.AlwaysPresent"/>), so they keep updating and stay in the key binding
        /// container's input queue exactly as in mania: they rebuild when the skin changes, their lighting follows the keys,
        /// and showing them again shows them current. Every one of them in mania only lights the skin and never handles a
        /// press (they return false). Replay parity runs with the track.
        /// </remarks>
        private void updateTrack(bool show, bool hideSkinStage)
        {
            if (!skinStagePartsFound)
            {
                skinStagePartsFound = true;
                findSkinStageParts();
            }

            TrackShown = show;
            Track.Alpha = show ? 1 : 0;

            if (skinStagePartsHidden == hideSkinStage)
                return;

            skinStagePartsHidden = hideSkinStage;

            for (int i = 0; i < skinStageParts.Count; i++)
                skinStageParts[i].Alpha = hideSkinStage ? 0 : 1;
        }

        /// <summary>
        /// Finds the wrappers mania puts the skin's stage parts in. They stay the same drawables when the skin changes (only
        /// their content is rebuilt), so this runs once. Mania builds them in fixed places, which is how they are told apart:
        /// <list type="bullet">
        /// <item>the stage background and foreground sit in the container that is the stage's only content (<c>Stage</c>);</item>
        /// <item>a column's key area is the column's own child, and its lane background is the only skinnable drawable in its
        /// background container, the column's other child holding one (<c>Column</c>). The hit object area's hit target is
        /// left to <see cref="updateHitTargets"/>.</item>
        /// </list>
        /// </summary>
        private void findSkinStageParts()
        {
            skinStageParts.Clear();

            var stages = drawableRuleset.Playfield.Stages;
            var allColumns = new List<Column>();

            for (int i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];

                foreach (var skinnable in stage.ChildrenOfType<SkinnableDrawable>())
                {
                    if (skinnable.Parent?.Parent == stage)
                        addSkinStagePart(skinnable);
                }

                foreach (var column in stage.Columns)
                {
                    allColumns.Add(column);

                    // The column's top-level container: a plain container that is the column's own child and holds no skinnable
                    // drawable (unlike its background container). Mania's legacy hold lighting is added to it while held.
                    foreach (var container in column.ChildrenOfType<Container>())
                    {
                        if (container.Parent == column && container.GetType() == typeof(Container) && !container.Children.Any(c => c is SkinnableDrawable))
                        {
                            container.AlwaysPresent = true;
                            columnTopLevels.Add(container);
                        }
                    }

                    foreach (var skinnable in column.ChildrenOfType<SkinnableDrawable>())
                    {
                        var parent = skinnable.Parent;

                        if (parent == column || (parent != column.HitObjectArea && parent?.Parent == column))
                            addSkinStagePart(skinnable);
                    }
                }
            }

            playfieldColumns = allColumns.ToArray();
            pressedColumns = new bool[playfieldColumns.Length];
            holdingColumns = new bool[playfieldColumns.Length];
        }

        private void addSkinStagePart(Drawable part)
        {
            // Hidden with alpha alone, a part would stop updating: it would miss skin changes and key releases (and so
            // show a stale skin or a stuck light when shown again) and leave the input queue.
            part.AlwaysPresent = true;
            skinStageParts.Add(part);
        }

        private bool isInSkinStage(Drawable drawable)
        {
            for (var d = drawable.Parent; d != null; d = d.Parent)
            {
                if (skinStageParts.Contains(d))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Reads the skin with the porter when it has changed: a skin osu!mania draws with its legacy pieces (a stable skin with
        /// mania images) is ported, any other is not (<see cref="Port"/> null, and ported styles fall back to the highway's).
        /// </summary>
        private void updatePort()
        {
            if (portSearches <= 0 || skinSource == null)
                return;

            portSearches--;

            var playfield = drawableRuleset.Playfield;
            updateSpecialSides(playfield);

            PortedSkin? port = null;

            try
            {
                if (playfield.Stages[0].ChildrenOfType<LegacyStageBackground>().Any())
                    port = PortedSkin.Create(skinSource, playfield);
            }
            catch (Exception e)
            {
                // Never break gameplay over a skin: fall back to the highway's own look.
                Logger.Error(e, "The skin porter could not read this skin; using the highway's own track and notes.");
            }

            Port = port;
            portedNotes = port != null ? new PortedNoteShapes(port) : null;
            portHasHitTarget = port != null && port.Stages.Any(st => st.Hint != null || st.ShowJudgementLine);
            portHasJudgementLine = port != null && port.Stages.Any(st => st.ShowJudgementLine);

            PortedTrack.Skin = port;
            PortedKeys.Skin = port;
            PortedLighting.Skin = port;

            // Keys over the notes, unless the skin draws them under (KeysUnderNotes): then behind everything in the overlay,
            // which is still over the track.
            bool keysUnderNotes = port != null && port.Columns.Length > 0 && port.Columns[0].KeysUnderNotes;
            Overlay.ChangeChildDepth(PortedKeys, keysUnderNotes ? 1 : 0);
        }

        /// <summary>
        /// The skin's stage as mania lays it out flat this frame: its hit position, scrolling area, width before the highway
        /// stretched it, and mania's unit size.
        /// </summary>
        private FlatStage measureFlatStage(RectangleF stage)
        {
            if (playfieldColumns.Length == 0 || stage.Width <= 0)
                return default;

            var column = playfieldColumns[0];

            if (column.DrawHeight <= 0)
                return default;

            float top = Warp.ToLocalSpace(column.ToScreenSpace(Vector2.Zero)).Y;
            float bottom = Warp.ToLocalSpace(column.ToScreenSpace(new Vector2(0, column.DrawHeight))).Y;
            float unitSize = (bottom - top) / column.DrawHeight;

            return new FlatStage(stage.Bottom, stage.Top, stage.Width / Math.Max(mania.Scale.X, 1e-3f), unitSize, DrawSize);
        }

        /// <summary>
        /// Hides mania's legacy hold lighting while the ported lighting replaces it. Hidden containers stay present, so the
        /// lighting it holds still fades out and is removed on time.
        /// </summary>
        private void hideColumnTopLevels(bool hide)
        {
            if (columnTopLevelsHidden == hide)
                return;

            columnTopLevelsHidden = hide;

            for (int i = 0; i < columnTopLevels.Count; i++)
                columnTopLevels[i].Alpha = hide ? 0 : 1;
        }

        /// <summary>
        /// Lights each pressed lane's beam, brighter while its hold note is held. Reads the keys mania's own columns are
        /// pressed by, so a replay or autoplay lights the lanes as the player's keys do.
        /// </summary>
        private void updateBeams()
        {
            if (TrackShown || PortedTrackShown)
            {
                var pressed = drawableRuleset.PressedActions;

                for (int i = 0; i < playfieldColumns.Length; i++)
                {
                    pressedColumns[i] = pressed != null && isPressed(pressed, playfieldColumns[i].Action.Value);
                    holdingColumns[i] = pressedColumns[i] && HighwayHitEffectLayer.IsHolding(playfieldColumns[i]);
                }
            }

            Track.UpdateBeams(pressedColumns, holdingColumns, TrackShown);

            static bool isPressed(IEnumerable<ManiaAction> pressed, ManiaAction action)
            {
                // The container keeps a list; indexing it avoids boxing an enumerator every frame.
                if (pressed is IReadOnlyList<ManiaAction> list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] == action)
                            return true;
                    }

                    return false;
                }

                foreach (var a in pressed)
                {
                    if (a == action)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Where each stage's special column is: only for a map made for stable's N+1 layout, on the skin's side.
        /// </summary>
        private void updateSpecialSides(ManiaPlayfield playfield)
        {
            var stages = playfield.Stages;
            var sides = new SpecialColumnSide[stages.Count];
            bool specialStyle = drawableRuleset.Beatmap.SpecialStyle;

            LegacyManiaSkinConfiguration.LegacySpecialStyle? skinStyle = null;

            try
            {
                if (specialStyle && skinSource != null)
                    skinStyle = PortedSkin.ReadSpecialStyle(skinSource, playfield.TotalColumns);
            }
            catch (Exception e)
            {
                // A divider is not worth breaking gameplay over: the left side, stable's default.
                Logger.Error(e, "The skin's SpecialStyle could not be read; marking the special column on the left.");
            }

            for (int i = 0; i < sides.Length; i++)
                sides[i] = LaneGroups.SpecialSide(specialStyle, skinStyle, playfield.TotalColumns, i, sides.Length);

            specialSides = sides;
        }

        /// <summary>
        /// Puts a guide line on each stage edge and between each pair of neighbouring columns, where the warp draws them.
        /// </summary>
        private void updateGuideLines(RectangleF source)
        {
            var stages = drawableRuleset.Playfield.Stages;

            int count = 0;

            for (int i = 0; i < stages.Count; i++)
                count += stages[i].Columns.Length + 1;

            Guides.SetLineCount(count);

            if (columnLanes.Length != count - stages.Count)
                columnLanes = new Vector2[count - stages.Count];

            if (stageLanes.Length != stages.Count)
                stageLanes = new Vector2[stages.Count];

            if (source.Width <= 0)
                return;

            int index = 0;
            int columnIndex = 0;

            for (int i = 0; i < stages.Count; i++)
            {
                var columns = stages[i].Columns;
                var special = i < specialSides.Length ? specialSides[i] : SpecialColumnSide.None;
                float previousRight = 0;

                for (int j = 0; j < columns.Length; j++)
                {
                    var quad = columns[j].HitObjectContainer.ScreenSpaceDrawQuad;
                    float left = toLane(quad.TopLeft);
                    float right = toLane(quad.BottomRight);

                    // The left edge of the stage, then the middle of each gap between columns; hand groups by the stage's own key
                    // count (or its special column), so dual stages are grouped each on its own.
                    Guides.SetLine(index++, j == 0 ? left : (previousRight + left) / 2,
                        j == 0 ? HighwayGuides.GuideLineKind.Edge
                        : LaneGroups.StartsGroup(columns.Length, j, special) ? HighwayGuides.GuideLineKind.Group
                        : HighwayGuides.GuideLineKind.Lane);
                    previousRight = right;

                    columnLanes[columnIndex++] = new Vector2(left, right);
                }

                Guides.SetLine(index++, previousRight, HighwayGuides.GuideLineKind.Edge);

                if (columns.Length > 0)
                    stageLanes[i] = new Vector2(columnLanes[columnIndex - columns.Length].X, previousRight);
            }

            float toLane(Vector2 screenSpace) => (Warp.ToLocalSpace(screenSpace).X - source.Left) / source.Width;
        }

        /// <summary>
        /// The columns of every stage, in this container's local space: left and right edges, the top of the scrolling area
        /// and the judgement line.
        /// </summary>
        private RectangleF stageBounds()
        {
            float left = float.MaxValue, right = float.MinValue, top = float.MaxValue, bottom = float.MinValue;

            var stages = drawableRuleset.Playfield.Stages;

            // Indexed loops: enumerating an IReadOnlyList boxes its enumerator, and this runs every frame.
            for (int i = 0; i < stages.Count; i++)
            {
                var columns = stages[i].Columns;

                for (int j = 0; j < columns.Length; j++)
                {
                    var quad = columns[j].HitObjectContainer.ScreenSpaceDrawQuad;

                    Vector2 topLeft = Warp.ToLocalSpace(quad.TopLeft);
                    Vector2 bottomRight = Warp.ToLocalSpace(quad.BottomRight);

                    left = Math.Min(left, topLeft.X);
                    right = Math.Max(right, bottomRight.X);
                    top = Math.Min(top, topLeft.Y);
                    bottom = Math.Max(bottom, bottomRight.Y);
                }
            }

            return left > right ? RectangleF.Empty : new RectangleF(left, top, right - left, bottom - top);
        }
    }
}
