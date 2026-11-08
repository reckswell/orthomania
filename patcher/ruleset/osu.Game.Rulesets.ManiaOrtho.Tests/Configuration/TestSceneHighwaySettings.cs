// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ManiaOrtho.ScoreServerContract;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.ScoreServer;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using Realms;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Configuration
{
    /// <summary>
    /// Settings plumbing: values persist, presets fill in values, each game remembers what the player changes, and the
    /// settings subsection loads. Headless under <c>dotnet test</c>; run it through the playtest runner with
    /// <c>--scene TestSceneHighwaySettings --screenshot out.png</c> to see the panel.
    /// </summary>
    public partial class TestSceneHighwaySettings : OsuTestScene
    {
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        private SettingsStore store = null!;

        private SearchContainer search = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create settings store", () => store = new SettingsStore(Realm));
            AddStep("reset highway settings", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    config.Highway.ForgetGames(); // each game remembers its settings; start from its own
                    config.Highway.SetValue(HighwaySetting.Wall, 0f); // a game with a wall leaves it to the player
                    config.Highway.GetBindable<WallScale>(HighwaySetting.WallScale).SetDefault();
                    config.Highway.SetValue(HighwaySetting.WallKeepsVisibleTime, true);
                    config.Highway.GetBindable<float>(HighwaySetting.HoldOpacity).SetDefault();
                    config.Highway.GetBindable<float>(HighwaySetting.TrackOpacity).SetDefault();
                    config.Highway.GetBindable<KeyBeamLength>(HighwaySetting.KeyBeam).SetDefault();
                    config.Highway.GetBindable<float>(HighwaySetting.KiaiSway).SetDefault();
                    config.Highway.GetBindable<SkinStageFit>(HighwaySetting.StageFit).SetDefault();
                }

                gameConfig.Highway.GetBindable<bool>(HighwaySetting.ShareManiaKeys).SetDefault();
                gameConfig.Highway.GetBindable<SpeedScale>(HighwaySetting.SpeedScale).SetDefault();
                gameConfig.GetBindable<double>(ManiaRulesetSetting.ScrollSpeed).SetDefault();
                gameConfig.Highway.ForgetGames();
                gameConfig.Highway.SetValue(HighwaySetting.FoldedGroups, HighwayConfigManager.DEFAULT_FOLDED_GROUPS);

                // Now, not 100 ms later in the background, where it would overwrite what a test's own configs store.
                gameConfig.Save();
                gameConfig.Highway.Save();
            });
        }

        /// <summary>
        /// Sekai's Long Note Transparency and Lane Transparency (research/game-sekai.md K6, K9): preferences that start at
        /// the look as designed, outside the presets.
        /// </summary>
        [Test]
        public void TestOpacities()
        {
            AddAssert("both default to full", () => new[] { new HighwaySettings().HoldOpacity, new HighwaySettings().TrackOpacity }, () => Is.EqualTo(new[] { 1f, 1f }));

            AddAssert("config defaults to full", () =>
            {
                using (var config = create())
                    return new[] { config.Highway.Current.HoldOpacity, config.Highway.Current.TrackOpacity };
            }, () => Is.EqualTo(new[] { 1f, 1f }));

            AddAssert("holds never vanish, the track can", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.HoldOpacity, 0f);
                    config.Highway.SetValue(HighwaySetting.TrackOpacity, 0f);
                    return new[] { config.Highway.Current.HoldOpacity, config.Highway.Current.TrackOpacity };
                }
            }, () => Is.EqualTo(new[] { HighwaySettings.MIN_HOLD_OPACITY, 0f }).Within(1e-6f));

            AddAssert("changing them keeps the preset", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.HoldOpacity, 0.7f);
                    config.Highway.SetValue(HighwaySetting.TrackOpacity, 0.7f);
                    return config.Highway.Get<HighwayPreset>(HighwaySetting.Preset);
                }
            }, () => Is.EqualTo(HighwayPreset.Chunithm));

            AddAssert("choosing a preset leaves them", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.HoldOpacity, 0.4f);
                    config.Highway.SetValue(HighwaySetting.TrackOpacity, 0.5f);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    return new[] { config.Highway.Current.HoldOpacity, config.Highway.Current.TrackOpacity };
                }
            }, () => Is.EqualTo(new[] { 0.4f, 0.5f }).Within(1e-4f));
        }

        /// <summary>
        /// DJMAX's beam lengths (research/game-djmax.md R2): a preference that starts at today's long beam, outside the presets.
        /// </summary>
        [Test]
        public void TestKeyBeam()
        {
            AddAssert("defaults to long", () =>
            {
                using (var config = create())
                    return new[] { new HighwaySettings().KeyBeam, config.Highway.Current.KeyBeam };
            }, () => Is.EqualTo(new[] { KeyBeamLength.Long, KeyBeamLength.Long }));

            AddAssert("kept, and keeps the preset", () =>
            {
                using (var config = create())
                    config.Highway.SetValue(HighwaySetting.KeyBeam, KeyBeamLength.Quarter);

                using (var config = create())
                    return (config.Highway.Current.KeyBeam, config.Highway.Get<HighwayPreset>(HighwaySetting.Preset));
            }, () => Is.EqualTo((KeyBeamLength.Quarter, HighwayPreset.Chunithm)));

            AddAssert("choosing a preset leaves it", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    return config.Highway.Current.KeyBeam;
                }
            }, () => Is.EqualTo(KeyBeamLength.Quarter));
        }

        /// <summary>
        /// SPEC 2: cosmetic camera motion is off by default, and a preference outside the presets.
        /// </summary>
        [Test]
        public void TestKiaiSwayIsOffByDefault()
        {
            AddAssert("off", () =>
            {
                using (var config = create())
                    return new[] { new HighwaySettings().KiaiSway, config.Highway.Current.KiaiSway };
            }, () => Is.EqualTo(new[] { 0f, 0f }));

            AddAssert("kept, keeps the preset, and a preset leaves it", () =>
            {
                using (var config = create())
                    config.Highway.SetValue(HighwaySetting.KiaiSway, 0.5f);

                using (var config = create())
                {
                    var kept = (config.Highway.Current.KiaiSway, config.Highway.Get<HighwayPreset>(HighwaySetting.Preset));
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    return (kept, config.Highway.Current.KiaiSway);
                }
            }, () => Is.EqualTo(((0.5f, HighwayPreset.Chunithm), 0.5f)));
        }

        /// <summary>
        /// The wall scale (SPEC 4) is a preference outside the presets. Choosing a game's gives the wall that game's speed
        /// behaviour; the toggle can be changed back afterwards.
        /// </summary>
        [Test]
        public void TestWallScale()
        {
            AddAssert("percent outside the presets; the default Chunithm preset shows Chunithm's", () =>
            {
                using (var config = create())
                    return new[] { new HighwaySettings().WallScale, config.Highway.Current.WallScale };
            }, () => Is.EqualTo(new[] { WallScale.Percent, WallScale.Chunithm }));

            AddAssert("Chunithm keeps the speed, IIDX the visible time, osu!mania the speed, percent leaves it", () =>
            {
                using (var config = create())
                {
                    var keeps = new List<bool>();

                    // From percent, which leaves the toggle: the Chunithm preset's Chunithm scale is already chosen.
                    config.Highway.SetValue(HighwaySetting.WallScale, WallScale.Percent);

                    foreach (var scale in new[] { WallScale.Chunithm, WallScale.Iidx, WallScale.Lane, WallScale.Percent })
                    {
                        config.Highway.SetValue(HighwaySetting.WallScale, scale);
                        keeps.Add(config.Highway.Current.WallKeepsVisibleTime);
                    }

                    return keeps;
                }
            }, () => Is.EqualTo(new[] { false, true, false, false }));

            AddAssert("the toggle still changes afterwards", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.WallScale, WallScale.Chunithm);
                    config.Highway.SetValue(HighwaySetting.WallKeepsVisibleTime, true);
                    return config.Highway.Current.WallKeepsVisibleTime;
                }
            });

            AddAssert("kept, keeps the preset, and a preset leaves it", () =>
            {
                using (var config = create())
                {
                    var kept = (config.Highway.Current.WallScale, config.Highway.Get<HighwayPreset>(HighwaySetting.Preset));
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    return (kept, config.Highway.Current.WallScale);
                }
            }, () => Is.EqualTo(((WallScale.Chunithm, HighwayPreset.Chunithm), WallScale.Chunithm)));

            AddAssert("loading a stored scale leaves the stored toggle", () =>
            {
                using (var config = create())
                    return config.Highway.Current.WallKeepsVisibleTime;
            });
        }

        /// <summary>
        /// The panel shows one wall slider, in the chosen scale's units, and it sets the same wall as the others.
        /// </summary>
        [Test]
        public void TestWallSliderPerScale()
        {
            AddStep("no wall, percent", () =>
            {
                gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                gameConfig.Highway.SetValue(HighwaySetting.Wall, 0f); // a game with a wall leaves it to the player
                gameConfig.Highway.SetValue(HighwaySetting.WallScale, WallScale.Percent);
            });
            showPanel();
            AddAssert("only the percent slider", shownWallSliders, () => Is.EqualTo(new[] { "Wall" }));

            AddStep("Chunithm's scale", () => gameConfig.Highway.SetValue(HighwaySetting.WallScale, WallScale.Chunithm));
            AddAssert("only Chunithm's slider", shownWallSliders, () => Is.EqualTo(new[] { "Wall (Chunithm)" }));

            AddStep("slide to 5", () => wallSlider<int>("Wall (Chunithm)").Current.Value = 5);
            AddAssert("the wall is Chunithm's 5", () => gameConfig.Highway.Get<float>(HighwaySetting.Wall), () => Is.EqualTo(WallScales.ChunithmWall(5)).Within(1e-4f));
            AddAssert("the percent slider follows", () => wallSlider<float>("Wall").Current.Value, () => Is.EqualTo(0.29f).Within(1e-4f));

            AddStep("IIDX's scale", () => gameConfig.Highway.SetValue(HighwaySetting.WallScale, WallScale.Iidx));
            AddAssert("only the white number slider", shownWallSliders, () => Is.EqualTo(new[] { "Wall (white number)" }));
            AddAssert("switching left the wall", () => gameConfig.Highway.Get<float>(HighwaySetting.Wall), () => Is.EqualTo(WallScales.ChunithmWall(5)).Within(1e-4f));
            AddAssert("it shows the same wall", () => wallSlider<int>("Wall (white number)").Current.Value,
                () => Is.EqualTo(WallScales.ToUnits(WallScale.Iidx, WallScales.ChunithmWall(5), gameConfig.Highway.Current.ToLayout().FarWidthRatio)));
            AddStep("slide to 500", () => wallSlider<int>("Wall (white number)").Current.Value = 500);
            AddAssert("the wall covers half the lane on screen", () => WallScales.ScreenShare(gameConfig.Highway.Get<float>(HighwaySetting.Wall),
                gameConfig.Highway.Current.ToLayout().FarWidthRatio), () => Is.EqualTo(0.5f).Within(5e-4f));
            AddAssert("Chunithm's slider follows to the nearest step", () => wallSlider<int>("Wall (Chunithm)").Current.Value,
                () => Is.EqualTo(WallScales.ChunithmStep(gameConfig.Highway.Get<float>(HighwaySetting.Wall))));

            AddStep("a wall set elsewhere", () => gameConfig.Highway.SetValue(HighwaySetting.Wall, 0.4f));
            AddAssert("the sliders follow", () => (wallSlider<float>("Wall").Current.Value, wallSlider<int>("Wall (Chunithm)").Current.Value),
                () => Is.EqualTo((0.4f, WallScales.ChunithmStep(0.4f))));

            AddStep("a far narrower camera", () =>
            {
                gameConfig.Highway.SetValue(HighwaySetting.LaneTilt, 80f);
                gameConfig.Highway.SetValue(HighwaySetting.LaneLength, 12f);
            });
            AddAssert("its far end is narrower", () => gameConfig.Highway.Current.ToLayout().FarWidthRatio, () => Is.LessThan(0.1f));
            AddAssert("the white number follows the camera", () => wallSlider<int>("Wall (white number)").Current.Value,
                () => Is.EqualTo(WallScales.ToUnits(WallScale.Iidx, 0.4f, gameConfig.Highway.Current.ToLayout().FarWidthRatio)));
            AddAssert("the wall stays", () => gameConfig.Highway.Get<float>(HighwaySetting.Wall), () => Is.EqualTo(0.4f).Within(1e-4f));
            AddAssert("its most follows the camera too", () => ((BindableNumber<int>)wallSlider<int>("Wall (white number)").Current).MaxValue,
                () => Is.EqualTo(WallScales.MaxUnits(WallScale.Iidx, gameConfig.Highway.Current.ToLayout().FarWidthRatio)));

            AddStep("Project Sekai's approach", () => gameConfig.Highway.SetValue(HighwaySetting.Approach, ApproachCurve.SEKAI));
            AddAssert("the white number follows the approach", () => wallSlider<int>("Wall (white number)").Current.Value,
                () => Is.EqualTo(WallScales.ToUnits(WallScale.Iidx, 0.4f, gameConfig.Highway.Current.ToLayout().FarWidthRatio, ApproachCurve.SEKAI)));
        }

        /// <summary>
        /// osu!mania's "Touch overlay" is offered here too (SETTINGS-PARITY.md), and sets the setting mania's touch area reads.
        /// </summary>
        [Test]
        public void TestTouchOverlayOffered()
        {
            AddStep("touch overlay off", () => gameConfig.SetValue(ManiaRulesetSetting.TouchOverlay, false));
            showPanel();
            AddStep("tick it", () => touchOverlay.Current.Value = true);
            AddAssert("the setting is on", () => gameConfig.Get<bool>(ManiaRulesetSetting.TouchOverlay));
            AddStep("touch overlay off", () => gameConfig.SetValue(ManiaRulesetSetting.TouchOverlay, false));
            AddAssert("the box follows", () => touchOverlay.Current.Value, () => Is.False);
        }

        private FormCheckBox touchOverlay => this.ChildrenOfType<FormCheckBox>().Single(c => c.Caption.ToString() == RulesetSettingsStrings.TouchOverlay.ToString());

        [Test]
        public void TestValuesPersist()
        {
            AddStep("write custom values", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.LaneTilt, 66.6f);
                    config.Highway.SetValue(HighwaySetting.Wall, 0.25f);
                    config.Highway.SetValue(HighwaySetting.NoteThickness, 0.04f);
                    config.Highway.SetValue(HighwaySetting.WallKeepsVisibleTime, false);
                    config.SetValue(ManiaRulesetSetting.ScrollSpeed, 29.0);
                }
            });

            AddAssert("values read back", () =>
            {
                using (var config = create())
                {
                    var current = config.Highway.Current;
                    return near(current.LaneTilt, 66.6f)
                           && near(current.Wall, 0.25f)
                           && near(current.NoteThickness, 0.04f)
                           && !current.WallKeepsVisibleTime
                           && config.Highway.Get<HighwayPreset>(HighwaySetting.Preset) == HighwayPreset.Chunithm
                           && config.Get<double>(ManiaRulesetSetting.ScrollSpeed) == 29.0;
                }
            });
        }

        /// <summary>
        /// The speed is stored finer and up to faster than osu!mania's limits, which read it first as the config loads: it
        /// comes back as it was set, not rounded to 0.1 or capped at 40.
        /// </summary>
        [TestCase(52.4)]
        [TestCase(17.1283)]
        [TestCase(40.0)]
        [TestCase(8.0)]
        public void TestFineSpeedPersists(double speed)
        {
            AddStep($"store speed {speed}", () =>
            {
                using (var config = create())
                    config.SetValue(ManiaRulesetSetting.ScrollSpeed, speed);
            });

            AddAssert("read back as it was", () =>
            {
                using (var config = create())
                    return config.Get<double>(ManiaRulesetSetting.ScrollSpeed);
            }, () => Is.EqualTo(speed).Within(1e-9));

            AddAssert("and again", () =>
            {
                using (var config = create())
                    return config.Get<double>(ManiaRulesetSetting.ScrollSpeed);
            }, () => Is.EqualTo(speed).Within(1e-9));
        }

        [Test]
        public void TestNoteThicknessReachesBarsOfAlphasSkin()
        {
            // Alpha's 4K note bars are 6.4% of the scroll length (research/game-osu-mania.md, finding 7); under the Parallel
            // camera the setting is the same measure, so the range must reach it, and StepMania's square arrows (14.4%).
            AddAssert("15% is the most", () =>
            {
                using (var config = create())
                    return ((BindableFloat)config.Highway.GetBindable<float>(HighwaySetting.NoteThickness)).MaxValue;
            }, () => Is.EqualTo(0.15f));

            AddStep("set 6.4%", () =>
            {
                using (var config = create())
                    config.Highway.SetValue(HighwaySetting.NoteThickness, 0.064f);
            });

            AddAssert("kept, not clamped", () =>
            {
                using (var config = create())
                    return config.Highway.Get<float>(HighwaySetting.NoteThickness);
            }, () => Is.EqualTo(0.064f).Within(1e-6f));
        }

        /// <summary>
        /// Alpha's skin port request: pick a skin and play. Skins without legacy mania images fall back to the highway's look.
        /// </summary>
        [Test]
        public void TestNotesDrawnFromThePortedSkinByDefault()
        {
            AddAssert("settings default", () => new HighwaySettings().NoteStyle, () => Is.EqualTo(HighwayNoteStyle.Ported));
            AddAssert("config default", () =>
            {
                using (var config = create())
                    return config.Highway.GetBindable<HighwayNoteStyle>(HighwaySetting.NoteStyle).Default;
            }, () => Is.EqualTo(HighwayNoteStyle.Ported));
        }

        [Test]
        public void TestPortedTrackByDefault()
        {
            AddAssert("settings default", () => new HighwaySettings().Track, () => Is.EqualTo(HighwayTrackStyle.Ported));
            AddAssert("config default", () =>
            {
                using (var config = create())
                    return config.Highway.GetBindable<HighwayTrackStyle>(HighwaySetting.Track).Default;
            }, () => Is.EqualTo(HighwayTrackStyle.Ported));

            AddAssert("a preset leaves it alone", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Track, HighwayTrackStyle.Skin);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    return config.Highway.Current.Track;
                }
            }, () => Is.EqualTo(HighwayTrackStyle.Skin));
        }

        /// <summary>
        /// An install from before the skin porter stored "Highway" for the three styles the first time it ran; they move to
        /// the porter once, a style chosen otherwise is kept, and choosing "Highway" again afterwards sticks.
        /// </summary>
        [Test]
        public void TestOldDefaultsMoveToThePorterOnce()
        {
            AddStep("store the old defaults", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.NoteStyle, HighwayNoteStyle.Highway);
                    config.Highway.SetValue(HighwaySetting.HitEffects, HighwayHitEffectStyle.Highway);
                    config.Highway.SetValue(HighwaySetting.Track, HighwayTrackStyle.Skin);
                    config.Highway.SetValue(HighwaySetting.SkinPortDefaultsApplied, false);
                }
            });

            AddAssert("old defaults moved, a chosen style kept", () =>
            {
                using (var config = create())
                {
                    var current = config.Highway.Current;
                    return current.NoteStyle == HighwayNoteStyle.Ported
                           && current.HitEffects == HighwayHitEffectStyle.Ported
                           && current.Track == HighwayTrackStyle.Skin
                           && config.Highway.Get<bool>(HighwaySetting.SkinPortDefaultsApplied);
                }
            });

            AddStep("choose the highway's notes again", () =>
            {
                using (var config = create())
                    config.Highway.SetValue(HighwaySetting.NoteStyle, HighwayNoteStyle.Highway);
            });

            AddAssert("kept", () =>
            {
                using (var config = create())
                    return config.Highway.Get<HighwayNoteStyle>(HighwaySetting.NoteStyle);
            }, () => Is.EqualTo(HighwayNoteStyle.Highway));
        }

        /// <summary>
        /// Ported lanes default to the skin's own width (Alpha 2026-11-04); an install that stored the earlier default ("Skin
        /// position", the camera's width) moves once, and choosing it again afterwards sticks.
        /// </summary>
        [Test]
        public void TestStageFitMovesToTheSkinsWidthOnce()
        {
            AddAssert("skin's width by default", () => HighwayPresets.Get(HighwayPresets.DEFAULT).StageFit, () => Is.EqualTo(SkinStageFit.SkinLayout));

            AddStep("store the old default", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.StageFit, SkinStageFit.SkinPosition);
                    config.Highway.SetValue(HighwaySetting.SkinLayoutDefaultApplied, false);
                }
            });

            AddAssert("moved", () =>
            {
                using (var config = create())
                    return config.Highway.Current.StageFit;
            }, () => Is.EqualTo(SkinStageFit.SkinLayout));

            AddAssert("only once", () =>
            {
                using (var config = create())
                    return config.Highway.Get<bool>(HighwaySetting.SkinLayoutDefaultApplied);
            });

            AddStep("store camera before the move", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.StageFit, SkinStageFit.Camera);
                    config.Highway.SetValue(HighwaySetting.SkinLayoutDefaultApplied, false);
                }
            });

            AddAssert("a fit other than the old default is kept", () =>
            {
                using (var config = create())
                    return config.Highway.Current.StageFit;
            }, () => Is.EqualTo(SkinStageFit.Camera));

            AddStep("choose skin position again", () =>
            {
                using (var config = create())
                    config.Highway.SetValue(HighwaySetting.StageFit, SkinStageFit.SkinPosition);
            });

            AddAssert("kept", () =>
            {
                using (var config = create())
                    return config.Highway.Current.StageFit;
            }, () => Is.EqualTo(SkinStageFit.SkinPosition));
        }

        /// <summary>
        /// The scroll speed's label shows how long notes are visible on the highway, not mania's <c>11485 / speed</c>: the
        /// skin's hit position changes it (the default skin's line is 14.8 units higher than the classic skin's, +2.3%), and
        /// so does a wall that keeps the speed, except under the flat camera (no wall). Toasts in gameplay:
        /// <see cref="ManiaOrtho.Tests.Highway.TestSceneVisibleTime"/>.
        /// </summary>
        [Test]
        public void TestScrollSpeedShowsTheVisibleTime()
        {
            AddStep("speed 29, no wall, default skin", () =>
            {
                skins.CurrentSkinInfo.Value = ArgonSkin.CreateInfo().ToLiveUnmanaged();
                speedWithoutWall();
            });
            showPanel();
            AddUntilStep("405 ms (mania's label: 396)", speedLabel, () => Is.EqualTo("405ms (speed 29.0)"));

            AddStep("40% wall keeping the speed", () => gameConfig.Highway.SetValue(HighwaySetting.Wall, 0.4f));
            AddUntilStep("243 ms", speedLabel, () => Is.EqualTo("243ms (speed 29.0)"));

            AddStep("flat camera", () => gameConfig.Highway.SetValue(HighwaySetting.CameraMode, HighwayCameraMode.Flat));
            AddUntilStep("405 ms: no wall when flat", speedLabel, () => Is.EqualTo("405ms (speed 29.0)"));

            // The classic skin keeps osu!'s default line (HitPosition 402), where mania's label is right.
            AddStep("classic skin", () => skins.CurrentSkinInfo.Value = DefaultLegacySkin.CreateInfo().ToLiveUnmanaged());
            AddUntilStep("396 ms", speedLabel, () => Is.EqualTo("396ms (speed 29.0)"));
        }

        /// <summary>
        /// A legacy skin gives a hit position per key count, so the label uses the selected beatmap's, as mania converts it
        /// (key mods included): here a skin whose 7K line is at the bottom of the stage (19.4% longer) and whose others are
        /// osu!'s default.
        /// </summary>
        [Test]
        public void TestScrollSpeedUsesTheBeatmapsKeyCount()
        {
            AddStep("speed 29, no wall", speedWithoutWall);
            AddStep("a 7K beatmap", () => Beatmap.Value = CreateWorkingBeatmap(maniaBeatmap(7)));
            showPanel(new SevenKeyLineAtTheBottom());
            AddUntilStep("473 ms", speedLabel, () => Is.EqualTo("473ms (speed 29.0)"));

            AddStep("a 4K beatmap", () => Beatmap.Value = CreateWorkingBeatmap(maniaBeatmap(4)));
            AddUntilStep("396 ms", speedLabel, () => Is.EqualTo("396ms (speed 29.0)"));

            AddStep("an osu! beatmap with 7K selected", () =>
            {
                Beatmap.Value = CreateWorkingBeatmap(new Beatmap { BeatmapInfo = { Ruleset = new RulesetInfo("osu", "osu!", string.Empty, 0) } });
                SelectedMods.Value = new Mod[] { new ManiaModKey7() };
            });
            AddUntilStep("473 ms", speedLabel, () => Is.EqualTo("473ms (speed 29.0)"));

            static IBeatmap maniaBeatmap(int keys) => new ManiaBeatmap(new StageDefinition(keys))
            {
                BeatmapInfo = { Ruleset = new ManiaRuleset().RulesetInfo, Difficulty = { CircleSize = keys } },
            };
        }

        [TearDownSteps]
        public void TearDownSteps()
        {
            AddStep("default skin, highway, beatmap and mods", () =>
            {
                skins.CurrentSkinInfo.SetDefault();
                gameConfig.GetBindable<double>(ManiaRulesetSetting.ScrollSpeed).SetDefault();
                gameConfig.Highway.GetBindable<WallScale>(HighwaySetting.WallScale).SetDefault();
                gameConfig.Highway.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime).SetDefault();
                gameConfig.Highway.ForgetGames();
                gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                gameConfig.Highway.GetBindable<HighwayPreset>(HighwaySetting.Preset).SetDefault();
                gameConfig.Highway.GetBindable<float>(HighwaySetting.Wall).SetDefault();
                gameConfig.Highway.ForgetGames();
                gameConfig.Save();
                gameConfig.Highway.Save();
                Beatmap.SetDefault();
                SelectedMods.SetDefault();
            });
        }

        [Test]
        public void TestPresets()
        {
            AddAssert("default is Chunithm", () =>
            {
                using (var config = create())
                    return same(config.Highway.Current, HighwayPresets.Get(HighwayPreset.Chunithm));
            });

            AddAssert("choosing a preset applies it", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    var voltex = HighwayPresets.Get(HighwayPreset.Voltex);
                    return same(config.Highway.Current, voltex) && config.Highway.Get<HighwayPreset>(HighwaySetting.Preset) == HighwayPreset.Voltex;
                }
            });

            // USC's chip on USC's own camera: 76 px at 1080p, against Chunithm's 81 to 98 (research/game-sdvx.md).
            AddAssert("Voltex notes look about as tall at the line as Chunithm's", () => heightAtLine(HighwayPreset.Voltex) / heightAtLine(HighwayPreset.Chunithm),
                () => Is.InRange(0.7f, 1.25f));

            // Sekai's notes are 7.4% of the screen tall at the line (research/game-sekai.md).
            AddAssert("Sekai notes are Sekai's height at the line", () => heightAtLine(HighwayPreset.Sekai), () => Is.InRange(0.067f, 0.081f));

            AddAssert("editing a value keeps the game", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    config.Highway.SetValue(HighwaySetting.TrackWidth, 0.9f);
                    return config.Highway.Get<HighwayPreset>(HighwaySetting.Preset) == HighwayPreset.Chunithm
                           && near(config.Highway.Get<float>(HighwaySetting.TrackWidth), 0.9f)
                           && config.Highway.GameChanged;
                }
            });

            AddAssert("its reset goes back to the game's value", () =>
            {
                using (var config = create())
                {
                    var trackWidth = config.Highway.GetBindable<float>(HighwaySetting.TrackWidth);
                    trackWidth.SetDefault();
                    return (trackWidth.Value, config.Highway.GameChanged);
                }
            }, () => Is.EqualTo((HighwayPresets.Get(HighwayPreset.Chunithm).TrackWidth, false)));

            AddAssert("Classic switches to flat", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Classic);
                    return config.Highway.Current.CameraMode == HighwayCameraMode.Flat;
                }
            });
        }

        /// <summary>
        /// Presets by game (Alpha 2026-11-08): choosing one for the first time sets its look (camera, line, lane or stage width,
        /// note thickness) and shows the speed and the wall in its numbers, which then stay as the player leaves them.
        /// </summary>
        /// <param name="preset">The game.</param>
        /// <param name="speedScale">The game's speed numbers.</param>
        /// <param name="wallScale">The game's wall units, or null for a game without a wall, which leaves them as they were.</param>
        [TestCase(HighwayPreset.Chunithm, SpeedScale.Chunithm, WallScale.Chunithm)]
        [TestCase(HighwayPreset.Voltex, SpeedScale.Sdvx, null)]
        [TestCase(HighwayPreset.Sekai, SpeedScale.Sekai, null)]
        [TestCase(HighwayPreset.Iidx, SpeedScale.Iidx, WallScale.Iidx)]
        [TestCase(HighwayPreset.Djmax, SpeedScale.Djmax, null)]
        [TestCase(HighwayPreset.Quaver, SpeedScale.Quaver, WallScale.Lane)]
        [TestCase(HighwayPreset.StepMania, SpeedScale.StepMania, WallScale.Lane)]
        [TestCase(HighwayPreset.Classic, SpeedScale.Mania, null)]
        public void TestGamePresets(HighwayPreset preset, SpeedScale speedScale, WallScale? wallScale)
        {
            var other = speedScale == SpeedScale.Mania ? SpeedScale.Chunithm : SpeedScale.Mania;

            AddAssert("choosing it sets its look and numbers, and leaves the wall's speed behaviour", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                    config.Highway.ForgetGames(); // so the game is chosen for the first time
                    config.Highway.SetValue(HighwaySetting.Wall, 0.3f);
                    config.Highway.SetValue(HighwaySetting.WallScale, WallScale.Percent);
                    config.Highway.SetValue(HighwaySetting.WallKeepsVisibleTime, true);
                    config.Highway.SetValue(HighwaySetting.Preset, preset);

                    var values = HighwayPresets.Get(preset);
                    var current = config.Highway.Current;

                    // A game with a wall leaves it to the player; one without takes it down.
                    return config.Highway.Get<HighwayPreset>(HighwaySetting.Preset) == preset
                           && same(current with { Wall = values.Wall }, values)
                           && current.WidthMode == values.WidthMode
                           && near(current.LaneWidth, values.LaneWidth)
                           && current.SpeedScale == speedScale
                           && current.WallScale == (wallScale ?? WallScale.Percent)
                           && HighwayPresets.HasWall(preset) == (wallScale != null)
                           && near(current.Wall, wallScale != null ? 0.3f : 0)
                           && current.WallKeepsVisibleTime;
                }
            });

            AddStep("show the speed in other numbers", () =>
            {
                using (var config = create())
                    config.Highway.SetValue(HighwaySetting.SpeedScale, other);
            });

            AddAssert("kept, and still the preset", () =>
            {
                using (var config = create())
                    return config.Highway.Get<SpeedScale>(HighwaySetting.SpeedScale) == other && config.Highway.Get<HighwayPreset>(HighwaySetting.Preset) == preset;
            });
        }

        [Test]
        public void TestWidthIsPartOfAGamesLook()
        {
            AddAssert("IIDX is drawn by lane", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Iidx);
                    return config.Highway.Current.WidthMode == HighwayWidthMode.Lane;
                }
            });

            AddAssert("lanes on Chunithm stay Chunithm's", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    config.Highway.SetValue(HighwaySetting.WidthMode, HighwayWidthMode.Lane);
                    config.Highway.SetValue(HighwaySetting.LaneWidth, 0.05f);
                }

                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Iidx);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    return (config.Highway.Get<HighwayPreset>(HighwaySetting.Preset), config.Highway.Current.WidthMode, config.Highway.Current.LaneWidth);
                }
            }, () => Is.EqualTo((HighwayPreset.Chunithm, HighwayWidthMode.Lane, 0.05f)));

            AddAssert("and IIDX keeps its own lane width", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Iidx);
                    return config.Highway.Current.LaneWidth;
                }
            }, () => Is.EqualTo(HighwayPresets.Get(HighwayPreset.Iidx).LaneWidth));
        }

        /// <summary>
        /// Each game remembers its own settings (Alpha 2026-11-08): its look and numbers as the player changed them, and the
        /// player's speed and wall. A game chosen for the first time starts from the speed and wall in use; Custom keeps all of
        /// its values.
        /// </summary>
        [Test]
        public void TestEachGameRemembersItsSettings()
        {
            AddStep("Chunithm: wall 0.3, speed 12, lane tilted to 50°", () =>
            {
                using (var config = create())
                {
                    config.SetValue(ManiaRulesetSetting.ScrollSpeed, 12.0);
                    config.Highway.SetValue(HighwaySetting.Wall, 0.3f);
                    config.Highway.SetValue(HighwaySetting.LaneTilt, 50f);
                }
            });

            AddAssert("SDVX, first time: no wall, the same speed, its own lane", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    return (config.Highway.Get<float>(HighwaySetting.Wall), config.Get<double>(ManiaRulesetSetting.ScrollSpeed), config.Highway.Current.LaneTilt);
                }
            }, () => Is.EqualTo((0f, 12.0, HighwayPresets.Get(HighwayPreset.Voltex).LaneTilt)));

            AddStep("SDVX at speed 20, a shorter lane", () =>
            {
                using (var config = create())
                {
                    config.SetValue(ManiaRulesetSetting.ScrollSpeed, 20.0);
                    config.Highway.SetValue(HighwaySetting.LaneLength, 6f);
                }
            });

            AddAssert("Chunithm again: its wall, speed and tilt", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    return (config.Highway.Get<float>(HighwaySetting.Wall), config.Get<double>(ManiaRulesetSetting.ScrollSpeed), config.Highway.Current.LaneTilt);
                }
            }, () => Is.EqualTo((0.3f, 12.0, 50f)));

            AddAssert("SDVX again: its speed and lane", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    return (config.Get<double>(ManiaRulesetSetting.ScrollSpeed), config.Highway.Current.LaneLength, config.Highway.Current.LaneTilt);
                }
            }, () => Is.EqualTo((20.0, 6f, HighwayPresets.Get(HighwayPreset.Voltex).LaneTilt)));

            AddAssert("a value reset to the game's follows the game again", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.JudgementLineHeight, 0.2f);
                    config.Highway.GetBindable<float>(HighwaySetting.JudgementLineHeight).SetDefault();
                }

                using (var config = create())
                    return (config.Highway.Current.JudgementLineHeight, config.Highway.Get<string>(HighwaySetting.GameMemory).Contains("JudgementLineHeight", StringComparison.Ordinal));
            }, () => Is.EqualTo((HighwayPresets.Get(HighwayPreset.Voltex).JudgementLineHeight, false)));

            AddAssert("Custom starts from SDVX, then keeps its own", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                    bool startsFromSdvx = near(config.Highway.Current.LaneLength, 6f);
                    config.Highway.SetValue(HighwaySetting.LaneTilt, 30f);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                    return startsFromSdvx && near(config.Highway.Current.LaneTilt, 30f) && near(config.Highway.Current.LaneLength, 6f);
                }
            });
        }

        /// <summary>
        /// The update that started each game's memory: the game in use keeps the player's wall, speed and numbers.
        /// </summary>
        [Test]
        public void TestGameMemoryStartsFromAnInstallBefore()
        {
            AddStep("an install from before, on Chunithm with a wall and osu!mania's numbers", () =>
            {
                using (var config = create())
                {
                    config.SetValue(ManiaRulesetSetting.ScrollSpeed, 17.0);
                    config.Highway.SetValue(HighwaySetting.Wall, 0.25f);
                    config.Highway.SetValue(HighwaySetting.SpeedScale, SpeedScale.Mania);
                    config.Highway.SetValue(HighwaySetting.GameMemoryStarted, false);
                    config.Highway.SetValue(HighwaySetting.GameMemory, string.Empty);
                }
            });

            AddAssert("Chunithm kept them, through SDVX and back", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    return (config.Highway.Get<float>(HighwaySetting.Wall), config.Get<double>(ManiaRulesetSetting.ScrollSpeed),
                        config.Highway.Get<SpeedScale>(HighwaySetting.SpeedScale));
                }
            }, () => Is.EqualTo((0.25f, 17.0, SpeedScale.Mania)));
        }

        /// <summary>
        /// The wall is the player's in a game with one, as the speed is: it keeps the preset, stays on the next start and comes
        /// back with the game (Alpha 2026-11-08). A game without one keeps it down.
        /// </summary>
        [Test]
        public void TestWallInAGamesPreset()
        {
            AddAssert("a wall in Chunithm keeps Chunithm", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    config.Highway.SetValue(HighwaySetting.Wall, 0.3f);
                    return config.Highway.Get<HighwayPreset>(HighwaySetting.Preset);
                }
            }, () => Is.EqualTo(HighwayPreset.Chunithm));

            AddAssert("and stays on the next start", () =>
            {
                using (var config = create())
                    return (config.Highway.Get<HighwayPreset>(HighwaySetting.Preset), config.Highway.Get<float>(HighwaySetting.Wall));
            }, () => Is.EqualTo((HighwayPreset.Chunithm, 0.3f)));

            AddAssert("SDVX takes it down", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    return config.Highway.Get<float>(HighwaySetting.Wall);
                }
            }, () => Is.Zero);

            AddAssert("and keeps it down", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Wall, 0.2f);
                    return (config.Highway.Get<HighwayPreset>(HighwaySetting.Preset), config.Highway.Get<float>(HighwaySetting.Wall));
                }
            }, () => Is.EqualTo((HighwayPreset.Voltex, 0f)));

            AddAssert("Chunithm brings its wall back", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    return config.Highway.Get<float>(HighwaySetting.Wall);
                }
            }, () => Is.EqualTo(0.3f));

            AddAssert("IIDX starts from it, then keeps its own", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Iidx);
                    bool startsFromIt = near(config.Highway.Get<float>(HighwaySetting.Wall), 0.3f);
                    config.Highway.SetValue(HighwaySetting.Wall, 0.5f);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    bool chunithms = near(config.Highway.Get<float>(HighwaySetting.Wall), 0.3f);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Iidx);
                    return startsFromIt && chunithms && near(config.Highway.Get<float>(HighwaySetting.Wall), 0.5f);
                }
            });
        }

        /// <summary>
        /// An install from before presets set the width, on a preset with "Width by" Lane: it moves to Custom and keeps its lane
        /// width rather than losing it to the preset.
        /// </summary>
        [Test]
        public void TestWidthByLaneKeptFromBefore()
        {
            AddStep("an install from before, on Chunithm by lane", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    config.Highway.SetValue(HighwaySetting.WidthMode, HighwayWidthMode.Lane);
                    config.Highway.SetValue(HighwaySetting.LaneWidth, 0.06f);
                    config.Highway.SetValue(HighwaySetting.PresetScalesApplied, false);
                    config.Highway.SetValue(HighwaySetting.GameMemoryStarted, false);
                    config.Highway.SetValue(HighwaySetting.GameMemory, string.Empty);
                }

                // Back on Chunithm, as the old version kept it: "Width by" was outside the presets then.
                Realm.Write(r => r.All<RealmRulesetSetting>()
                                  .First(s => s.RulesetName == ManiaOrthoRuleset.SHORT_NAME && s.Key == nameof(HighwaySetting.Preset))
                                  .Value = nameof(HighwayPreset.Chunithm));
            });

            AddAssert("Custom, by lane, its lane width kept", () =>
            {
                using (var config = create())
                {
                    var current = config.Highway.Current;
                    return (config.Highway.Get<HighwayPreset>(HighwaySetting.Preset), current.WidthMode, current.LaneWidth);
                }
            }, () => Is.EqualTo((HighwayPreset.Custom, HighwayWidthMode.Lane, 0.06f)));
        }

        /// <summary>
        /// The update that gave presets their game's numbers shows them once, for the preset in use; afterwards the player's
        /// choice stays.
        /// </summary>
        [Test]
        public void TestPresetScalesReachAnExistingInstallOnce()
        {
            AddStep("an install from before, on SDVX with osu!mania's numbers", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    config.Highway.SetValue(HighwaySetting.SpeedScale, SpeedScale.Mania);
                    config.Highway.SetValue(HighwaySetting.PresetScalesApplied, false);
                    config.Highway.SetValue(HighwaySetting.GameMemoryStarted, false);
                    config.Highway.SetValue(HighwaySetting.GameMemory, string.Empty);
                }
            });

            AddAssert("SDVX's numbers on the next start", () =>
            {
                using (var config = create())
                    return config.Highway.Get<SpeedScale>(HighwaySetting.SpeedScale);
            }, () => Is.EqualTo(SpeedScale.Sdvx));

            AddStep("back to osu!mania's numbers", () =>
            {
                using (var config = create())
                    config.Highway.SetValue(HighwaySetting.SpeedScale, SpeedScale.Mania);
            });

            AddAssert("they stay", () =>
            {
                using (var config = create())
                    return config.Highway.Get<SpeedScale>(HighwaySetting.SpeedScale);
            }, () => Is.EqualTo(SpeedScale.Mania));
        }

        /// <summary>
        /// Alpha 2026-11-08: "Settings: orthomania" first, its Gameplay and Other settings in groups that fold, then "Game preset"
        /// with the game first and its settings under it; a game without a wall shows none.
        /// </summary>
        [Test]
        public void TestSettingsPanel()
        {
            AddStep("Chunithm", () =>
            {
                gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                gameConfig.Highway.SetValue(HighwaySetting.Wall, 0f); // a game with a wall leaves it to the player
            });
            showPanel();

            AddAssert("Settings: orthomania, then Game preset", () => groups(SettingsGroupStyle.Section), () => Is.EqualTo(new[] { "Settings: orthomania", "Game preset" }));
            AddAssert("their groups", () => groups(SettingsGroupStyle.Fold),
                () => Is.EqualTo(new[] { "Gameplay", "Other settings", "Speed", "Lane and camera", "Lane and note size", "Wall" }));
            AddAssert("the notes come first", () => firstSettingIn("Settings: orthomania"), () => Is.EqualTo("Notes"));
            AddAssert("the game comes first in Game preset", () => firstSettingIn("Game preset"), () => Is.EqualTo("Game"));
            AddAssert("the speed and the lane tilt are the game's", () => new[] { groupAbove(speedScaleDropdown), groupAbove(cameraSlider("Lane tilt")) },
                () => Is.All.EqualTo("Game preset"));
            AddAssert("the skin layout is orthomania's", () => groupAbove(this.ChildrenOfType<FormEnumDropdown<SkinStageFit>>().Single()), () => Is.EqualTo("Settings: orthomania"));
            AddAssert("Chunithm has a wall", () => group("Wall").IsPresent && shown(this.ChildrenOfType<FormEnumDropdown<WallScale>>().Single()));

            AddStep("SDVX", () => gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex));
            AddUntilStep("no wall settings", () => !group("Wall").IsPresent && !shown(this.ChildrenOfType<FormEnumDropdown<WallScale>>().Single())
                                                   && !this.ChildrenOfType<FormCheckBox>().Any(c => c.Caption.ToString() == "Wall keeps visible time" && shown(c)));

            AddStep("a wall by hand", () => gameConfig.Highway.SetValue(HighwaySetting.Wall, 0.2f));
            AddUntilStep("still SDVX, still no wall", () => (gameConfig.Highway.Get<HighwayPreset>(HighwaySetting.Preset), gameConfig.Highway.Get<float>(HighwaySetting.Wall), group("Wall").IsPresent),
                () => Is.EqualTo((HighwayPreset.Voltex, 0f, false)));

            AddStep("Chunithm again", () => gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm));
            AddUntilStep("the wall's settings are back", () => group("Wall").IsPresent);
        }

        /// <summary>
        /// "Link with osu!" under the score server address, and what the address's note says about whose account plays go to.
        /// The score server's settings are orthomania's own, under Other settings.
        /// </summary>
        [Test]
        public void TestScoreServerLink()
        {
            AddStep("not linked, local server", () =>
            {
                ScoreServerLinking.Clear(gameConfig.ScoreServer);
                gameConfig.ScoreServer.SetValue(ScoreServerSetting.ScoreServerAddress, ScoreServerConfigManager.DEFAULT_ADDRESS);
            });
            showPanel();

            // What the panel watches must outlive a collection: bound copies are only weakly held.
            AddStep("collect garbage", () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });

            AddAssert("in Settings: orthomania, under Other settings", () => groupAbove(linkButton) == "Settings: orthomania" && !onScreen(linkButton));
            AddStep("unfold other settings", () => clickFold("Other settings"));
            AddUntilStep("shown", () => onScreen(linkButton));

            AddAssert("offers to link", () => linkButton.Text.ToString(), () => Is.EqualTo("Link with osu!"));
            AddAssert("plays go to the first account", addressNote, () => Does.StartWith("Not linked"));

            // As the osu! account the game is signed in with.
            AddStep("linked as osu player", () => ScoreServerLinking.Save(gameConfig.ScoreServer, new Uri(ScoreServerConfigManager.DEFAULT_ADDRESS),
                new LinkStatus { Status = LinkStatus.LINKED, Token = "ortho_token", Username = "osu player", OsuUserId = API.LocalUser.Value.Id }));
            AddAssert("offers to unlink", () => linkButton.Text.ToString(), () => Is.EqualTo("Unlink osu player"));
            AddAssert("plays go to that account", addressNote, () => Is.EqualTo("Plays go to osu player's account (linked with osu!)."));

            AddStep("linked as another osu! account", () => ScoreServerLinking.Save(gameConfig.ScoreServer, new Uri(ScoreServerConfigManager.DEFAULT_ADDRESS),
                new LinkStatus { Status = LinkStatus.LINKED, Token = "ortho_token_2", Username = "osu player", OsuUserId = API.LocalUser.Value.Id + 1 }));
            AddAssert("says the game is signed in as someone else", addressNote, () => Does.Contain("but the game is signed in to osu! as"));

            AddStep("another address", () => gameConfig.ScoreServer.SetValue(ScoreServerSetting.ScoreServerAddress, "http://localhost:7727"));
            AddAssert("says the link is elsewhere", addressNote, () => Does.StartWith("The game is linked to 127.0.0.1:7727"));
            AddStep("back", () => gameConfig.ScoreServer.SetValue(ScoreServerSetting.ScoreServerAddress, ScoreServerConfigManager.DEFAULT_ADDRESS));

            AddStep("unlink", () => linkButton.TriggerClick());
            AddAssert("forgotten", () => ScoreServerLinking.Current(gameConfig.ScoreServer), () => Is.Null);
            AddAssert("offers to link again", () => linkButton.Text.ToString(), () => Is.EqualTo("Link with osu!"));
        }

        private ScoreServerLinkButton linkButton => this.ChildrenOfType<ScoreServerLinkButton>().Single();

        private string addressNote() => this.ChildrenOfType<SettingsItemV2>().Single(i => i.ChildrenOfType<FormTextBox>().Any(b => b.Caption.ToString() == "Score server address"))
                                             .Note.Value?.Text.ToString() ?? string.Empty;

        /// <summary>
        /// The groups fold (Alpha 2026-11-08: "not a long list of things to scroll through"): Other settings starts folded, a
        /// fold stays as left, and searching finds settings in a folded group.
        /// </summary>
        [Test]
        public void TestSettingsFold()
        {
            showPanel();

            AddAssert("other settings folded, the rest open", () => groupsShowing(), () => Is.EqualTo(new[] { "Gameplay", "Speed", "Lane and camera", "Lane and note size", "Wall" }));
            AddAssert("their settings hidden", () => onScreen(this.ChildrenOfType<FormEnumDropdown<SkinStageFit>>().Single()), () => Is.False);

            AddStep("unfold other settings", () => clickFold("Other settings"));
            AddUntilStep("shown", () => onScreen(this.ChildrenOfType<FormEnumDropdown<SkinStageFit>>().Single()));
            AddStep("fold the lane and its size", () =>
            {
                clickFold("Lane and camera");
                clickFold("Lane and note size");
            });
            AddAssert("remembered", () => gameConfig.Highway.Get<string>(HighwaySetting.FoldedGroups), () => Is.EqualTo("Lane,Size"));

            showPanel();
            AddAssert("folded as left", () => groupsShowing(), () => Is.EqualTo(new[] { "Gameplay", "Other settings", "Speed", "Wall" }));

            AddStep("search \"angle\"", () => search.SearchTerm = "angle");
            AddUntilStep("found in its folded group", () => onScreen(cameraSlider("Lane tilt")));
            AddAssert("nothing else of the group", () => onScreen(cameraSlider("Judgement line height")), () => Is.False);
            AddAssert("the gameplay settings hidden", () => onScreen(this.ChildrenOfType<FormEnumDropdown<HighwayNoteStyle>>().Single()), () => Is.False);
            AddAssert("its section's heading kept", () => group("Game preset").IsPresent && !group("Settings: orthomania").IsPresent);

            AddStep("search a group's name", () => search.SearchTerm = "skin layout");
            AddUntilStep("all of the group", () => onScreen(this.ChildrenOfType<FormEnumDropdown<SkinStageFit>>().Single())
                                                   && onScreen(this.ChildrenOfType<FormEnumDropdown<SkinKeyArea>>().Single()));

            AddStep("search \"pjsk\"", () => search.SearchTerm = "pjsk");
            AddUntilStep("the game", () => onScreen(this.ChildrenOfType<FormEnumDropdown<HighwayPreset>>().Single()));

            AddStep("no search", () => search.SearchTerm = string.Empty);
            AddUntilStep("folded again", () => !onScreen(cameraSlider("Lane tilt")) && onScreen(this.ChildrenOfType<FormEnumDropdown<HighwayNoteStyle>>().Single()));
        }

        /// <summary>
        /// Lane tilt (Alpha 2026-11-08): each tilted game's own lane, one camera for all, so the same tilt means the same in
        /// every preset (research/topic-approach-angles.md).
        /// </summary>
        [Test]
        public void TestLaneTiltByGame()
        {
            AddAssert("Chunithm ends under its HUD, 0.24 wide", () => layoutOf(HighwayPreset.Chunithm), () => Is.EqualTo((0.163f, 0.24f)).Using<(float, float)>(nearLayout));
            AddAssert("SDVX ends 41% down, 0.071 wide", () => layoutOf(HighwayPreset.Voltex), () => Is.EqualTo((0.41f, 0.071f)).Using<(float, float)>(nearLayout));
            AddAssert("Project Sekai ends just under the top, 0.073 wide", () => layoutOf(HighwayPreset.Sekai), () => Is.EqualTo((0.016f, 0.073f)).Using<(float, float)>(nearLayout));
            AddAssert("only Project Sekai eases its notes", () => Enum.GetValues<HighwayPreset>().Where(p => p != HighwayPreset.Custom)
                                                                      .Where(p => HighwayPresets.Get(p).ToLayout().Approach != ApproachCurve.PERSPECTIVE),
                () => Is.EqualTo(new[] { HighwayPreset.Sekai }));
            AddAssert("the straight-on games stand upright to the top", () => new[] { HighwayPreset.Iidx, HighwayPreset.Djmax, HighwayPreset.Quaver, HighwayPreset.StepMania }
                                                                                .Select(p => HighwayPresets.Get(p).ToLayout().FarY),
                () => Is.All.EqualTo(0));

            AddAssert("SDVX remembers a tilt, Project Sekai an approach", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    config.Highway.SetValue(HighwaySetting.LaneTilt, 70f);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Sekai);
                    config.Highway.SetValue(HighwaySetting.Approach, ApproachCurve.PERSPECTIVE);
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    float tilt = config.Highway.Current.LaneTilt;
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Sekai);
                    return (tilt, config.Highway.Current.Approach, config.Highway.Get<HighwayPreset>(HighwaySetting.Preset));
                }
            }, () => Is.EqualTo((70f, ApproachCurve.PERSPECTIVE, HighwayPreset.Sekai)));

            static (float, float) layoutOf(HighwayPreset preset)
            {
                var layout = HighwayPresets.Get(preset).ToLayout();
                return (layout.FarY, layout.FarWidthRatio);
            }

            static bool nearLayout((float, float) a, (float, float) b) => System.Math.Abs(a.Item1 - b.Item1) < 0.002f && System.Math.Abs(a.Item2 - b.Item2) < 0.002f;
        }

        /// <summary>
        /// An install from before the lane tilt keeps its Custom camera's shape, once; a named preset shows its own lane.
        /// </summary>
        [TestCase(HighwayCameraMode.Perspective, 0.106f, 0.284f)]
        [TestCase(HighwayCameraMode.Perspective, 0.5f, 0f)]
        [TestCase(HighwayCameraMode.Perspective, 1f, 0.3f)]
        [TestCase(HighwayCameraMode.ParallelTilt, 1f, 0.3f)]
        public void TestCustomCameraKeptAsTilt(HighwayCameraMode mode, float farWidthRatio, float farEdgeHeight)
        {
            AddStep("store a camera from before", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                    config.Highway.SetValue(HighwaySetting.CameraMode, mode);
                    config.Highway.SetValue(HighwaySetting.JudgementLineHeight, 0.059f);
                    config.Highway.SetValue(HighwaySetting.FarWidthRatio, farWidthRatio);
                    config.Highway.SetValue(HighwaySetting.FarEdgeHeight, farEdgeHeight);
                    config.Highway.SetValue(HighwaySetting.LaneTiltApplied, false);
                    config.Highway.SetValue(HighwaySetting.GameMemoryStarted, false);
                    config.Highway.SetValue(HighwaySetting.GameMemory, string.Empty);
                }
            });

            AddAssert("it draws the same lane", () =>
            {
                using (var config = create())
                {
                    var layout = config.Highway.Current.ToLayout();
                    return (layout.FarY, layout.FarWidthRatio);
                }
            }, () => Is.EqualTo((farEdgeHeight, farWidthRatio)).Using<(float, float)>((a, b) => System.Math.Abs(a.Item1 - b.Item1) < 0.003f
                                                                                                    && System.Math.Abs(a.Item2 - b.Item2) < 0.003f));

            AddStep("tilt it", () =>
            {
                using (var config = create())
                    config.Highway.SetValue(HighwaySetting.LaneTilt, 30f);
            });

            AddAssert("only once", () =>
            {
                using (var config = create())
                    return config.Highway.Get<float>(HighwaySetting.LaneTilt);
            }, () => Is.EqualTo(30f));
        }

        /// <summary>
        /// The camera settings: tilt shows where it ends the lane; length and approach only for the perspective camera; the
        /// far edge sliders they replaced are gone.
        /// </summary>
        [Test]
        public void TestLaneTiltSettings()
        {
            AddStep("Chunithm", () =>
            {
                gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
            });
            showPanel();

            AddAssert("no far edge sliders", () => this.ChildrenOfType<FormSliderBar<float>>().Select(b => b.Caption.ToString()),
                () => Has.None.StartsWith("Far edge"));
            AddAssert("tilt ends the lane under the HUD", () => tiltLabel(), () => Is.EqualTo("56.3° (ends 16% down)"));
            AddStep("a lane to the top", () => gameConfig.Highway.SetValue(HighwaySetting.LaneTilt, 45f));
            AddUntilStep("the label says so", () => tiltLabel(), () => Is.EqualTo("45.0° (to the top)"));
            AddAssert("length and approach shown", () => shown(cameraSlider("Lane length")) && shown(cameraSlider("Note approach")));

            AddStep("SDVX", () => gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex));
            AddUntilStep("tilt ends the lane 41% down", () => tiltLabel(), () => Is.EqualTo("81.4° (ends 41% down)"));
            AddStep("a shorter lane", () => gameConfig.Highway.SetValue(HighwaySetting.LaneLength, 4f));
            AddUntilStep("the label follows the length", () => tiltLabel(), () => Is.Not.EqualTo("81.4° (ends 41% down)"));
            AddAssert("approach reads as perspective", () => cameraSlider("Note approach").LabelFormat(ApproachCurve.PERSPECTIVE).ToString(), () => Is.EqualTo("Perspective"));
            AddAssert("and Sekai's as Sekai's", () => cameraSlider("Note approach").LabelFormat(ApproachCurve.SEKAI).ToString(), () => Is.EqualTo("Project Sekai"));

            AddStep("IIDX", () => gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Iidx));
            AddUntilStep("tilt only", () => shown(cameraSlider("Lane tilt")) && !shown(cameraSlider("Lane length")) && !shown(cameraSlider("Note approach"))
                                            && !shown(cameraSlider("Kiai lift")));

            AddStep("osu!mania", () => gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Classic));
            AddUntilStep("no tilt", () => !shown(cameraSlider("Lane tilt")));

            string tiltLabel()
            {
                var bar = cameraSlider("Lane tilt");
                return bar.LabelFormat(bar.Current.Value).ToString();
            }
        }

        /// <summary>
        /// The kiai lift (research/topic-camera-motion.md C1) is off by default, and a preference outside the presets.
        /// </summary>
        [Test]
        public void TestKiaiLiftIsOffByDefault()
        {
            AddAssert("off", () =>
            {
                using (var config = create())
                    return new[] { new HighwaySettings().KiaiLift, config.Highway.Current.KiaiLift };
            }, () => Is.EqualTo(new[] { 0f, 0f }));

            AddAssert("kept, keeps the preset, and a preset leaves it", () =>
            {
                using (var config = create())
                {
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Voltex);
                    config.Highway.SetValue(HighwaySetting.KiaiLift, 0.3f);
                }

                using (var config = create())
                {
                    var kept = (config.Highway.Current.KiaiLift, config.Highway.Get<HighwayPreset>(HighwaySetting.Preset));
                    config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
                    return (kept, config.Highway.Current.KiaiLift);
                }
            }, () => Is.EqualTo(((0.3f, HighwayPreset.Voltex), 0.3f)));
        }

        private FormSliderBar<float> cameraSlider(string caption) => this.ChildrenOfType<FormSliderBar<float>>().Single(b => b.Caption.ToString() == caption);

        private FormEnumDropdown<SpeedScale> speedScaleDropdown => this.ChildrenOfType<FormEnumDropdown<SpeedScale>>().Single();

        private OrthoSettingsGroup group(string title) => this.ChildrenOfType<OrthoSettingsGroup>().Single(g => g.Title.ToString() == title);

        /// <summary>
        /// The panel's groups of a style, by title in the panel's order.
        /// </summary>
        private string[] groups(SettingsGroupStyle style) => this.ChildrenOfType<OrthoSettingsGroup>().Where(g => g.Style == style).Select(g => g.Title.ToString()).ToArray();

        private string[] groupsShowing() => this.ChildrenOfType<OrthoSettingsGroup>().Where(g => g.Style == SettingsGroupStyle.Fold && g.IsPresent && g.ContentShown)
                                                .Select(g => g.Title.ToString()).ToArray();

        private void clickFold(string title) => group(title).ChildrenOfType<OsuClickableContainer>().First().TriggerClick();

        /// <summary>
        /// The caption of the first setting in a group.
        /// </summary>
        private string firstSettingIn(string title) => group(title).ChildrenOfType<SettingsItemV2>().First().Control.FilterTerms.First().ToString();

        /// <summary>
        /// The section a setting is in.
        /// </summary>
        private static string groupAbove(Drawable setting)
        {
            for (var parent = setting.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is OrthoSettingsGroup { Style: SettingsGroupStyle.Section } section)
                    return section.Title.ToString();
            }

            return string.Empty;
        }

        /// <summary>
        /// Whether a setting is on the panel now: it and everything around it present.
        /// </summary>
        private static bool onScreen(Drawable setting)
        {
            for (Drawable? drawable = setting; drawable != null; drawable = drawable.Parent)
            {
                if (!drawable.IsPresent)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// "Speed scale" (Alpha 2026-11-07): the chosen game's slider shows the one stored speed in its numbers, moving it sets the
        /// speed, and every scale follows a change made elsewhere (F3/F4, another scale).
        /// </summary>
        [Test]
        public void TestSpeedScale()
        {
            AddStep("osu!mania speed 29", () => gameConfig.SetValue(ManiaRulesetSetting.ScrollSpeed, 29.0));
            showPanel();

            AddStep("Chunithm's numbers", () => gameConfig.Highway.SetValue(HighwaySetting.SpeedScale, SpeedScale.Chunithm));
            AddAssert("only its slider shown", () => shown(speedBar("Speed (Chunithm)")) && !shown(speedBar("Scroll speed")));
            AddAssert("speed 29 is about Chunithm 13.75 to 14", () => speedBar("Speed (Chunithm)").Current.Value, () => Is.InRange(13.5, 14.25));

            AddStep("slide to Chunithm 10", () => speedBar("Speed (Chunithm)").Current.Value = 10);
            AddAssert("the speed is Chunithm 10's 549 ms", () => gameConfig.Get<double>(ManiaRulesetSetting.ScrollSpeed),
                () => Is.InRange(20.0, 22.0)); // 11485 / 549 = 20.9, times the skin's hit position factor
            AddAssert("osu!mania's slider follows", () => speedBar("Scroll speed").Current.Value,
                () => Is.EqualTo(Math.Round(gameConfig.Get<double>(ManiaRulesetSetting.ScrollSpeed), 1)).Within(1e-9));
            AddAssert("the green number follows", () => speedBar("Green number (IIDX)").Current.Value, () => Is.InRange(310, 350));

            AddStep("Chunithm 15.5", () => speedBar("Speed (Chunithm)").Current.Value = 15.5);
            AddAssert("snaps to a whole number past 15", () => speedBar("Speed (Chunithm)").Current.Value, () => Is.EqualTo(16));

            // A wall that keeps the visible time makes notes cross the track faster than the fastest speed can show them.
            AddStep("a high wall that keeps the visible time", () =>
            {
                gameConfig.Highway.SetValue(HighwaySetting.WallKeepsVisibleTime, true);
                gameConfig.Highway.SetValue(HighwaySetting.Wall, 0.6f);
            });
            AddStep("slide to Chunithm 19", () => speedBar("Speed (Chunithm)").Current.Value = 19);
            AddAssert("the fastest speed", () => gameConfig.Get<double>(ManiaRulesetSetting.ScrollSpeed), () => Is.EqualTo(SpeedScales.MAX_MANIA_SPEED));
            AddAssert("the slider shows the number reached", () => speedBar("Speed (Chunithm)").Current.Value, () => Is.LessThan(19));
            AddStep("no wall", () => gameConfig.Highway.SetValue(HighwaySetting.Wall, 0f));

            AddStep("speed 8 elsewhere", () => gameConfig.SetValue(ManiaRulesetSetting.ScrollSpeed, 8.0));
            AddAssert("Chunithm's slider follows", () => speedBar("Speed (Chunithm)").Current.Value, () => Is.InRange(3.5, 4.0));
        }

        private static bool shown(Drawable setting) => setting.FindClosestParent<SettingsItemV2>()?.Alpha == 1;

        private FormSliderBar<double> speedBar(string caption) => this.ChildrenOfType<FormSliderBar<double>>().Single(b => b.Caption.ToString() == caption);

        /// <summary>
        /// "Share osu!mania's keys" (Alpha 2026-11-07): while on, the panel keeps the two key binding sections equal; off, each
        /// keeps its own; on again, they settle on osu!mania's (both were changed from the defaults).
        /// </summary>
        [Test]
        public void TestKeySharingSwitch()
        {
            AddStep("both 4K on Z F J K", () => Realm.Write(r =>
            {
                setKeys(r, ManiaRuleset.SHORT_NAME, InputKey.Z);
                setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.Z);
            }));
            showPanel();

            AddStep("osu!mania's first key to Q", () => Realm.Write(r => setKeys(r, ManiaRuleset.SHORT_NAME, InputKey.Q)));
            AddUntilStep("maniaortho follows", () => keysOf(ManiaOrthoRuleset.SHORT_NAME), () => Is.EqualTo("Q F J K"));

            AddStep("stop sharing", () => gameConfig.Highway.SetValue(HighwaySetting.ShareManiaKeys, false));
            AddAssert("the panel stopped mirroring", () => this.ChildrenOfType<ManiaOrthoSettingsSubsection>().Single().MirroringKeys, () => Is.False);
            AddStep("osu!mania's first key to W", () => Realm.Write(r => setKeys(r, ManiaRuleset.SHORT_NAME, InputKey.W)));
            AddStep("maniaortho's first key to E", () => Realm.Write(r => setKeys(r, ManiaOrthoRuleset.SHORT_NAME, InputKey.E)));
            AddWaitStep("let a copy happen if it would", 5);
            AddAssert("each kept its own", () => (keysOf(ManiaRuleset.SHORT_NAME), keysOf(ManiaOrthoRuleset.SHORT_NAME)),
                () => Is.EqualTo(("W F J K", "E F J K")));

            AddStep("share again", () => gameConfig.Highway.SetValue(HighwaySetting.ShareManiaKeys, true));
            AddAssert("the panel mirrors again", () => this.ChildrenOfType<ManiaOrthoSettingsSubsection>().Single().MirroringKeys);
            AddUntilStep("both on osu!mania's", () => (keysOf(ManiaRuleset.SHORT_NAME), keysOf(ManiaOrthoRuleset.SHORT_NAME)),
                () => Is.EqualTo(("W F J K", "W F J K")));
        }

        private const int key_variant = 4;

        /// <summary>
        /// Gives each of a ruleset's 4K columns one key, <paramref name="first"/> on the first column and F J K on the others,
        /// changing the rows in place as osu!'s key binding settings do.
        /// </summary>
        private static void setKeys(Realm realm, string rulesetName, InputKey first)
        {
            var keys = new[] { first, InputKey.F, InputKey.J, InputKey.K };
            var existing = realm.All<RealmKeyBinding>().Where(b => b.RulesetName == rulesetName && b.Variant == key_variant).AsEnumerable().ToLookup(b => b.ActionInt);

            for (int i = 0; i < keys.Length; i++)
            {
                int action = (int)ManiaAction.Key1 + i;
                var rows = existing[action].ToArray();

                if (rows.Length == 1)
                    rows[0].KeyCombination = new KeyCombination(keys[i]);
                else
                {
                    foreach (var row in rows)
                        realm.Remove(row);

                    realm.Add(new RealmKeyBinding(action, new KeyCombination(keys[i]), rulesetName, key_variant));
                }
            }
        }

        /// <summary>
        /// A ruleset's 4K keys by column, each column's keys in their order.
        /// </summary>
        private string keysOf(string rulesetName) => Realm.Run(r => string.Join(' ', r.All<RealmKeyBinding>()
                                                                                       .Where(b => b.RulesetName == rulesetName && b.Variant == key_variant)
                                                                                       .AsEnumerable()
                                                                                       .OrderBy(b => b.ActionInt)
                                                                                       .Select(b => string.Join('+', b.KeyCombination.Keys))));

        /// <param name="skin">A skin to show the panel with, in place of the game's.</param>
        private void showPanel(ISkinSource? skin = null)
        {
            AddStep("show settings", () =>
            {
                var panel = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 480,
                    AutoSizeAxes = Axes.Y,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background4 },
                        // Searched as the settings overlay searches its sections.
                        search = new SearchContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Child = new ManiaOrthoSettingsSubsection(new ManiaOrthoRuleset()),
                        },
                    }
                };

                Child = skin == null
                    ? panel
                    : new DependencyProvidingContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        CachedDependencies = new (Type, object)[] { (typeof(ISkinSource), skin) },
                        Child = panel,
                    };
            });

            AddUntilStep("panel loaded", () => Child.IsLoaded);
        }

        /// <summary>
        /// The game's config for this ruleset, which the settings panel shows.
        /// </summary>
        private ManiaOrthoRulesetConfigManager gameConfig => (ManiaOrthoRulesetConfigManager)RulesetConfigs.GetConfigFor(new ManiaOrthoRuleset()).AsNonNull();

        private void speedWithoutWall()
        {
            gameConfig.SetValue(ManiaRulesetSetting.ScrollSpeed, 29.0);
            gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
            gameConfig.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Chunithm);
            gameConfig.Highway.SetValue(HighwaySetting.Wall, 0f); // a game with a wall leaves it to the player
            gameConfig.Highway.SetValue(HighwaySetting.SpeedScale, SpeedScale.Mania);
            gameConfig.Highway.SetValue(HighwaySetting.WallKeepsVisibleTime, false);
        }

        // Sliders hidden with their speed scale never load their text (null).
        private string speedLabel() => this.ChildrenOfType<OsuSpriteText>().Select(t => t.Current.Value).FirstOrDefault(t => t?.Contains("(speed ", StringComparison.Ordinal) == true) ?? string.Empty;

        /// <summary>
        /// A legacy skin's mania settings, as far as the speed label asks: HitPosition 480 (the line at the bottom) for 7K,
        /// osu!'s default for every other key count.
        /// </summary>
        private class SevenKeyLineAtTheBottom : ISkinSource
        {
            public event Action? SourceChanged
            {
                add { }
                remove { }
            }

            public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
                where TLookup : notnull
                where TValue : notnull
            {
                if (lookup is LegacyManiaSkinConfigurationLookup { Lookup: LegacyManiaSkinConfigurationLookups.HitPosition } mania && typeof(TValue) == typeof(float))
                    return (IBindable<TValue>)(object)new Bindable<float>(mania.TotalColumns == 7 ? 0 : LegacyManiaSkinConfiguration.DEFAULT_HIT_POSITION);

                return null;
            }

            public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

            public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;

            public ISample? GetSample(ISampleInfo sampleInfo) => null;

            public ISkin? FindProvider(Func<ISkin, bool> lookupFunction) => lookupFunction(this) ? this : null;

            public IEnumerable<ISkin> AllSources => new ISkin[] { this };
        }

        /// <summary>
        /// The captions of the wall sliders the panel shows.
        /// </summary>
        private string[] shownWallSliders() => this.ChildrenOfType<SettingsItemV2>()
                                                   .Where(item => item.IsPresent && item.Alpha > 0)
                                                   .SelectMany(item => item.ChildrenOfType<FormSliderBar<float>>().Select(s => s.Caption.ToString())
                                                                           .Concat(item.ChildrenOfType<FormSliderBar<int>>().Select(s => s.Caption.ToString())))
                                                   .Where(caption => caption.StartsWith("Wall", StringComparison.Ordinal))
                                                   .ToArray();

        private FormSliderBar<T> wallSlider<T>(string caption)
            where T : struct, INumber<T>, IMinMaxValue<T>
            => this.ChildrenOfType<FormSliderBar<T>>().Single(s => s.Caption.ToString() == caption);

        private ManiaOrthoRulesetConfigManager create() => new ManiaOrthoRulesetConfigManager(store, new ManiaOrthoRuleset().RulesetInfo);

        // Bindables round to their precision (0.01), so compare with a tolerance.
        private static bool near(float a, float b) => System.Math.Abs(a - b) < 1e-4f;

        /// <summary>
        /// How tall a note on the judgement line is drawn with <paramref name="preset"/>, as a share of the screen height.
        /// </summary>
        private static float heightAtLine(HighwayPreset preset)
        {
            var settings = HighwayPresets.Get(preset);
            var projection = new HighwayProjection(settings.ToLayout(), new osuTK.Vector2(1920, 1080));

            return (projection.YAt(0) - projection.YAt(settings.NoteThickness)) / 1080;
        }

        private static bool same(HighwaySettings a, HighwaySettings b)
            => a.CameraMode == b.CameraMode
               && near(a.LaneTilt, b.LaneTilt)
               && near(a.LaneLength, b.LaneLength)
               && near(a.Approach, b.Approach)
               && near(a.JudgementLineHeight, b.JudgementLineHeight)
               && near(a.TrackWidth, b.TrackWidth)
               && near(a.Wall, b.Wall)
               && near(a.NoteThickness, b.NoteThickness);
    }
}
