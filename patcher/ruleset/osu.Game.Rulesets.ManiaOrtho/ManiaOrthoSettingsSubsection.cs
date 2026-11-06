// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.ScoreServer;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring.Legacy;
using osu.Game.Skinning;
using osu.Game.Utils;

namespace osu.Game.Rulesets.ManiaOrtho
{
    /// <summary>
    /// This ruleset's section in the settings overlay: mania's scroll speed and note colouring, then the highway.
    /// Mania's scroll direction is left out on purpose: the camera decides the direction (SPEC section 3).
    /// </summary>
    public partial class ManiaOrthoSettingsSubsection : RulesetSettingsSubsection
    {
        [Resolved]
        private ISkinSource? skin { get; set; }

        [Resolved]
        private IBindable<WorkingBeatmap>? beatmap { get; set; }

        [Resolved]
        private IBindable<IReadOnlyList<Mod>>? mods { get; set; }

        [Resolved]
        private OsuGame? game { get; set; }

        [Resolved]
        private IAPIProvider? api { get; set; }

        [Resolved]
        private RealmAccess? realm { get; set; }

        private SettingsItemV2 scoreServerAddressItem = null!;
        private Bindable<string> scoreServerAddress = null!;
        private ScoreServerLinkButton scoreServerLink = null!;
        private IBindable<APIState>? apiState;
        private IBindable<APIUser>? apiUser;

        /// <summary>
        /// Keeps this ruleset's section of the key binding settings on osu!mania's keys, which gameplay uses. Here because this
        /// is the ruleset's one part of the game that lives with the settings, where keys are changed.
        /// </summary>
        private KeyBindingMirror? keyBindingMirror;

        private HighwayConfigManager highway = null!;
        private FormSliderBar<double> scrollSpeed = null!;

        /// <summary>
        /// The values the scroll speed's visible time depends on, kept to show it again when one changes.
        /// </summary>
        private readonly List<IUnbindable> visibleTimeInputs = new List<IUnbindable>();

        private Bindable<float> wall = null!;
        private Bindable<WallScale> wallScale = null!;

        /// <summary>
        /// The wall in each scale's units, kept in step with <see cref="wall"/> (stored finer than any slider moves).
        /// </summary>
        private readonly BindableFloat percentWall = new BindableFloat { MinValue = 0, MaxValue = HighwayScroll.MAX_WALL, Precision = 0.01f };

        private readonly BindableInt chunithmWall = new BindableInt { MinValue = 0, MaxValue = WallScales.MAX_CHUNITHM_STEP };

        private readonly BindableInt iidxWall = new BindableInt { MinValue = 0, MaxValue = 1000 };
        private readonly BindableFloat laneWall = new BindableFloat { MinValue = 0, MaxValue = 1, Precision = 0.01f };

        /// <summary>
        /// One wall slider per scale; only the chosen scale's is shown.
        /// </summary>
        private readonly Dictionary<WallScale, SettingsItemV2> wallSliders = new Dictionary<WallScale, SettingsItemV2>();

        private bool syncingWall;

        public ManiaOrthoSettingsSubsection(ManiaOrthoRuleset ruleset)
            : base(ruleset)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var config = (ManiaOrthoRulesetConfigManager)Config;
            highway = config.Highway;
            wall = highway.GetBindable<float>(HighwaySetting.Wall);
            wallScale = highway.GetBindable<WallScale>(HighwaySetting.WallScale);

            const string wall_keys = "In gameplay, Shift with the scroll speed keys (F3 and F4 unless you changed them) moves it.";

