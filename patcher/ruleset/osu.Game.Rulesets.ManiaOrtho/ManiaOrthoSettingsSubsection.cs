// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
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
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring.Legacy;
using osu.Game.Skinning;
using osu.Game.Utils;

namespace osu.Game.Rulesets.ManiaOrtho
{
    /// <summary>
    /// This ruleset's section in the settings overlay (Alpha 2026-11-08, organised by game). First "Settings: orthomania", its
    /// own settings: Gameplay, and Other settings folded away. Then "Game preset", the game and under it that game's speed,
    /// lane, sizes and wall, which each game remembers. Mania's scroll direction is left out on purpose: the camera decides
    /// the direction (SPEC section 3).
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

        // Kept: a bound copy is only weakly held by what it is bound to, so one nobody keeps stops hearing changes once collected.
        private IBindable<string>? scoreServerLinkToken;

        /// <summary>
        /// While "Share osu!mania's keys" is on, keeps this ruleset's section of the key binding settings on osu!mania's keys,
        /// which gameplay then uses. Here because this is the ruleset's one part of the game that lives with the settings,
        /// where keys are changed.
        /// </summary>
        private KeyBindingMirror? keyBindingMirror;

        private Bindable<bool> shareManiaKeys = null!;

        /// <summary>
        /// Whether the panel keeps the two key binding sections equal now.
        /// </summary>
        public bool MirroringKeys => keyBindingMirror != null;

        private HighwayConfigManager highway = null!;

        /// <summary>
        /// osu!mania's scroll speed setting, the one stored speed every speed scale shows.
        /// </summary>
        private Bindable<double> maniaSpeed = null!;

        private Bindable<SpeedScale> speedScale = null!;

        /// <summary>
        /// The speed in each scale's numbers, kept in step with <see cref="maniaSpeed"/>; only the chosen scale's slider shows.
        /// </summary>
        private readonly Dictionary<SpeedScale, BindableDouble> speedUnits = new Dictionary<SpeedScale, BindableDouble>();

        private readonly Dictionary<SpeedScale, FormSliderBar<double>> speedBars = new Dictionary<SpeedScale, FormSliderBar<double>>();
        private readonly Dictionary<SpeedScale, SettingsItemV2> speedSliders = new Dictionary<SpeedScale, SettingsItemV2>();

        private bool syncingSpeed;

        private SettingsItemV2 laneWidthSlider = null!;

        private Bindable<HighwayCameraMode> cameraMode = null!;

        /// <summary>
        /// The lane's tilt, shown with where the lane ends on screen, which also depends on its length and the judgement line.
        /// </summary>
        private FormSliderBar<float> laneTiltBar = null!;

        private SettingsItemV2 laneTiltItem = null!;
        private SettingsItemV2 laneLengthItem = null!;
        private SettingsItemV2 approachItem = null!;
        private SettingsItemV2 kiaiLiftItem = null!;
        private Bindable<HighwayWidthMode> widthMode = null!;

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

        private Bindable<HighwayPreset> preset = null!;

        /// <summary>
        /// The wall's settings, hidden under a game without a wall (<see cref="HighwayPresets.HasWall"/>).
        /// </summary>
        private OrthoSettingsGroup wallGroup = null!;

        /// <summary>
        /// The groups that fold, remembered by name in <see cref="HighwaySetting.FoldedGroups"/>.
        /// </summary>
        private readonly List<OrthoSettingsGroup> folds = new List<OrthoSettingsGroup>();

        private Bindable<string> foldedGroups = null!;

        private SettingsItemV2 wallScaleItem = null!;
        private SettingsItemV2 wallKeepsVisibleTimeItem = null!;

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

            maniaSpeed = config.GetBindable<double>(ManiaRulesetSetting.ScrollSpeed);
            speedScale = highway.GetBindable<SpeedScale>(HighwaySetting.SpeedScale);

