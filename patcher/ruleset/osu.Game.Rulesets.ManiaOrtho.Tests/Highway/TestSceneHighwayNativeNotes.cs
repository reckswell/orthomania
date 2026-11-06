// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.Replays;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// Notes drawn natively on the track (SPEC 6): they must sit exactly where mania's own notes would be drawn on the warped
    /// stage, keep a fixed thickness in track units, and follow mania's hold behaviour.
    /// </summary>
    public partial class TestSceneHighwayNativeNotes : PlayerTestScene
    {
        private const float depth_tolerance = 0.003f;

        private bool autoplay = true;

        /// <summary>
        /// A beatmap to play instead of synthetic-4k.
        /// </summary>
        private IBeatmap? customBeatmap;

        protected override bool Autoplay => autoplay;

        protected override Ruleset CreatePlayerRuleset() => new ManiaOrthoRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => customBeatmap ?? PlaytestBeatmaps.Load("synthetic-4k");

        private HighwayPlayfieldAdjustmentContainer highway => ((DrawableManiaOrthoRuleset)Player.DrawableRuleset).Highway.AsNonNull();

        private ManiaOrthoRulesetConfigManager rulesetConfig => (ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(CreatePlayerRuleset()).AsNonNull();

        private HighwayConfigManager highwayConfig => rulesetConfig.Highway;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            AddStep("Chunithm preset, native notes, default skin", () =>
            {
                autoplay = true;
                customBeatmap = null;
                skins.CurrentSkinInfo.SetDefault();
                rulesetConfig.SetValue(ManiaRulesetSetting.TimingBasedNoteColouring, false);
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                highwayConfig.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                highwayConfig.SetValue(HighwaySetting.NoteStyle, HighwayNoteStyle.Highway);
                highwayConfig.GetBindable<float>(HighwaySetting.FarFade).SetDefault();
                highwayConfig.GetBindable<float>(HighwaySetting.HoldOpacity).SetDefault();
            });

            base.SetUpSteps();

            AddUntilStep("notes gathered", () => highway.NoteLayer.Count > 0);
        }

        [TearDownSteps]
        public override void TearDownSteps()
        {
            AddStep("default notes, default skin", () =>
            {
                highwayConfig.GetBindable<HighwayNoteStyle>(HighwaySetting.NoteStyle).SetDefault();
                highwayConfig.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime).SetDefault();
                highwayConfig.GetBindable<float>(HighwaySetting.FarFade).SetDefault();
                highwayConfig.GetBindable<float>(HighwaySetting.HoldOpacity).SetDefault();
                rulesetConfig.SetValue(ManiaRulesetSetting.TimingBasedNoteColouring, false);
                skins.CurrentSkinInfo.SetDefault();
            });
            base.TearDownSteps();
        }

        [Test]
        public void TestManiaNotesAreNotDrawnInTheStage()
        {
            AddAssert("native notes on", () => highway.NativeNotes && highway.NoteLayer.IsPresent);
            AddAssert("every column's notes are proxied away", () => columns.All(c => c.HitObjectContainer.HasProxy));
            AddAssert("each proxy sits beside its notes", () => columns.Select((c, i) => highway.NoteProxyHolders[i].Parent == c.HitObjectContainer.Parent).All(b => b));
            AddAssert("proxies hidden", () => highway.NoteProxyHolders.All(h => !h.IsPresent));

            AddStep("flat camera", () => highwayConfig.SetValue(HighwaySetting.CameraMode, HighwayCameraMode.Flat));
            AddUntilStep("no native notes when flat", () => !highway.NoteLayer.IsPresent);
            AddAssert("mania's notes drawn through their proxies", () => highway.NoteProxyHolders.Count == columns.Length && highway.NoteProxyHolders.All(h => h.IsPresent));
        }

        [Test]
        public void TestProxiesStayInsideCoverMods()
        {
            AddStep("load with Fade In", () => LoadPlayer(new Mod[] { new ManiaModFadeIn() }));
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("notes gathered", () => highway.NoteLayer.Count > 0);

            AddAssert("each proxy is inside its column's cover, drawn under it", () => highway.NoteProxyHolders.All(h =>
                h.Parent is BufferedContainer cover && cover.Parent is PlayfieldCoveringWrapper && cover.Children[0] == h));
        }

        [TestCase(CoverExpandDirection.AlongScroll)]
        [TestCase(CoverExpandDirection.AgainstScroll)]
        public void TestCoverHidesNativeNotes(CoverExpandDirection direction)
        {
            const float margin = 0.02f;

            AddStep("load with Cover", () => LoadPlayer(new Mod[] { new ManiaModCover { Direction = { Value = direction } } }));
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("notes gathered", () => highway.NoteLayer.Count > 0);

            // Cover's default coverage: half the column fully covered (the far half along scroll, the near half against it),
            // fading over the next quarter. Positions are fractions of the column from the hit position.
            AddAssert("cover measured in track depth", () =>
            {
                float length = highway.Warp.Scroll.ScrollLength;
                var c = highway.ColumnCovers[0];

                return direction == CoverExpandDirection.AlongScroll
                    ? near(c.CoveredFrom, 0.5f * length) && c.CoveredTo == float.MaxValue && near(c.FadeFrom, 0.5f * length) && near(c.FadeTo, 0.25f * length)
                      && near(c.FadeAlpha, 1)
                    : c.CoveredFrom == float.MinValue && near(c.CoveredTo, 0.5f * length) && near(c.FadeFrom, 0.5f * length) && near(c.FadeTo, 0.75f * length)
                      && near(c.FadeAlpha, 1);

                static bool near(float actual, float expected) => Math.Abs(actual - expected) < 0.005f;
            });

            AddUntilStep("notes under the cover are not drawn, fading notes fade, notes clear of it are drawn", () =>
            {
                bool along = direction == CoverExpandDirection.AlongScroll;
                float length = highway.Warp.Scroll.ScrollLength;
                float thickness = highway.NoteLayer.NoteThickness / length;
                int covered = 0, fading = 0, clear = 0;

                foreach (var note in columns.SelectMany(c => c.HitObjectContainer.AliveObjects).OfType<DrawableNote>())
                {
                    if (note.Alpha < 1 || note.Judged)
                        continue;

                    float near = warpedDepth(note) / length;
                    float far = near + thickness;

                    if (near < 0 || far > 1)
                        continue;

                    float shown = quadsFor(note.HitObject).Select(q => Math.Max(q.Near.A, q.Far.A)).DefaultIfEmpty(0).Max();

                    if (along ? near > 0.5f + margin : far < 0.5f - margin)
                    {
                        if (shown > 0.01f)
                            throw new AssertionException($"note at {note.HitObject.StartTime} ({near:0.00} of the column) is under the cover but drawn at {shown}");

                        covered++;
                    }
                    else if (along ? far < 0.25f - margin : near > 0.75f + margin)
                    {
                        if (shown < 0.99f)
                            throw new AssertionException($"note at {note.HitObject.StartTime} ({near:0.00} of the column) is clear of the cover but drawn at {shown}");

                        clear++;
                    }
                    else if (along ? near > 0.3f && far < 0.45f : near > 0.55f && far < 0.7f)
                    {
                        // In the middle of the fade, which runs from the cover (hidden) to the clear part (shown).
                        if (shown < 0.05f || shown > 0.95f)
                            throw new AssertionException($"note at {note.HitObject.StartTime} ({near:0.00} of the column) is in the fade but drawn at {shown}");

                        fading++;
                    }
                }

                return covered > 0 && fading > 0 && clear > 0;
            });
        }

        [Test]
        public void TestNotesUseTheSkinsColumnColours()
        {
            AddAssert("argon's column colours", () => columns.All(c => quadsInColumn(c).All(q => sameColour(q.Far, c.AccentColour.Value))));
        }

        [Test]
        public void TestLegacySkinNotesUseTheBuiltInColours()
        {
            AddStep("classic skin", () => skins.CurrentSkinInfo.Value = DefaultLegacySkin.CreateInfo().ToLiveUnmanaged());
            AddUntilStep("column colours are black", () => columns.All(c => c.AccentColour.Value.R + c.AccentColour.Value.G + c.AccentColour.Value.B < 0.1f));

            AddUntilStep("white outer lanes, pink inner", () =>
            {
                Color4[] expected = { HighwayNoteLayer.OUTER_COLOUR, HighwayNoteLayer.INNER_COLOUR, HighwayNoteLayer.INNER_COLOUR, HighwayNoteLayer.OUTER_COLOUR };
                var notes = columns.Select(quadsInColumn).ToArray();

                return notes.Any(n => n.Length > 0) && notes.Select((n, i) => n.All(q => sameColour(q.Far, expected[i]))).All(b => b);
            });
        }

        [Test]
        public void TestTimingBasedColouring()
        {
            AddStep("timing-based colouring", () => rulesetConfig.SetValue(ManiaRulesetSetting.TimingBasedNoteColouring, true));

            AddUntilStep("notes take their snap colour", () =>
            {
                int checkedNotes = 0;

                foreach (var note in columns.SelectMany(c => c.HitObjectContainer.AliveObjects).OfType<DrawableNote>())
                {
                    var quads = quadsFor(note.HitObject).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note && q.Far.A > 0).ToArray();

                    if (quads.Length == 0)
                        continue;

                    if (!sameColour(quads.MaxBy(q => q.Depth1).Far, note.Colour.TopLeft.SRGB))
                        return false;

                    checkedNotes++;
                }

                return checkedNotes > 0;
            });

        }

        [Test]
        public void TestNotesSitWhereTheWarpedNotesWould()
        {
            AddAssert("leading edges match the warped stage", () =>
            {
                int checkedNotes = 0;

                foreach (var note in drawableRuleset.Playfield.Stages.SelectMany(s => s.Columns).SelectMany(c => c.HitObjectContainer.AliveObjects).OfType<DrawableNote>())
                {
                    if (note.Alpha <= 0 || note.Judged)
                        continue;

                    float expected = warpedDepth(note);
                    var quad = quadsFor(note.HitObject).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note).MinBy(q => q.Depth0);

                    if (Math.Abs(quad.Depth0 - expected) > depth_tolerance)
                        throw new AssertionException($"note at {note.HitObject.StartTime}: native {quad.Depth0}, warped {expected}");

                    checkedNotes++;
                }

                return checkedNotes > 0;
            });
        }

        /// <summary>
        /// SPEC 4: the scroll speed is a visible time. A note reaches the wall (the far edge when there is none) exactly that
        /// long before its hit, in every camera mode and at every wall; with the speed lock the visible time shrinks instead.
        /// </summary>
        [TestCase(HighwayCameraMode.Perspective, 0f, true)]
        [TestCase(HighwayCameraMode.Perspective, 0.4f, true)]
        [TestCase(HighwayCameraMode.Perspective, 0.7f, true)]
        [TestCase(HighwayCameraMode.Perspective, 0.4f, false)]
        [TestCase(HighwayCameraMode.ParallelTilt, 0.4f, true)]
        public void TestNoteAppearsOneVisibleTimeBeforeItsHit(HighwayCameraMode mode, float wall, bool keepVisibleTime)
        {
            HitObject note = null!;
            double visibleTime = 0;
            double appearTime = 0;

            AddStep($"{mode}, wall {wall:0%}, keep visible time: {keepVisibleTime}", () =>
            {
                highwayConfig.SetValue(HighwaySetting.CameraMode, mode);
                highwayConfig.SetValue(HighwaySetting.WallKeepsVisibleTime, keepVisibleTime);
                highwayConfig.SetValue(HighwaySetting.Wall, wall);
            });

            AddUntilStep("highway uses the camera and wall", () => highway.Settings.CameraMode == mode
                                                                  && Math.Abs(highway.Warp.Scroll.Wall - wall) < 1e-4f
                                                                  && highway.Warp.Scroll.WallKeepsVisibleTime == keepVisibleTime);

            AddStep("stop when a note should appear", () =>
            {
                // A tap well before the synthetic chart's first SV change (bar 16), so time maps to depth linearly.
                note = drawableRuleset.Objects.First(h => h is Note && h.StartTime >= 6000);
                visibleTime = highway.Warp.Scroll.VisibleTime(((IDrawableScrollingRuleset)drawableRuleset).ScrollingInfo.TimeRange.Value);
                appearTime = note.StartTime - visibleTime;

                Player.GameplayClockContainer.Stop();
                Player.GameplayClockContainer.Seek(appearTime);
            });

            AddUntilStep("playfield at that time", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - appearTime) < 1);
            AddWaitStep("let the highway gather", 2);

            AddAssert("the note is at the wall", () => quadsFor(note).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note).Min(q => q.Depth0),
                () => Is.EqualTo(highway.Warp.Scroll.AppearDepth).Within(depth_tolerance));

            AddAssert("on screen too", () =>
            {
                // Where this camera draws the note's leading edge, against the drawn wall's lower edge (the track's far edge
                // when there is no wall).
                float noteY = highway.Projection.YAt(quadsFor(note).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note).Min(q => q.Depth0));
                float edgeY = wall > 0
                    ? highway.ToLocalSpace(highway.Wall.GetScreenSpaceCover(0).BottomLeft).Y
                    : highway.Projection.Layout.FarY * highway.Projection.Size.Y;

                return Math.Abs(noteY - edgeY);
            }, () => Is.LessThan(1.5f));

            AddAssert("the visible time is mania's time range unless the speed is locked", () => visibleTime,
                () => Is.EqualTo(((IDrawableScrollingRuleset)drawableRuleset).ScrollingInfo.TimeRange.Value * (keepVisibleTime ? 1 : 1 - wall)).Within(1e-6));

            AddStep("half way", () => Player.GameplayClockContainer.Seek(appearTime + visibleTime / 2));
            AddUntilStep("playfield at that time", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - (appearTime + visibleTime / 2)) < 1);
            AddWaitStep("let the highway gather", 2);

            AddAssert("the note is half way to the line", () => quadsFor(note).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note).Min(q => q.Depth0),
                () => Is.EqualTo(highway.Warp.Scroll.AppearDepth / 2).Within(depth_tolerance));
        }

        /// <summary>
        /// The far fade (research/game-yarg-clonehero.md Y3): notes are drawn at nothing where they appear (the far edge, or the
        /// wall's edge) and fade in linearly to full over that share of the visible track; below it they are untouched.
        /// </summary>
        [TestCase(0f)]
        [TestCase(0.4f)]
        public void TestFarFade(float wall)
        {
            const float share = 0.25f;

            AddStep($"far fade {share:0%}, wall {wall:0%}", () =>
            {
                highwayConfig.SetValue(HighwaySetting.Wall, wall);
                highwayConfig.SetValue(HighwaySetting.FarFade, share);
            });
            AddUntilStep("highway fades", () => highway.NoteLayer.FarFade.IsActive && Math.Abs(highway.Warp.Scroll.Wall - wall) < 1e-4f);

            AddUntilStep("faded by depth: none at the wall, full a quarter of the track below it", () =>
            {
                float appear = highway.Warp.Scroll.AppearDepth;
                int fading = 0, clear = 0;

                foreach (var note in columns.SelectMany(c => c.HitObjectContainer.AliveObjects).OfType<DrawableNote>())
                {
                    if (note.Alpha < 1 || note.Judged)
                        continue;

                    foreach (var quad in quadsFor(note.HitObject).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note))
                    {
                        if (quad.Depth1 > appear + 1e-4f)
                            throw new AssertionException($"note at {note.HitObject.StartTime} drawn past where notes appear ({quad.Depth1} > {appear})");

                        checkEnd(quad.Depth0, quad.Near.A);
                        checkEnd(quad.Depth1, quad.Far.A);

                        float expectedNear = expected(quad.Depth0);

                        if (expectedNear < 1)
                            fading++;
                        else if (quad.Depth0 > 0)
                            clear++;
                    }

                    void checkEnd(float depth, float alpha)
                    {
                        if (Math.Abs(alpha - expected(depth)) > 0.01f)
                            throw new AssertionException($"note at {note.HitObject.StartTime} drawn at {alpha} at depth {depth}, expected {expected(depth)}");
                    }
                }

                return fading > 0 && clear > 0;

                float expected(float depth) => Math.Clamp((appear - depth) / (appear * share), 0, 1);
            });

            AddStep("no far fade", () => highwayConfig.SetValue(HighwaySetting.FarFade, 0f));
            AddUntilStep("notes appear at full strength", () =>
            {
                var notes = Enumerable.Range(0, highway.NoteLayer.Count).Select(highway.NoteLayer.GetQuad)
                                      .Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note && q.Depth1 > highway.Warp.Scroll.AppearDepth * 0.8f)
                                      .ToArray();

                return !highway.NoteLayer.FarFade.IsActive && notes.Length > 0 && notes.All(q => q.Near.A > 0.99f && q.Far.A > 0.99f);
            });
        }

        /// <summary>
        /// The far fade multiplies mania's own covers (scored mods), never replaces them: with Fade In, a note where both fade
        /// is drawn at the product of the two, and one Fade In hides stays hidden.
        /// </summary>
        [Test]
        public void TestFarFadeMultipliesFadeIn()
        {
            AddStep("far fade 30%", () => highwayConfig.SetValue(HighwaySetting.FarFade, HighwaySettings.MAX_FAR_FADE));
            AddStep("load with Fade In", () => LoadPlayer(new Mod[] { new ManiaModFadeIn() }));
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("notes gathered under both", () => highway.NoteLayer.Count > 0 && highway.NoteLayer.FarFade.IsActive && highway.ColumnCovers[0].IsActive);

            AddUntilStep("each note at Fade In's visibility times the far fade's, both fading somewhere", () =>
            {
                var farFade = highway.NoteLayer.FarFade;
                var allColumns = columns;
                int both = 0;

                for (int c = 0; c < allColumns.Length; c++)
                {
                    var cover = highway.ColumnCovers[c];

                    foreach (var note in allColumns[c].HitObjectContainer.AliveObjects.OfType<DrawableNote>())
                    {
                        if (note.Alpha < 1 || note.Judged)
                            continue;

                        foreach (var quad in quadsFor(note.HitObject).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note))
                        {
                            // Each piece lies within one part of each cover; its ends are drawn at that part's visibility.
                            float middle = (quad.Depth0 + quad.Depth1) / 2;

                            checkEnd(quad.Depth0, quad.Near.A);
                            checkEnd(quad.Depth1, quad.Far.A);

                            float byCover = cover.VisibilityAt(middle);
                            float byFade = farFade.VisibilityAt(middle);

                            if (byCover > 0 && byCover < 1 && byFade > 0 && byFade < 1)
                                both++;

                            void checkEnd(float depth, float alpha)
                            {
                                float expected = cover.VisibilityAt(depth, middle) * farFade.VisibilityAt(depth, middle);

                                if (Math.Abs(alpha - expected) > 0.01f)
                                    throw new AssertionException($"note at {note.HitObject.StartTime} drawn at {alpha} at depth {depth}, expected {expected}");
                            }
                        }
                    }
                }

                return both > 0;
            });
        }

        /// <summary>
        /// SPEC 6: notes have a fixed thickness in track units; each camera preset sets it.
        /// </summary>
        [TestCase(HighwayPreset.Chunithm)]
        [TestCase(HighwayPreset.Voltex)]
        public void TestNotesHaveAFixedThicknessOnTheTrack(HighwayPreset preset)
        {
            float expected = HighwayPresets.Get(preset).NoteThickness;

            AddStep($"{preset} preset", () => highwayConfig.SetValue(HighwaySetting.Preset, preset));
            AddUntilStep("highway uses its thickness", () => highway.NoteLayer.NoteThickness, () => Is.EqualTo(expected).Within(1e-5f));
            AddWaitStep("let the highway gather", 2);

            AddAssert("every note is that depth thick", () =>
            {
                var notes = Enumerable.Range(0, highway.NoteLayer.Count).Select(highway.NoteLayer.GetQuad).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note)
                                      .GroupBy(q => q.HitObject).ToArray();

                return notes.Length > 0 && notes.All(g => Math.Abs(g.Max(q => q.Depth1) - g.Min(q => q.Depth0) - expected) < 1e-4f);
            });
        }

        [Test]
        public void TestHeldNoteStopsAtTheLine()
        {
            DrawableHoldNote held = null!;

            AddUntilStep("a hold note is being held", () =>
            {
                held = drawableRuleset.Playfield.Stages.SelectMany(s => s.Columns).SelectMany(c => c.HitObjectContainer.AliveObjects).OfType<DrawableHoldNote>()
                                      .FirstOrDefault(h => h.IsHolding.Value && h.Time.Current > h.HitObject.StartTime + 50 && h.Time.Current < h.HitObject.EndTime - 200)!;

                // Freeze it there, so the following checks see the same frame.
                if (held != null)
                    Player.GameplayClockContainer.Stop();

                return held != null;
            });

            AddWaitStep("let the highway gather", 2);

            AddAssert("its head sits on the line", () => quadsFor(held.HitObject).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.HoldHead).Min(q => q.Depth0),
                () => Is.EqualTo(0).Within(1e-4));

            AddAssert("its body starts under the head", () => quadsFor(held.HitObject).Single(q => q.Kind == HighwayNoteLayer.NoteQuadKind.HoldBody).Depth0,
                () => Is.EqualTo(highway.NoteLayer.NoteThickness / 2).Within(1e-4));

            AddAssert("its tail is where the warped tail is", () =>
            {
                float tail = quadsFor(held.HitObject).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.HoldTail).Min(q => q.Depth0);
                return Math.Abs(tail - warpedDepth(held.Tail)) < depth_tolerance;
            });
        }

        /// <summary>
        /// Sekai's Long Note Transparency (research game-sekai.md K6): the body fades, its head and tail do not.
        /// </summary>
        [Test]
        public void TestHoldOpacity()
        {
            DrawableHoldNote held = null!;
            float body = 0;
            float head = 0;
            float tail = 0;

            AddUntilStep("a hold note is being held", () =>
            {
                held = drawableRuleset.Playfield.Stages.SelectMany(s => s.Columns).SelectMany(c => c.HitObjectContainer.AliveObjects).OfType<DrawableHoldNote>()
                                      .FirstOrDefault(h => h.IsHolding.Value && h.Time.Current > h.HitObject.StartTime + 50 && h.Time.Current < h.HitObject.EndTime - 200)!;

                if (held != null)
                    Player.GameplayClockContainer.Stop();

                return held != null;
            });

            AddWaitStep("let the highway gather", 2);
            AddStep("measure at full opacity", () =>
            {
                body = holdBody(held).Near.A;
                head = holdEndAlpha(held, HighwayNoteLayer.NoteQuadKind.HoldHead);
                tail = holdEndAlpha(held, HighwayNoteLayer.NoteQuadKind.HoldTail);
            });
            AddAssert("a held body is drawn at 85%", () => body, () => Is.EqualTo(0.85f).Within(1e-4f));

            AddStep("hold opacity 40%", () => highwayConfig.SetValue(HighwaySetting.HoldOpacity, 0.4f));
            AddUntilStep("the body is 40% as opaque", () => holdBody(held).Near.A, () => Is.EqualTo(0.4f * body).Within(1e-4f));
            AddAssert("its head is not", () => holdEndAlpha(held, HighwayNoteLayer.NoteQuadKind.HoldHead), () => Is.EqualTo(head).Within(1e-4f));
            AddAssert("nor its tail", () => holdEndAlpha(held, HighwayNoteLayer.NoteQuadKind.HoldTail), () => Is.EqualTo(tail).Within(1e-4f));

            AddStep("hold opacity 0%", () => highwayConfig.SetValue(HighwaySetting.HoldOpacity, 0f));
            AddUntilStep("bodies never vanish", () => holdBody(held).Near.A, () => Is.EqualTo(HighwaySettings.MIN_HOLD_OPACITY * body).Within(1e-4f));
        }

        private HighwayNoteLayer.NoteQuad holdBody(DrawableHoldNote hold) => quadsFor(hold.HitObject).Single(q => q.Kind == HighwayNoteLayer.NoteQuadKind.HoldBody);

        private float holdEndAlpha(DrawableHoldNote hold, HighwayNoteLayer.NoteQuadKind kind) => quadsFor(hold.HitObject).Where(q => q.Kind == kind).Max(q => q.Near.A);

        [Test]
        public void TestHoldHeldPastItsEndKeepsItsHeadOnTheLine()
        {
            const double start = 4000;
            const double end = 4500;

            // Pressed on its time and released 150 ms after its end, inside the tail's late window (226 ms at OD 0).
            playOneHold(start, end, end + 150);
            seekTo(end + 50);

            AddAssert("its head was hit", () => onlyHold.Head.IsHit);
            AddAssert("it is still held", () => onlyHold.IsHolding.Value);
            AddAssert("its release is not judged yet", () => onlyHold.Judged, () => Is.False);
            AddAssert("its head sits on the line", () => holdHeadDepth, () => Is.EqualTo(0).Within(1e-4));
            AddAssert("no body is left", () => quadsFor(onlyHold.HitObject).Count(q => q.Kind == HighwayNoteLayer.NoteQuadKind.HoldBody), () => Is.Zero);
        }

        [Test]
        public void TestHoldWithNoLengthPassesTheLine()
        {
            // Converted maps clamp a hold's end to its start; mania draws such a hold with no height and lets its head pass
            // the line like a tap's, held or not.
            const double time = 4000;

            playOneHold(time, time, time + 150);
            seekTo(time + 50);

            AddAssert("its head was hit", () => onlyHold.Head.IsHit);
            AddAssert("it is still held", () => onlyHold.IsHolding.Value);
            AddAssert("its head has passed the line", () => holdHeadDepth, () => Is.LessThan(-0.01f));
        }

        /// <summary>
        /// Loads a 4K chart with one hold note in the first column, played by a replay that presses it at its start and
        /// releases it at <paramref name="releaseAt"/>. With a replay attached the playfield follows seeks of a stopped clock,
        /// so the checks see exact times whatever the test host's frame times are.
        /// </summary>
        private void playOneHold(double start, double end, double releaseAt)
        {
            AddStep("play one hold note", () =>
            {
                autoplay = false;
                customBeatmap = new ManiaBeatmap(new StageDefinition(4))
                {
                    BeatmapInfo = new BeatmapInfo
                    {
                        Ruleset = new ManiaRuleset().RulesetInfo,
                        // The widest windows: the release is judged until 226 ms after the end.
                        Difficulty = new BeatmapDifficulty { CircleSize = 4, OverallDifficulty = 0 },
                    },
                    HitObjects = { new HoldNote { StartTime = start, Duration = end - start, Column = 0 } },
                };
                LoadPlayer();
            });
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);

            AddStep("attach the replay", () =>
            {
                Player.GameplayClockContainer.Stop();

                ManiaAction action = columns[0].Action.Value;
                drawableRuleset.SetReplayScore(new Score
                {
                    Replay = new Replay
                    {
                        Frames =
                        {
                            new ManiaReplayFrame(0),
                            new ManiaReplayFrame(start, action),
                            new ManiaReplayFrame(releaseAt),
                        }
                    }
                });
            });
        }

        private void seekTo(double time)
        {
            AddStep($"seek to {time}", () => Player.GameplayClockContainer.Seek(time));
            AddUntilStep("playfield at that time", () => drawableRuleset.FrameStableClock.CurrentTime, () => Is.EqualTo(time).Within(1));
            AddWaitStep("let the highway gather", 2);
        }

        private DrawableHoldNote onlyHold => columns[0].HitObjectContainer.AliveObjects.OfType<DrawableHoldNote>().Single();

        private float holdHeadDepth => quadsFor(onlyHold.HitObject).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.HoldHead).Min(q => q.Depth0);

        private DrawableManiaOrthoRuleset drawableRuleset => (DrawableManiaOrthoRuleset)Player.DrawableRuleset;

        private Column[] columns => drawableRuleset.Playfield.Stages.SelectMany(s => s.Columns).ToArray();

        /// <summary>
        /// The main (coloured) quad of every visible tap note in <paramref name="column"/>: the far part of the bar, after
        /// the white rim.
        /// </summary>
        private HighwayNoteLayer.NoteQuad[] quadsInColumn(Column column)
            => column.HitObjectContainer.AliveObjects.OfType<DrawableNote>().Where(n => n is not DrawableHoldNoteHead)
                     .Select(n => quadsFor(n.HitObject).Where(q => q.Kind == HighwayNoteLayer.NoteQuadKind.Note).ToArray())
                     .Where(q => q.Length == 2 && q[0].Far.A > 0)
                     .Select(q => q.MaxBy(x => x.Depth1)).ToArray();

        private static bool sameColour(Color4 drawn, Color4 expected)
            => Math.Abs(drawn.R - expected.R) < 0.01f && Math.Abs(drawn.G - expected.G) < 0.01f && Math.Abs(drawn.B - expected.B) < 0.01f;

        private HighwayNoteLayer.NoteQuad[] quadsFor(HitObject hitObject)
            => Enumerable.Range(0, highway.NoteLayer.Count).Select(highway.NoteLayer.GetQuad).Where(q => q.HitObject == hitObject).ToArray();

        /// <summary>
        /// Where the warp would draw <paramref name="drawable"/>'s leading edge: mania places it in the flat stage, and the warp
        /// maps the stage's scrolling area linearly onto the track up to the scroll length.
        /// </summary>
        private float warpedDepth(DrawableManiaHitObject drawable)
        {
            var source = highway.Warp.Source;
            float y = highway.Warp.ToLocalSpace(drawable.ScreenSpaceDrawQuad.BottomLeft).Y;

            return (source.Bottom - y) / source.Height * highway.Warp.Scroll.ScrollLength;
        }
    }
}