            wallSliders[WallScale.Percent] = new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = "Wall",
                HintText = "How much of the far end of the track's depth is covered. " + wall_keys,
                Current = percentWall,
                DisplayAsPercentage = true,
                KeyboardStep = 0.01f,
            })
            {
                Keywords = new[] { "sudden", "cover", "field wall" },
            };

            wallSliders[WallScale.Chunithm] = new SettingsItemV2(new FormSliderBar<int>
            {
                Caption = "Wall (Chunithm)",
                HintText = "Chunithm's field wall: 0 is none, each step covers more of the far end of the track, as in the arcade, up "
                           + "to 14. " + wall_keys,
                Current = chunithmWall,
                KeyboardStep = 1,
            })
            {
                Keywords = new[] { "field wall", "cover", "chunithm" },
            };

            wallSliders[WallScale.Iidx] = new SettingsItemV2(new FormSliderBar<int>
            {
                Caption = "Wall (white number)",
                HintText = "IIDX's SUDDEN+ white number: thousandths of the lane's height on screen covered from the far end. Its most "
                           + "depends on the camera. " + wall_keys,
                Current = iidxWall,
                KeyboardStep = 10,
            })
            {
                Keywords = new[] { "sudden", "white number", "lane cover", "iidx", "beatoraja" },
            };

            wallSliders[WallScale.Lane] = new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = "Wall (% of the lane)",
                HintText = "How much of the lane's height on screen is covered from the far end, as osu!mania's Cover and Quaver's lane "
                           + "cover measure it. " + wall_keys,
                Current = laneWall,
                DisplayAsPercentage = true,
                KeyboardStep = 0.01f,
            })
            {
                Keywords = new[] { "cover", "lane cover", "osu!mania", "quaver" },
            };

            Children = new Drawable[]
            {
                new SettingsItemV2(scrollSpeed = new FormSliderBar<double>
                {
                    Caption = RulesetSettingsStrings.ScrollSpeed,
                    HintText = "Shown as the time notes are visible, from the wall (or the far end) to the judgement line, with the "
                               + "current skin, wall and camera at the selected beatmap's key count. In gameplay, F3 and F4 change it.",
                    Current = config.GetBindable<double>(ManiaRulesetSetting.ScrollSpeed),
                    KeyboardStep = 1,
                    LabelFormat = scrollSpeedLabel,
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = RulesetSettingsStrings.TimingBasedColouring,
                    Current = config.GetBindable<bool>(ManiaRulesetSetting.TimingBasedNoteColouring),
                })
                {
                    Keywords = new[] { "color" },
                },
                // As osu!mania shows it: on screen buttons for each key along the bottom, for touch screens and the mouse.
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = RulesetSettingsStrings.TouchOverlay,
                    Current = config.GetBindable<bool>(ManiaRulesetSetting.TouchOverlay),
                }),
                new SettingsItemV2(new FormEnumDropdown<HighwayPreset>
                {
                    Caption = "Highway preset",
                    HintText = "Chunithm, Voltex (SDVX), Parallel or Classic (osu!mania's layout). Changing any value below makes it Custom.",
                    Current = highway.GetBindable<HighwayPreset>(HighwaySetting.Preset),
                })
                {
                    Keywords = new[] { "camera", "chunithm", "sdvx", "voltex" },
                },
                new SettingsItemV2(new FormEnumDropdown<HighwayCameraMode>
                {
                    Caption = "Camera",
                    Current = highway.GetBindable<HighwayCameraMode>(HighwaySetting.CameraMode),
                })
                {
                    Keywords = new[] { "perspective", "tilt", "flat" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Far edge width",
                    HintText = "Width of the far end of the track compared to the judgement line. Lower is a stronger perspective.",
                    Current = highway.GetBindable<float>(HighwaySetting.FarWidthRatio),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.01f,
                }),
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Track width",
                    HintText = "Width of the track at the judgement line, as a share of the screen width. A ported skin at \"Skin position "
                               + "and width\" uses the skin's own width instead.",
                    Current = highway.GetBindable<float>(HighwaySetting.TrackWidth),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.01f,
                }),
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Judgement line height",
                    HintText = "Distance of the judgement line above the bottom of the screen (lift).",
                    Current = highway.GetBindable<float>(HighwaySetting.JudgementLineHeight),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.01f,
                })
                {
                    Keywords = new[] { "lift", "hit position" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Far edge height",
                    HintText = "Distance of the far end of the track below the top of the screen.",
                    Current = highway.GetBindable<float>(HighwaySetting.FarEdgeHeight),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.01f,
                }),
                new SettingsItemV2(new FormEnumDropdown<WallScale>
                {
                    Caption = "Wall scale",
                    HintText = "The units the wall is set in and moved by in gameplay: a percent of the track, Chunithm's field wall "
                               + "steps, IIDX's white number, or osu!mania's and Quaver's lane cover. Choosing a game also gives the wall "
                               + "that game's speed behaviour (below): Chunithm, osu!mania and Quaver keep the speed, IIDX the visible time. "
                               + "Percent leaves it as it is.",
                    Current = wallScale,
                })
                {
                    Keywords = new[] { "wall units", "sudden", "white number", "chunithm", "iidx", "quaver", "cover" },
                },
                wallSliders[WallScale.Percent],
                wallSliders[WallScale.Chunithm],
                wallSliders[WallScale.Iidx],
                wallSliders[WallScale.Lane],
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Wall keeps visible time",
                    HintText = "On: notes appear at the wall's edge and slow down so they stay visible for the same time. "
                               + "Off: notes keep their speed and come out from under the wall, which covers them, as in Chunithm.",
                    Current = highway.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime),
                }),
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Far fade",
                    HintText = "Notes fade in over this share of the visible track, down from the far edge or the wall, instead of "
                               + "appearing at full strength. Off in Chunithm and SDVX. Highway and Skin (ported) notes only, not under the "
                               + "flat camera; Hidden and Fade In still apply on top.",
                    Current = highway.GetBindable<float>(HighwaySetting.FarFade),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.01f,
                })
                {
                    Keywords = new[] { "fade in", "appear", "yarg" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Lane guides",
                    HintText = "Thin lines along the lane boundaries that fade into the distance, as in Chunithm.",
                    Current = highway.GetBindable<bool>(HighwaySetting.LaneGuides),
                })
                {
                    Keywords = new[] { "guidelines", "dividers", "column lines" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Use skin hit target",
                    HintText = "On: the skin's hit target marks the judgement line. Off: a Chunithm-style judgement line does.",
                    Current = highway.GetBindable<bool>(HighwaySetting.SkinHitTarget),
                })
                {
                    Keywords = new[] { "judgement line", "receptor", "stage hint" },
                },
                new SettingsItemV2(new FormEnumDropdown<HighwayNoteStyle>
                {
                    Caption = "Notes",
                    HintText = "Skin (ported): your skin's note images, redrawn sharp on the track. Skin (warped): your skin's "
                               + "notes tilted with the rest of its stage. Highway: notes drawn on the track at the preset's thickness, in "
                               + "your skin's colours or white and pink lanes. Ported needs a stable (legacy) skin. Applies from the next play.",
                    Current = highway.GetBindable<HighwayNoteStyle>(HighwaySetting.NoteStyle),
                })
                {
                    Keywords = new[] { "note skin", "thickness", "native" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Note thickness",
                    HintText = "How deep Highway notes are, as a share of the track's length. Chunithm's notes are 2.55%.",
                    Current = highway.GetBindable<float>(HighwaySetting.NoteThickness),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.0005f,
                })
                {
                    Keywords = new[] { "note size", "note height" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Hold opacity",
                    HintText = "How solid hold note bodies are, so notes and the track show through long holds. Their heads and tails "
                               + "stay solid. Highway and Skin (ported) notes only.",
                    Current = highway.GetBindable<float>(HighwaySetting.HoldOpacity),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.01f,
                })
                {
                    Keywords = new[] { "long note", "ln", "transparency", "hold body", "sekai" },
                },
                new SettingsItemV2(new FormEnumDropdown<HighwayHitEffectStyle>
                {
                    Caption = "Hit effects",
                    HintText = "Highway: bursts on the track at the judgement line, gold for Perfect and orange for Great, that also "
                               + "light the lane's piece of the line, and a glow while you hold a note. Skin (ported): your skin's hit and "
                               + "hold lighting, upright at the line. Skin (warped): your skin's hit explosions, tilted with the track.",
                    Current = highway.GetBindable<HighwayHitEffectStyle>(HighwaySetting.HitEffects),
                })
                {
                    Keywords = new[] { "hit lighting", "explosion", "burst", "glow" },
                },
                new SettingsItemV2(new FormEnumDropdown<HighwayTrackStyle>
                {
                    Caption = "Track",
                    HintText = "Highway: a dark floor with teal side rails, and a beam rising from the judgement line in each lane you "
                               + "press. Skin (ported): your skin's stage rebuilt on the track from its images, with its keys under the line. "
                               + "Skin (warped): your skin's stage, tilted with the track.",
                    Current = highway.GetBindable<HighwayTrackStyle>(HighwaySetting.Track),
                })
                {
                    Keywords = new[] { "stage", "lane background", "column", "receptor", "key area", "floor", "beam", "lane lighting", "port" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Track opacity",
                    HintText = "How solid the lanes' background is: the Highway floor, or your skin's lane colours under Skin (ported). "
                               + "Lower shows more of the beatmap's background through the lanes; the background dim still applies. Lines, "
                               + "rails and lighting stay.",
                    Current = highway.GetBindable<float>(HighwaySetting.TrackOpacity),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.01f,
                })
                {
                    Keywords = new[] { "lane transparency", "lane opacity", "floor", "background", "brightness", "sekai" },
                },
                new SettingsItemV2(new FormEnumDropdown<KeyBeamLength>
                {
                    Caption = "Key beam",
                    HintText = "How far up the Highway track the beam in a pressed lane reaches. Long: most of the track. Half and "
                               + "Quarter: that share of the track's height on screen, as DJMAX's short beams. A skin's lane lights keep "
                               + "the skin's length.",
                    Current = highway.GetBindable<KeyBeamLength>(HighwaySetting.KeyBeam),
                })
                {
                    Keywords = new[] { "beam", "lane beam", "key beam", "lighting", "djmax", "iidx", "length" },
                },
                new SettingsItemV2(new FormEnumDropdown<SkinKeyArea>
                {
                    Caption = "Skin key area",
                    HintText = "Where a ported skin's keys go when its judgement line sits high (IIDX-style skins). Panel and Floor lift "
                               + "the line to the skin's height; the others keep the camera's line.",
                    Current = highway.GetBindable<SkinKeyArea>(HighwaySetting.KeyArea),
                })
                {
                    Keywords = new[] { "keys", "receptor", "iidx", "hit position", "lift", "panel" },
                },
                new SettingsItemV2(new FormEnumDropdown<SkinStageFit>
                {
                    Caption = "Skin stage position",
                    HintText = "Camera: the preset's track, centred. Skin position: the preset's width, centred where the skin puts "
                               + "its stage (narrowed if it would not fit there). Skin position and width (default): the skin's own stage, "
                               + "each lane as wide at the judgement line as the skin draws it.",
                    Current = highway.GetBindable<SkinStageFit>(HighwaySetting.StageFit),
                })
                {
                    Keywords = new[] { "column start", "stage width", "centre", "center", "left" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Side stage lean",
                    HintText = "For a skin that puts its stage to one side: how far its far end leans in towards the screen's centre. "
                               + "0% keeps it upright above the stage, 100% makes its lanes meet above the screen's centre. The judgement "
                               + "line stays where the skin puts it. Ported skins at either skin stage position, perspective cameras only.",
                    Current = highway.GetBindable<float>(HighwaySetting.SideStageLean),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.05f,
                })
                {
                    Keywords = new[] { "vanishing point", "perspective", "lean", "skew", "converge", "iidx", "stepmania", "side" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Side stage pull",
                    HintText = "For a skin that puts its stage to one side: how far the whole stage moves towards the screen's centre. "
                               + "0% keeps it exactly where the skin puts it, 100% centres it. Ported skins at either skin stage position only.",
                    Current = highway.GetBindable<float>(HighwaySetting.SideStagePull),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.05f,
                })
                {
                    Keywords = new[] { "column start", "position", "centre", "center", "offset", "side" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Skin note proportions",
                    HintText = "On: ported notes are as tall at the judgement line, for their width, as your skin draws them. Off: they "
                               + "use the note thickness above.",
                    Current = highway.GetBindable<bool>(HighwaySetting.SkinNoteProportions),
                })
                {
                    Keywords = new[] { "note height", "note size", "thickness" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Kiai sway",
                    HintText = "Off by default. In kiai sections the far end of the track swings from side to side with the beat; the "
                               + "judgement line and everything below it stay still. Purely cosmetic.",
                    Current = highway.GetBindable<float>(HighwaySetting.KiaiSway),
                    DisplayAsPercentage = true,
                    KeyboardStep = 0.01f,
                })
                {
                    Keywords = new[] { "camera motion", "sway", "kiai", "shake", "movement" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Send plays to my score server",
                    HintText = "Off by default. Each pass and fail also goes to your own score server (orthomania's, started with "
                               + "server/scripts/server), which rates it with osu!'s live osu!mania pp and keeps an unofficial total. "
                               + "Nothing is sent to osu!. If the server is not running, plays wait and go with the next one.",
                    Current = config.ScoreServer.GetBindable<bool>(ScoreServerSetting.ScoreServerEnabled),
                })
                {
                    Keywords = new[] { "pp", "performance", "ranking", "profile", "server", "upload" },
                },
                scoreServerAddressItem = new SettingsItemV2(new FormTextBox
                {
                    Caption = "Score server address",
                    HintText = $"Where your score server listens ({ScoreServerConfigManager.DEFAULT_ADDRESS} on this computer). osu!'s own "
                               + "servers (ppy.sh) are refused. Linked with osu! (below), your plays go to your account there.",
                    Current = scoreServerAddress = config.ScoreServer.GetBindable<string>(ScoreServerSetting.ScoreServerAddress),
                })
                {
                    Keywords = new[] { "pp", "server", "address", "url" },
                },
                scoreServerLink = new ScoreServerLinkButton(config.ScoreServer, scoreServerAddress),
                new SettingsButtonV2
                {
                    Text = "Open my score page",
                    TooltipText = "Your unofficial pp, best plays and latest plays, as your score server shows them.",
                    Keywords = new[] { "pp", "profile", "server", "ranking" },
                    Action = openScorePage,
                },
            };
        }

        private void showScoreServerSignIn()
            => scoreServerAddressItem.Note.Value = scoreServerLink.Message.Value
                                                   ?? ScoreServerSignIn.Describe(((ManiaOrthoRulesetConfigManager)Config).ScoreServer, api, scoreServerAddress.Value);

        private void openScorePage()
        {
            var address = ScoreServerClient.ParseAddress(((ManiaOrthoRulesetConfigManager)Config).ScoreServer.Get<string>(ScoreServerSetting.ScoreServerAddress), out _);

            if (address != null)
                game?.OpenUrlExternally(address.AbsoluteUri);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Whose account plays go to: the account the game is linked to.
            apiState = api?.State.GetBoundCopy();
            apiUser = api?.LocalUser.GetBoundCopy();
            scoreServerAddress.BindValueChanged(_ => showScoreServerSignIn());
            scoreServerLink.Message.BindValueChanged(_ => showScoreServerSignIn());
            ((ManiaOrthoRulesetConfigManager)Config).ScoreServer.GetBindable<string>(ScoreServerSetting.LinkToken).BindValueChanged(_ => showScoreServerSignIn());
            apiState?.BindValueChanged(_ => Schedule(showScoreServerSignIn));
            apiUser?.BindValueChanged(_ => Schedule(showScoreServerSignIn));
            showScoreServerSignIn();

            if (realm != null)
                keyBindingMirror = new KeyBindingMirror(realm);

            showVisibleTimeOnChange(highway.GetBindable<float>(HighwaySetting.Wall));

            // The other scales follow the wall, and set it when moved. The screen-based ones depend on the camera.
            wall.BindValueChanged(_ => showWallInOtherScales(null));
            showWallInOtherScalesOnChange(highway.GetBindable<float>(HighwaySetting.FarWidthRatio));
            showWallInOtherScalesOnChange(highway.GetBindable<HighwayCameraMode>(HighwaySetting.CameraMode));
            percentWall.BindValueChanged(e => setWall(WallScale.Percent, (int)Math.Round(e.NewValue * 100)));
            chunithmWall.BindValueChanged(e => setWall(WallScale.Chunithm, e.NewValue));
            iidxWall.BindValueChanged(e => setWall(WallScale.Iidx, e.NewValue));
            laneWall.BindValueChanged(e => setWall(WallScale.Lane, (int)Math.Round(e.NewValue * 100)));

            wallScale.BindValueChanged(e =>
            {
                foreach (var (scale, slider) in wallSliders)
                {
                    slider.CanBeShown.Value = scale == e.NewValue;
                    slider.Alpha = scale == e.NewValue ? 1 : 0;
                }

                showWallInOtherScales(null);
            }, true);
            showVisibleTimeOnChange(highway.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime));
            showVisibleTimeOnChange(highway.GetBindable<HighwayCameraMode>(HighwaySetting.CameraMode));

            if (beatmap != null)
                showVisibleTimeOnChange(beatmap.GetBoundCopy());
            if (mods != null)
                showVisibleTimeOnChange(mods.GetBoundCopy());

            if (skin != null)
                skin.SourceChanged += showVisibleTime;
        }

        private void showWallInOtherScalesOnChange<T>(IBindable<T> input)
        {
            visibleTimeInputs.Add(input);
            input.BindValueChanged(_ => showWallInOtherScales(null));
        }

        /// <summary>
        /// Shows the wall in each scale but <paramref name="except"/> (the one just moved, so it is not nudged under the
        /// pointer by rounding).
        /// </summary>
        private void showWallInOtherScales(WallScale? except)
        {
            if (syncingWall)
                return;

            syncingWall = true;

            try
            {
                float ratio = highway.Current.ToLayout().FarWidthRatio;

                iidxWall.MaxValue = WallScales.MaxUnits(WallScale.Iidx, ratio);
                laneWall.MaxValue = WallScales.MaxUnits(WallScale.Lane, ratio) / 100f;

                if (except != WallScale.Percent)
                    percentWall.Value = WallScales.ToUnits(WallScale.Percent, wall.Value, ratio) / 100f;
                if (except != WallScale.Chunithm)
                    chunithmWall.Value = WallScales.ToUnits(WallScale.Chunithm, wall.Value, ratio);
                if (except != WallScale.Iidx)
                    iidxWall.Value = WallScales.ToUnits(WallScale.Iidx, wall.Value, ratio);
                if (except != WallScale.Lane)
                    laneWall.Value = WallScales.ToUnits(WallScale.Lane, wall.Value, ratio) / 100f;
            }
            finally
            {
                syncingWall = false;
            }
        }

        /// <summary>
        /// Sets the wall from <paramref name="units"/> of <paramref name="scale"/>, moved on its slider.
        /// </summary>
        private void setWall(WallScale scale, int units)
        {
            if (syncingWall)
                return;

            syncingWall = true;

            try
            {
                wall.Value = WallScales.FromUnits(scale, units, highway.Current.ToLayout().FarWidthRatio);
            }
            finally
            {
                syncingWall = false;
            }

            showWallInOtherScales(scale);
        }

        private void showVisibleTimeOnChange<T>(IBindable<T> input)
        {
            visibleTimeInputs.Add(input);
            input.BindValueChanged(_ => showVisibleTime());
        }

        // Setting the label's format shows the value again. Once, however many inputs changed while the panel was hidden.
        private void showVisibleTime() => Scheduler.AddOnce(formatScrollSpeed);

        private void formatScrollSpeed() => scrollSpeed.LabelFormat = scrollSpeedLabel;

        /// <summary>
        /// Mania's label ("405ms (speed 29.0)"), with how long notes are visible on the highway in place of
        /// <c>11485 / speed</c>: the skin's hit position for the selected beatmap's key count (and key mods) changes it as in
        /// mania, and the wall when it keeps the speed. A beatmap's own skin can change it in gameplay too; the toast there shows
        /// the exact value.
        /// </summary>
        private LocalisableString scrollSpeedLabel(double speed)
        {
            var settings = highway.Current;
            var selectedMods = mods?.Value ?? Array.Empty<Mod>();
            float hitPosition = skin == null ? Stage.HIT_TARGET_POSITION : VisibleTime.HitPosition(skin, keyCount(selectedMods));
            double visible = VisibleTime.Of(DrawableManiaRuleset.ComputeScrollTime(speed), hitPosition, ModUtils.CalculateRateWithMods(selectedMods), settings.ToScroll(),
                settings.CameraMode == HighwayCameraMode.Flat);

            return RulesetSettingsStrings.ScrollSpeedTooltip((int)Math.Round(visible), speed);
        }

        /// <summary>
        /// The selected beatmap's key count as mania converts it (4 with none selected).
        /// </summary>
        private int keyCount(IReadOnlyList<Mod> selectedMods)
        {
            if (beatmap == null || beatmap.Value is DummyWorkingBeatmap)
                return 4;

            return ManiaBeatmapConverter.GetColumnCount(LegacyBeatmapConversionDifficultyInfo.FromBeatmapInfo(beatmap.Value.BeatmapInfo), selectedMods);
        }

        protected override void Dispose(bool isDisposing)
        {
            keyBindingMirror?.Dispose();
            if (skin != null)
                skin.SourceChanged -= showVisibleTime;

            // Held in a list, which the drawable's own unbinding does not reach.
            foreach (var input in visibleTimeInputs)
                input.UnbindAll();

            base.Dispose(isDisposing);
        }
    }
}