            foreach (var scale in Enum.GetValues<SpeedScale>())
            {
                var range = SpeedScales.RangeOf(scale);
                var units = speedUnits[scale] = new BindableDouble { MinValue = range.Min, MaxValue = range.Max, Precision = range.Precision };

                var bar = speedBars[scale] = scale == SpeedScale.Mania
                    ? new FormSliderBar<double>
                    {
                        Caption = RulesetSettingsStrings.ScrollSpeed,
                        HintText = "Shown as the time notes are visible, from the wall (or the far end) to the judgement line, with the "
                                   + "current skin, wall and camera at the selected beatmap's key count. In gameplay, F3 and F4 change it.",
                        Current = units,
                        KeyboardStep = 1,
                        LabelFormat = scrollSpeedLabel,
                    }
                    : new FormSliderBar<double>
                    {
                        Caption = SpeedScales.Caption(scale),
                        HintText = SpeedScales.Explain(scale) + " Shown with the time notes are visible. In gameplay, F3 and F4 move one step.",
                        Current = units,
                        KeyboardStep = (float)range.KeyStep,
                        LabelFormat = v => gameSpeedLabel(scale, v),
                    };

                speedSliders[scale] = new SettingsItemV2(bar)
                {
                    Keywords = new[] { "scroll speed", "note speed", "hi-speed", "approach", scale.GetDescription() },
                };
            }

