// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.Scoring;
using osu.Game.Rulesets.ManiaOrtho.Tests.Playtest;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Parity
{
    /// <summary>
    /// The core gameplay invariant: the new playfield is visual only, so the same inputs must give the same
    /// judgements, score, accuracy and live pp as osu!mania. Plays one humanised replay through both rulesets and compares.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneReplayParity : RateAdjustedBeatmapTestScene
    {
        private ReplayPlayer currentPlayer = null!;
        private GameplayClockContainer clock = null!;
        private readonly List<JudgementResult> results = new List<JudgementResult>();

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [TestCase(PlaytestBeatmaps.REAL_4K, 1)]
        [TestCase("synthetic-4k", 2)]
        [TestCase("synthetic-7k", 3)]
        public void TestJudgementParity(string source, int seed) => testParity(source, seed, HighwayNoteStyle.Skin);

        /// <summary>
        /// Native notes take over drawing mania's notes (their containers are proxied away); judging must not notice.
        /// </summary>
        [TestCase("synthetic-7k", 4)]
        public void TestJudgementParityWithNativeNotes(string source, int seed)
        {
            testParity(source, seed, HighwayNoteStyle.Highway);

            // The highway's hit effects (every case but the ported one) listen to each column's results for bursts and slot lights.
            AddAssert("ortho run lit the line on hits", () => ortho.SlotsLit, () => Is.GreaterThan(0));
        }

        /// <summary>
        /// Hidden, Fade In and Cover wrap mania's note containers, which native notes read to fade the same way. Flashlight is
        /// replaced by one that draws on the highway.
        /// </summary>
        [TestCase("synthetic-7k", 5, "HD")]
        [TestCase("synthetic-7k", 6, "FI")]
        [TestCase("synthetic-7k", 7, "CO")]
        [TestCase("synthetic-7k", 8, "FL")]
        public void TestJudgementParityWithVisibilityMods(string source, int seed, string mod) => testParity(source, seed, HighwayNoteStyle.Highway, mod);

        /// <summary>
        /// The bottom lift raises the judgement line and with it mania's hit position on screen; judging must not notice.
        /// </summary>
        [TestCase("synthetic-7k", 9)]
        public void TestJudgementParityWithLift(string source, int seed)
        {
            AddStep("lift the line to 30%", () => orthoConfig.Highway.SetValue(HighwaySetting.JudgementLineHeight, 0.3f));

            testParity(source, seed, HighwayNoteStyle.Highway);

            AddAssert("ortho run played with the lift", () => ortho.LineHeight, () => Is.EqualTo(0.3f).Within(1e-4f));
        }

        /// <summary>
        /// The highway's track hides the skin's stage parts, which keep their key handlers; every case above runs with it (the
        /// default). With the skin's track they are shown and light up on presses as in mania.
        /// </summary>
        [TestCase("synthetic-7k", 10)]
        public void TestJudgementParityWithSkinTrack(string source, int seed)
        {
            AddStep("skin's track", () => orthoConfig.Highway.SetValue(HighwaySetting.Track, HighwayTrackStyle.Skin));

            testParity(source, seed, HighwayNoteStyle.Highway);
        }

        /// <summary>
        /// The skin porter draws a legacy skin's own images, lifts the judgement line to the skin's hit position (panel key
        /// area), hides mania's legacy hit lighting and listens to each column's results for its own.
        /// </summary>
        [TestCase("synthetic-7k", 11)]
        public void TestJudgementParityWithPortedSkin(string source, int seed)
        {
            // The classic skin's HUD has no pp counter (stable skins add it in the skin editor); the other cases compare it.
            hudHasPerformanceCounter = false;

            AddStep("classic skin, ported", () =>
            {
                skins.CurrentSkinInfo.Value = DefaultLegacySkin.CreateInfo().ToLiveUnmanaged();
                orthoConfig.Highway.SetValue(HighwaySetting.Track, HighwayTrackStyle.Ported);
                orthoConfig.Highway.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Ported);
                orthoConfig.Highway.SetValue(HighwaySetting.KeyArea, SkinKeyArea.Panel);
            });

            testParity(source, seed, HighwayNoteStyle.Ported);

            AddAssert("ortho run drew the ported skin", () => ortho.Ported);
        }

        /// <summary>
        /// The far fade only changes how native notes are drawn, at its longest here.
        /// </summary>
        [TestCase("synthetic-7k", 12)]
        public void TestJudgementParityWithFarFade(string source, int seed)
        {
            AddStep("far fade 30%", () => orthoConfig.Highway.SetValue(HighwaySetting.FarFade, HighwaySettings.MAX_FAR_FADE));

            testParity(source, seed, HighwayNoteStyle.Highway);

            AddAssert("ortho run faded its notes", () => ortho.FarFade);
        }

        /// <summary>
        /// Whether the case's skin shows a pp counter to compare. Read while the steps are built, before any runs.
        /// </summary>
        private bool hudHasPerformanceCounter;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            hudHasPerformanceCounter = true;

            base.SetUpSteps();

            // A case that changes the camera must not leave it changed for the next one, even when it fails.
            AddStep("default preset", () =>
            {
                orthoConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                orthoConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPresets.DEFAULT);
                orthoConfig.Highway.SetValue(HighwaySetting.Track, HighwayTrackStyle.Highway);
                orthoConfig.Highway.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Highway);
                orthoConfig.Highway.GetBindable<float>(HighwaySetting.FarFade).SetDefault();
                skins.CurrentSkinInfo.SetDefault();
            });
        }

        private ManiaOrthoRulesetConfigManager orthoConfig => (ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(new ManiaOrthoRuleset()).AsNonNull();

        private PlayRecord ortho = null!;

        private void testParity(string source, int seed, HighwayNoteStyle noteStyle, params string[] mods)
        {
            PlayRecord mania = null!;

            AddAssert("game discovered the ruleset", () => rulesets.GetRuleset(ManiaOrthoRuleset.SHORT_NAME), () => Is.Not.Null);
            AddStep($"notes: {noteStyle}", () => orthoConfig.Highway.SetValue(HighwaySetting.NoteStyle, noteStyle));

            play(new ManiaRuleset(), source, seed, mods, r => mania = r);
            play(new ManiaOrthoRuleset(), source, seed, mods, r => ortho = r);

            AddAssert("ortho run used the ortho playfield", () => ortho.DrawableRulesetType, () => Is.EqualTo(nameof(DrawableManiaOrthoRuleset)));
            // Ported notes are drawn by the highway's note layer too.
            AddAssert($"ortho run drew notes as {noteStyle}", () => ortho.NativeNotes, () => Is.EqualTo(noteStyle != HighwayNoteStyle.Skin));
            AddAssert("ortho run drew the track as set", () => ortho.HighwayTrack,
                () => Is.EqualTo(orthoConfig.Highway.Get<HighwayTrackStyle>(HighwaySetting.Track) == HighwayTrackStyle.Highway));
            AddAssert("both runs played with the mods", () => new[] { mania.Mods, ortho.Mods }, () => Has.All.EquivalentTo(mods));
            AddAssert("replay produced a realistic mix of judgements", () => mania.Judgements.Select(j => j.Type).Distinct().Count(), () => Is.GreaterThanOrEqualTo(3));
            AddAssert("same judgement count", () => ortho.Judgements.Count, () => Is.EqualTo(mania.Judgements.Count));
            AddAssert("same judgement sequence", () => ortho.Judgements, () => Is.EqualTo(mania.Judgements));
            AddAssert("same total score", () => ortho.TotalScore, () => Is.EqualTo(mania.TotalScore));
            AddAssert("same accuracy", () => ortho.Accuracy, () => Is.EqualTo(mania.Accuracy));
            if (hudHasPerformanceCounter)
            {
                AddAssert("mania showed live pp", () => mania.LivePerformance, () => Is.GreaterThan(0));
                AddAssert("same live pp", () => ortho.LivePerformance, () => Is.EqualTo(mania.LivePerformance));
            }

            AddAssert("ortho run had a play log that skipped the replay", () => ortho.PlayLog, () => Is.EqualTo("not logged"));
        }

        private void play(Ruleset ruleset, string source, int seed, string[] mods, System.Action<PlayRecord> store)
        {
            AddStep($"[{ruleset.ShortName}] set beatmap", () => Beatmap.Value = CreateWorkingBeatmap(PlaytestBeatmaps.Load(source)));
            AddStep($"[{ruleset.ShortName}] set ruleset", () => Ruleset.Value = ruleset.RulesetInfo);
            AddStep($"[{ruleset.ShortName}] set mods", () => SelectedMods.Value = mods.Select(m => ruleset.CreateModFromAcronym(m).AsNonNull()).ToArray());
            AddStep($"[{ruleset.ShortName}] push replay player", () =>
            {
                var replay = HumanisedReplayGenerator.Generate(PlaytestBeatmaps.Load(source), seed);
                var player = new ReplayPlayer(new Score { Replay = replay, ScoreInfo = new ScoreInfo { Ruleset = ruleset.RulesetInfo } });

                player.OnLoadComplete += _ => player.GameplayState.ScoreProcessor.NewJudgement += result =>
                {
                    if (currentPlayer == player)
                        results.Add(result);
                };

                results.Clear();
                LoadScreen(currentPlayer = player);
            });

            AddUntilStep($"[{ruleset.ShortName}] player loaded", () => currentPlayer.IsCurrentScreen());
            AddStep($"[{ruleset.ShortName}] skip intro", () =>
            {
                clock = currentPlayer.ChildrenOfType<GameplayClockContainer>().Single();

                if (clock.CurrentTime < 0)
                    currentPlayer.Seek(0);
            });

            // Until-steps time out after 10s of game time, so wait for long maps in 8s checkpoints.
            double end = PlaytestBeatmaps.Load(source).HitObjects.Max(h => h.GetEndTime());

            for (double checkpoint = 8000; checkpoint < end; checkpoint += 8000)
            {
                double target = checkpoint;
                AddUntilStep($"[{ruleset.ShortName}] reach {target / 1000:0}s", () => clock.CurrentTime >= target);
            }

            AddUntilStep($"[{ruleset.ShortName}] play to completion", () => currentPlayer.GameplayState.HasCompleted);

            // The HUD's pp counter (in the default skin's layout) rates the play through the score's ruleset as it goes.
            if (hudHasPerformanceCounter)
                AddUntilStep($"[{ruleset.ShortName}] live pp counted", () => currentPlayer.ChildrenOfType<PerformancePointsCounter>().Any(c => c.IsValid));

            AddStep($"[{ruleset.ShortName}] record", () =>
            {
                var processor = currentPlayer.GameplayState.ScoreProcessor;

                store(new PlayRecord(
                    results.Select(r => new JudgementRecord(r.HitObject.StartTime, r.HitObject.GetType().Name, r.Type, System.Math.Round(r.TimeOffset, 3))).ToList(),
                    processor.TotalScore.Value,
                    processor.Accuracy.Value,
                    currentPlayer.ChildrenOfType<osu.Game.Rulesets.UI.DrawableRuleset>().Single().GetType().Name,
                    currentPlayer.ChildrenOfType<HighwayPlayfieldAdjustmentContainer>().SingleOrDefault()?.NativeNotes == true,
                    currentPlayer.GameplayState.Mods.Select(m => m.Acronym).ToArray(),
                    currentPlayer.ChildrenOfType<HighwayPlayfieldAdjustmentContainer>().SingleOrDefault()?.Settings.JudgementLineHeight,
                    currentPlayer.ChildrenOfType<HighwayPlayfieldAdjustmentContainer>().SingleOrDefault()?.TrackShown == true,
                    currentPlayer.ChildrenOfType<PerformancePointsCounter>().FirstOrDefault(c => c.IsValid)?.Current.Value ?? 0,
                    currentPlayer.ChildrenOfType<HighwayPlayfieldAdjustmentContainer>().SingleOrDefault() is HighwayPlayfieldAdjustmentContainer h
                    && h.Port != null && h.PortedNotesShown && h.PortedTrackShown && h.PortedLightingShown,
                    currentPlayer.ChildrenOfType<PlayLogRecorder>().SingleOrDefault() is PlayLogRecorder recorder ? recorder.Writing == null ? "not logged" : "logged" : "none",
                    currentPlayer.ChildrenOfType<HighwayPlayfieldAdjustmentContainer>().SingleOrDefault()?.NoteLayer.FarFade.IsActive == true,
                    currentPlayer.ChildrenOfType<HighwayPlayfieldAdjustmentContainer>().SingleOrDefault()?.HitEffects.SlotsLit ?? 0));
            });

            AddStep($"[{ruleset.ShortName}] exit player", () => currentPlayer.Exit());
            AddUntilStep($"[{ruleset.ShortName}] player exited", () => !currentPlayer.IsCurrentScreen());
            AddStep($"[{ruleset.ShortName}] dispose player", () => currentPlayer.Dispose());
        }

        private record JudgementRecord(double ObjectTime, string ObjectType, HitResult Type, double Offset);

        private record PlayRecord(List<JudgementRecord> Judgements, long TotalScore, double Accuracy, string DrawableRulesetType, bool NativeNotes, string[] Mods,
                                  float? LineHeight, bool HighwayTrack, int LivePerformance, bool Ported,
                                  string PlayLog, bool FarFade, int SlotsLit);
    }
}
