// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Configuration;

namespace osu.Game.Rulesets.ManiaOrtho.ScoreServer
{
    /// <summary>
    /// Whether, and where, plays go to the player's own score server (orthomania's <c>server/</c>). Stored under this ruleset next to
    /// mania's and the highway's settings, so its keys share no name with theirs. Off unless the player turns it on.
    /// </summary>
    public class ScoreServerConfigManager : RulesetConfigManager<ScoreServerSetting>
    {
        /// <summary>
        /// Where orthomania's <c>server/scripts/server</c> runs the server: this computer only.
        /// </summary>
        public const string DEFAULT_ADDRESS = "http://127.0.0.1:7727";

        private readonly OwnSettings? own;

        /// <remarks>
        /// With orthomania's own data folder, kept there (<c>settings/score-server.json</c>) with the link's token, never in osu!'s data.
        /// </remarks>
        public ScoreServerConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
            : base(OwnSettings.StoreFor(settings), ruleset, variant)
        {
            own = OwnSettings.Load("score-server", variant, ConfigStore);
        }

        protected override bool PerformSave()
        {
            base.PerformSave();
            own?.Save(ConfigStore);
            return true;
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();

            SetDefault(ScoreServerSetting.ScoreServerEnabled, false);
            SetDefault(ScoreServerSetting.ScoreServerAddress, DEFAULT_ADDRESS);

            SetDefault(ScoreServerSetting.LinkServer, string.Empty);
            SetDefault(ScoreServerSetting.LinkToken, string.Empty);
            SetDefault(ScoreServerSetting.LinkUsername, string.Empty);
            SetDefault(ScoreServerSetting.LinkOsuUserId, 0, 0, int.MaxValue);
        }
    }

    public enum ScoreServerSetting
    {
        ScoreServerEnabled,
        ScoreServerAddress,

        /// <summary>
        /// The server the game is linked to by signing in with osu! (<see cref="ScoreServerLinking"/>); its token goes only there.
        /// </summary>
        LinkServer,

        /// <summary>
        /// The server's own token for this game (never osu!'s), sent with each play to <see cref="LinkServer"/>.
        /// </summary>
        LinkToken,

        LinkUsername,
        LinkOsuUserId,
    }
}