            Children = new Drawable[]
            {
                section("Settings: orthomania",
                    fold("Gameplay", "Gameplay",
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
                        new SettingsItemV2(new FormCheckBox
                        {
                            Caption = RulesetSettingsStrings.TimingBasedColouring,
                            Current = config.GetBindable<bool>(ManiaRulesetSetting.TimingBasedNoteColouring),
                        })
                        {
                            Keywords = new[] { "color" },
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
                        new SettingsItemV2(new FormCheckBox
                        {
                            Caption = "Use skin hit target",
                            HintText = "On: the skin's hit target marks the judgement line. Off: a Chunithm-style judgement line does.",
                            Current = highway.GetBindable<bool>(HighwaySetting.SkinHitTarget),
                        })
                        {
                            Keywords = new[] { "judgement line", "receptor", "stage hint" },
                        }),
                    fold("Other settings", "Other",
                        label("Track and motion",
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
                            kiaiLiftItem = new SettingsItemV2(new FormSliderBar<float>
                            {
                                Caption = "Kiai lift",
                                HintText = "Off by default. In kiai sections the lane lifts towards the top of the screen over three beats, as SDVX's "
                                           + "charts tilt it up at a chorus, and settles back after. 100% lifts its far end to the top edge. The judgement "
                                           + "line stays still and, with the Perspective note approach, notes reach it at the same speed. For a lane that "
                                           + "ends below the top (SDVX), under the perspective camera. Purely cosmetic.",
                                Current = highway.GetBindable<float>(HighwaySetting.KiaiLift),
                                DisplayAsPercentage = true,
                                KeyboardStep = 0.05f,
                            })
                            {
                                Keywords = new[] { "camera motion", "tilt", "kiai", "chorus", "sdvx", "zoom", "pitch", "movement" },
                            }),
                        label("Skin layout",
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
                                           + "use the game preset's note thickness.",
                                Current = highway.GetBindable<bool>(HighwaySetting.SkinNoteProportions),
                            })
                            {
                                Keywords = new[] { "note height", "note size", "thickness" },
                            }),
                        label("Input",
                            new SettingsItemV2(new FormCheckBox
                            {
                                Caption = "Share osu!mania's keys",
                                HintText = "On: maniaortho plays with the keys bound for osu!mania, and changing keys in either mode's key binding "
                                           + "settings changes both. Off: maniaortho keeps keys of its own, under maniaortho in the key binding "
                                           + "settings, starting from osu!mania's. Turning it back on keeps osu!mania's keys, or maniaortho's for a key "
                                           + "count where osu!mania's are still the defaults.",
                                Current = shareManiaKeys = highway.GetBindable<bool>(HighwaySetting.ShareManiaKeys),
                            })
                            {
                                Keywords = new[] { "key bindings", "keybinds", "link", "controls", "osu!mania" },
                            },
                            // As osu!mania shows it: on screen buttons for each key along the bottom, for touch screens and the mouse.
                            new SettingsItemV2(new FormCheckBox
                            {
                                Caption = RulesetSettingsStrings.TouchOverlay,
                                Current = config.GetBindable<bool>(ManiaRulesetSetting.TouchOverlay),
                            })),
                        label("Score server",
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
                            }))),
                section("Game preset",
                    new SettingsItemV2(new FormEnumDropdown<HighwayPreset>
                    {
                        Caption = "Game",
                        HintText = "Plays like a game's own screen: its lane, judgement line, lane or stage width and note thickness, and "
                                   + "shows the speed and the wall in its numbers. Chunithm, SDVX and Project Sekai are tilted highways; IIDX, "
                                   + "DJMAX, Quaver and StepMania are straight-on; osu!mania is its own stage, unchanged. Each game remembers "
                                   + "everything below as you leave it, its speed and wall too; a setting's reset button goes back to the "
                                   + "game's own value. SDVX, Project Sekai, DJMAX and osu!mania have no wall, so its settings are hidden.",
                        Current = preset = highway.GetBindable<HighwayPreset>(HighwaySetting.Preset),
                    })
                    {
                        Keywords = new[]
                        {
                            "game preset", "highway preset", "camera", "chunithm", "sdvx", "voltex", "project sekai", "pjsk", "colorful stage", "iidx",
                            "djmax", "quaver", "stepmania", "osu!mania"
                        },
                    },
                    fold("Speed", "Speed",
                        new SettingsItemV2(new FormEnumDropdown<SpeedScale>
                        {
                            Caption = "Speed scale",
                            HintText = "The numbers the speed is set in: osu!mania's scroll speed, or another game's own speed, each giving the "
                                       + "time that game gives notes. Only the numbers change: the speed is one setting, so switching keeps it.",
                            Current = speedScale,
                        })
                        {
                            Keywords = new[] { "scroll speed", "note speed", "hi-speed", "chunithm", "sdvx", "iidx", "green number", "sekai", "bandori", "arcaea", "djmax", "quaver", "cmod" },
                        },
                        speedSliders[SpeedScale.Mania],
                        speedSliders[SpeedScale.Chunithm],
                        speedSliders[SpeedScale.Sdvx],
                        speedSliders[SpeedScale.Iidx],
                        speedSliders[SpeedScale.Sekai],
                        speedSliders[SpeedScale.BanGDream],
                        speedSliders[SpeedScale.Arcaea],
                        speedSliders[SpeedScale.Djmax],
                        speedSliders[SpeedScale.Quaver],
                        speedSliders[SpeedScale.StepMania]),
                    fold("Lane and camera", "Lane",
                        new SettingsItemV2(new FormEnumDropdown<HighwayCameraMode>
                        {
                            Caption = "Camera",
                            HintText = "Perspective: lanes narrow into the distance, as in Chunithm, SDVX and Project Sekai. Parallel tilt: "
                                       + "lanes stay parallel and notes keep a steady speed. Flat: osu!mania's own stage.",
                            Current = cameraMode = highway.GetBindable<HighwayCameraMode>(HighwaySetting.CameraMode),
                        })
                        {
                            Keywords = new[] { "perspective", "tilt", "flat" },
                        },
                        laneTiltItem = new SettingsItemV2(laneTiltBar = new FormSliderBar<float>
                        {
                            Caption = "Lane tilt",
                            HintText = "How far the lane leans back, pivoting on the judgement line: more tilt ends it lower on the screen, so "
                                       + "notes come from further down. 0° stands it upright. Chunithm 56.3° (ends 16% down, under its score "
                                       + "frame), Project Sekai 57.8° on a longer lane (to the top), SDVX 81.4° (towards the middle). One camera "
                                       + "for every game preset, so an angle looks the same in each.",
                            Current = highway.GetBindable<float>(HighwaySetting.LaneTilt),
                            KeyboardStep = 0.5f,
                            LabelFormat = laneTiltLabel,
                        })
                        {
                            Keywords = new[] { "angle", "lane angle", "lane end", "far edge", "approach angle", "pitch", "chunithm", "sdvx", "sekai" },
                        },
                        laneLengthItem = new SettingsItemV2(new FormSliderBar<float>
                        {
                            Caption = "Lane length",
                            HintText = "How long the lane is, in screen heights: longer narrows its far end, so notes start smaller and speed up "
                                       + "more near the line. Chunithm 3.3, SDVX 11.46, Project Sekai 13. A lane longer than the screen allows "
                                       + "is cut at the top edge. Perspective camera only.",
                            Current = highway.GetBindable<float>(HighwaySetting.LaneLength),
                            KeyboardStep = 0.1f,
                        })
                        {
                            Keywords = new[] { "far edge width", "depth", "perspective", "narrow", "length" },
                        },
                        approachItem = new SettingsItemV2(new FormSliderBar<float>
                        {
                            Caption = "Note approach",
                            HintText = "How notes move down the lane. Perspective (-1): true depth, notes crawl in the distance and rush in "
                                       + "near the line, as in Chunithm and SDVX. Project Sekai (0): they grow by the same factor every moment, so "
                                       + "they move slower near the line. Even (1): a steady speed on screen. Notes still appear and reach the "
                                       + "line at the same times. Perspective camera only.",
                            Current = highway.GetBindable<float>(HighwaySetting.Approach),
                            KeyboardStep = 0.05f,
                            LabelFormat = approachLabel,
                        })
                        {
                            Keywords = new[] { "approach curve", "easing", "note speed", "sekai", "perspective", "acceleration" },
                        },
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
                        }),
                    fold("Lane and note size", "Size",
                        new SettingsItemV2(new FormEnumDropdown<HighwayWidthMode>
                        {
                            Caption = "Width by",
                            HintText = "What sets the Highway track's width: one track width for every key count, as in Chunithm, or a width "
                                       + "per lane, so 4K is narrower than 7K. The default skin's track only: ported skins keep their own lane "
                                       + "widths.",
                            Current = widthMode = highway.GetBindable<HighwayWidthMode>(HighwaySetting.WidthMode),
                        })
                        {
                            Keywords = new[] { "lane width", "column width", "track width", "thin", "thick" },
                        },
                        new SettingsItemV2(new FormSliderBar<float>
                        {
                            Caption = "Track width",
                            HintText = "Width of the track at the judgement line, as a share of the screen width. A ported skin at \"Skin position "
                                       + "and width\" uses the skin's own width instead. With \"Width by\" on Lane, the Highway track uses the lane "
                                       + "width instead.",
                            Current = highway.GetBindable<float>(HighwaySetting.TrackWidth),
                            DisplayAsPercentage = true,
                            KeyboardStep = 0.01f,
                        }),
                        laneWidthSlider = new SettingsItemV2(new FormSliderBar<float>
                        {
                            Caption = "Lane width",
                            HintText = "Width of each lane at the judgement line, as a share of the screen width; the Highway track is that many "
                                       + "lanes wide, up to the whole screen. Ported skins keep their own lane widths.",
                            Current = highway.GetBindable<float>(HighwaySetting.LaneWidth),
                            DisplayAsPercentage = true,
                            KeyboardStep = 0.005f,
                        })
                        {
                            Keywords = new[] { "column width", "thin", "thick", "lane size" },
                        },
                        new SettingsItemV2(new FormSliderBar<float>
                        {
                            Caption = "Note thickness",
                            HintText = "How tall the default skin's notes are (Highway notes), as a share of the track's length. Chunithm's are 2.55%, "
                                       + "StepMania's arrows 14.4%.",
                            Current = highway.GetBindable<float>(HighwaySetting.NoteThickness),
                            DisplayAsPercentage = true,
                            KeyboardStep = 0.0005f,
                        })
                        {
                            Keywords = new[] { "note size", "note height" },
                        }),
                    wallGroup = fold("Wall", "Wall",
                        wallScaleItem = new SettingsItemV2(new FormEnumDropdown<WallScale>
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
                        wallKeepsVisibleTimeItem = new SettingsItemV2(new FormCheckBox
                        {
                            Caption = "Wall keeps visible time",
                            HintText = "On: notes appear at the wall's edge and slow down so they stay visible for the same time. "
                                       + "Off: notes keep their speed and come out from under the wall, which covers them, as in Chunithm.",
                            Current = highway.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime),
                        }))),
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
            scoreServerLinkToken = ((ManiaOrthoRulesetConfigManager)Config).ScoreServer.GetBindable<string>(ScoreServerSetting.LinkToken);
            scoreServerLinkToken.BindValueChanged(_ => showScoreServerSignIn());
            apiState?.BindValueChanged(_ => Schedule(showScoreServerSignIn));
            apiUser?.BindValueChanged(_ => Schedule(showScoreServerSignIn));
            showScoreServerSignIn();

            shareManiaKeys.BindValueChanged(e =>
            {
                keyBindingMirror?.Dispose();
                keyBindingMirror = null;

                // The mirror's first sync settles any difference left from while they were apart.
                if (e.NewValue && realm != null)
                    keyBindingMirror = new KeyBindingMirror(realm);
            }, true);

            showVisibleTimeOnChange(highway.GetBindable<float>(HighwaySetting.Wall));

            // Every scale follows the stored speed, and sets it when moved.
            maniaSpeed.BindValueChanged(_ => showSpeedInScales(null), true);

            foreach (var (scale, units) in speedUnits)
                units.BindValueChanged(e => setSpeed(scale, e.NewValue));

            cameraMode.BindValueChanged(e =>
            {
                bool perspective = e.NewValue == HighwayCameraMode.Perspective;

                show(laneTiltItem, e.NewValue != HighwayCameraMode.Flat);
                show(laneLengthItem, perspective);
                show(approachItem, perspective);
                show(kiaiLiftItem, perspective);
            }, true);

            // Where the lane ends depends on its length, the judgement line and the camera too.
            showLaneEndOnChange(highway.GetBindable<float>(HighwaySetting.LaneLength));
            showLaneEndOnChange(highway.GetBindable<float>(HighwaySetting.JudgementLineHeight));
            showLaneEndOnChange(cameraMode);

            widthMode.BindValueChanged(e =>
            {
                laneWidthSlider.CanBeShown.Value = e.NewValue == HighwayWidthMode.Lane;
                laneWidthSlider.Alpha = e.NewValue == HighwayWidthMode.Lane ? 1 : 0;
            }, true);

            speedScale.BindValueChanged(e =>
            {
                foreach (var (scale, slider) in speedSliders)
                {
                    slider.CanBeShown.Value = scale == e.NewValue;
                    slider.Alpha = scale == e.NewValue ? 1 : 0;
                }
            }, true);

            // The other scales follow the wall, and set it when moved. The screen-based ones depend on the camera.
            wall.BindValueChanged(_ => showWallInOtherScales(null));
            showWallInOtherScalesOnChange(highway.GetBindable<float>(HighwaySetting.LaneTilt));
            showWallInOtherScalesOnChange(highway.GetBindable<float>(HighwaySetting.LaneLength));
            showWallInOtherScalesOnChange(highway.GetBindable<float>(HighwaySetting.Approach));
            showWallInOtherScalesOnChange(highway.GetBindable<float>(HighwaySetting.JudgementLineHeight));
            showWallInOtherScalesOnChange(highway.GetBindable<HighwayCameraMode>(HighwaySetting.CameraMode));
            percentWall.BindValueChanged(e => setWall(WallScale.Percent, (int)Math.Round(e.NewValue * 100)));
            chunithmWall.BindValueChanged(e => setWall(WallScale.Chunithm, e.NewValue));
            iidxWall.BindValueChanged(e => setWall(WallScale.Iidx, e.NewValue));
            laneWall.BindValueChanged(e => setWall(WallScale.Lane, (int)Math.Round(e.NewValue * 100)));

            wallScale.BindValueChanged(_ =>
            {
                showWallSettings();
                showWallInOtherScales(null);
            }, true);
            preset.BindValueChanged(_ => showWallSettings(), true);

            foldedGroups = highway.GetBindable<string>(HighwaySetting.FoldedGroups);
            var folded = new HashSet<string>(foldedGroups.Value.Split(',', StringSplitOptions.RemoveEmptyEntries));

            foreach (var group in folds)
            {
                group.Expanded.Value = !folded.Contains(group.Key);
                group.Expanded.BindValueChanged(_ => foldedGroups.Value = string.Join(',', folds.Where(f => !f.Expanded.Value).Select(f => f.Key)));
            }
            showVisibleTimeOnChange(highway.GetBindable<bool>(HighwaySetting.WallKeepsVisibleTime));
            showVisibleTimeOnChange(highway.GetBindable<HighwayCameraMode>(HighwaySetting.CameraMode));

            if (beatmap != null)
                showVisibleTimeOnChange(beatmap.GetBoundCopy());
            if (mods != null)
                showVisibleTimeOnChange(mods.GetBoundCopy());

            if (skin != null)
                skin.SourceChanged += showVisibleTime;
        }

        /// <summary>
        /// Shows the wall's settings only for a game with a wall, and of the wall sliders only the chosen scale's.
        /// </summary>
        private void showWallSettings()
        {
            bool hasWall = HighwayPresets.HasWall(preset.Value);

            wallGroup.CanBeShown.Value = hasWall;
            show(wallScaleItem, hasWall);
            show(wallKeepsVisibleTimeItem, hasWall);

            foreach (var (scale, slider) in wallSliders)
                show(slider, hasWall && scale == wallScale.Value);
        }

        private static OrthoSettingsGroup section(LocalisableString title, params Drawable[] children)
            => new OrthoSettingsGroup(title, SettingsGroupStyle.Section) { Children = children };

        private OrthoSettingsGroup fold(LocalisableString title, string key, params Drawable[] children)
        {
            var group = new OrthoSettingsGroup(title, SettingsGroupStyle.Fold, key) { Children = children };
            folds.Add(group);
            return group;
        }

        private static OrthoSettingsGroup label(LocalisableString title, params Drawable[] children)
            => new OrthoSettingsGroup(title, SettingsGroupStyle.Label) { Children = children };

        private static void show(SettingsItemV2 item, bool visible)
        {
            item.CanBeShown.Value = visible;
            item.Alpha = visible ? 1 : 0;
        }

        private void showLaneEndOnChange<T>(IBindable<T> input)
        {
            visibleTimeInputs.Add(input);
            input.BindValueChanged(_ => Scheduler.AddOnce(formatLaneTilt));
        }

        // Setting the label's format shows the value again.
        private void formatLaneTilt() => laneTiltBar.LabelFormat = laneTiltLabel;

        /// <summary>
        /// The tilt and where it ends the lane, as a share of the screen's height down from the top.
        /// </summary>
        private LocalisableString laneTiltLabel(float tilt)
        {
            float farY = (highway.Current with { LaneTilt = tilt }).ToLayout().FarY;
            string angle = tilt.ToString("0.0", CultureInfo.InvariantCulture);

            return farY < 0.005f ? $"{angle}° (to the top)" : $"{angle}° (ends {(farY * 100).ToString("0", CultureInfo.InvariantCulture)}% down)";
        }

        private static LocalisableString approachLabel(float approach)
        {
            if (Math.Abs(approach - ApproachCurve.PERSPECTIVE) < 0.001f)
                return "Perspective";
            if (Math.Abs(approach - ApproachCurve.SEKAI) < 0.001f)
                return "Project Sekai";
            if (Math.Abs(approach - ApproachCurve.EVEN) < 0.001f)
                return "Even";

            return approach.ToString("0.00", CultureInfo.InvariantCulture);
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
                var layout = highway.Current.ToLayout();
                float ratio = layout.FarWidthRatio;
                float approach = layout.Approach;

                iidxWall.MaxValue = WallScales.MaxUnits(WallScale.Iidx, ratio, approach);
                laneWall.MaxValue = WallScales.MaxUnits(WallScale.Lane, ratio, approach) / 100f;

                if (except != WallScale.Percent)
                    percentWall.Value = WallScales.ToUnits(WallScale.Percent, wall.Value, ratio, approach) / 100f;
                if (except != WallScale.Chunithm)
                    chunithmWall.Value = WallScales.ToUnits(WallScale.Chunithm, wall.Value, ratio, approach);
                if (except != WallScale.Iidx)
                    iidxWall.Value = WallScales.ToUnits(WallScale.Iidx, wall.Value, ratio, approach);
                if (except != WallScale.Lane)
                    laneWall.Value = WallScales.ToUnits(WallScale.Lane, wall.Value, ratio, approach) / 100f;
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
                var layout = highway.Current.ToLayout();
                wall.Value = WallScales.FromUnits(scale, units, layout.FarWidthRatio, layout.Approach);
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

        /// <summary>
        /// Shows the speed again in every scale and label, after something they depend on changed (skin, wall, camera, beatmap,
        /// mods).
        /// </summary>
        private void formatScrollSpeed()
        {
            showSpeedInScales(null);

            foreach (var (scale, bar) in speedBars)
                bar.LabelFormat = scale == SpeedScale.Mania ? scrollSpeedLabel : v => gameSpeedLabel(scale, v);
        }

        /// <summary>
        /// Shows the speed in each scale but <paramref name="except"/> (the one just moved, so it is not nudged under the
        /// pointer by rounding).
        /// </summary>
        private void showSpeedInScales(SpeedScale? except)
        {
            if (syncingSpeed)
                return;

            syncingSpeed = true;

            try
            {
                var context = speedContext();

                foreach (var (scale, units) in speedUnits)
                {
                    if (scale != except)
                        units.Value = context.ToUnits(scale, maniaSpeed.Value);
                }
            }
            finally
            {
                syncingSpeed = false;
            }
        }

        /// <summary>
        /// Sets the speed from <paramref name="value"/> of <paramref name="scale"/>, moved on its slider: the nearest number the
        /// game offers (Chunithm's whole numbers past 15). A number past the fastest or slowest speed stored (a high wall
        /// that keeps the visible time makes notes faster) moves the slider back to the one reached.
        /// </summary>
        private void setSpeed(SpeedScale scale, double value)
        {
            if (syncingSpeed)
                return;

            syncingSpeed = true;
            bool reached;

            try
            {
                var context = speedContext();
                double units = SpeedScales.Snap(scale, value);
                speedUnits[scale].Value = units;
                maniaSpeed.Value = context.FromUnits(scale, units);
                reached = Math.Abs(context.ToUnits(scale, maniaSpeed.Value) - units) < SpeedScales.RangeOf(scale).Precision / 2;
            }
            finally
            {
                syncingSpeed = false;
            }

            showSpeedInScales(reached ? scale : null);
        }

        /// <summary>
        /// The skin's line for the selected beatmap's key count, the selected mods' rate, the wall and the camera.
        /// </summary>
        private SpeedContext speedContext()
        {
            var settings = highway.Current;
            var selectedMods = mods?.Value ?? Array.Empty<Mod>();
            float hitPosition = skin == null ? Stage.HIT_TARGET_POSITION : VisibleTime.HitPosition(skin, keyCount(selectedMods));

            return new SpeedContext(hitPosition, ModUtils.CalculateRateWithMods(selectedMods), settings.ToScroll(), settings.CameraMode == HighwayCameraMode.Flat);
        }

        /// <summary>
        /// "9.25 (593 ms visible)": a game's number, with how long notes are visible at it.
        /// </summary>
        private LocalisableString gameSpeedLabel(SpeedScale scale, double units)
        {
            var context = speedContext();
            double visible = context.Milliseconds(context.FromUnits(scale, units), true);
            string number = units.ToString("F" + SpeedScales.RangeOf(scale).Decimals, CultureInfo.InvariantCulture);

            return $"{number} ({Math.Round(visible).ToString(CultureInfo.InvariantCulture)} ms visible)";
        }

        /// <summary>
        /// Mania's label ("405ms (speed 29.0)"), with how long notes are visible on the highway in place of
        /// <c>11485 / speed</c>: the skin's hit position for the selected beatmap's key count (and key mods) changes it as in
        /// mania, and the wall when it keeps the speed. A beatmap's own skin can change it in gameplay too; the toast there shows
        /// the exact value.
        /// </summary>
        private LocalisableString scrollSpeedLabel(double speed)
        {
            double visible = speedContext().Milliseconds(speed, true);
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
