// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Online.API;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.ManiaOrtho.ScoreServer
{
    /// <summary>
    /// Whose account plays go to: the one the game is linked to by signing in with osu! (<see cref="ScoreServerLinking"/>).
    /// Plays then carry the score server's own token for this game, to that server only. osu!'s sign-in is never sent anywhere.
    /// </summary>
    public static class ScoreServerSignIn
    {
        /// <summary>
        /// What the score server address setting says about whose account plays go to: the account the game is linked to, or
        /// that it is not linked for this address.
        /// </summary>
        /// <param name="config">The score server settings, holding the link.</param>
        /// <param name="api">osu!'s sign-in in the game, only to warn when it is someone else than the link's account.</param>
        /// <param name="scoreServerAddress">The score server address setting.</param>
        public static SettingsNote.Data? Describe(ScoreServerConfigManager? config, IAPIProvider? api, string? scoreServerAddress)
        {
            var server = ScoreServerClient.ParseAddress(scoreServerAddress, out _);

            if (server == null)
                return null;

            if (ScoreServerLinking.Current(config) is not { } link)
            {
                return new SettingsNote.Data("Not linked: plays go to the score server's first account. Press \"Link with osu!\" to send them to yours.",
                    SettingsNote.Type.Informational);
            }

            if (!SameServer(link.Server, server))
            {
                return new SettingsNote.Data($"The game is linked to {link.Server.Authority}, not to this address, so plays sent here go to the score "
                                             + "server's first account. Link with osu! again for this address.", SettingsNote.Type.Warning);
            }

            // Signed in to osu! in the game as someone else than the account the link signed in with.
            if (api?.State.Value == APIState.Online && link.OsuUserId > 0 && api.LocalUser.Value.Id != link.OsuUserId)
            {
                return new SettingsNote.Data($"Plays go to {link.Username}'s account, but the game is signed in to osu! as {api.LocalUser.Value.Username}. "
                                             + "Unlink and link with osu! again to use that account.", SettingsNote.Type.Warning);
            }

            return new SettingsNote.Data($"Plays go to {link.Username}'s account (linked with osu!).", SettingsNote.Type.Informational);
        }

        /// <summary>
        /// Whether two addresses are the same score server (scheme, host and port), so a link made with one is good for the other.
        /// Never for osu!'s own servers.
        /// </summary>
        public static bool SameServer(Uri a, Uri b)
            => a.Scheme.Equals(b.Scheme, StringComparison.OrdinalIgnoreCase)
               && a.Authority.Equals(b.Authority, StringComparison.OrdinalIgnoreCase)
               && !ScoreServerClient.IsOsuHost(b.Host)
               && !ScoreServerClient.IsOsuHost(b.IdnHost);
    }
}
