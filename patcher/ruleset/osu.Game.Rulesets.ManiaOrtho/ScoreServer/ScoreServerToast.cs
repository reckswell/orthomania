// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using System.Text;
using osu.Framework.Graphics.Sprites;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Rulesets.ManiaOrtho.ScoreServer
{
    /// <summary>
    /// The notification after a play went to (or could not reach) the score server.
    /// </summary>
    public static class ScoreServerToast
    {
        /// <summary>
        /// Differences smaller than this are rounding: the game shows pp to two decimals.
        /// </summary>
        private const double pp_tolerance = 0.01;

        public static Notification Create(ScoreServerOutcome outcome, string playName) => new SimpleNotification
        {
            Text = Describe(outcome, playName),
            Icon = outcome.Kind switch
            {
                ScoreServerOutcomeKind.Sent => FontAwesome.Solid.Trophy,
                ScoreServerOutcomeKind.Queued => FontAwesome.Solid.Inbox,
                _ => FontAwesome.Solid.ExclamationTriangle,
            },
        };

        public static string Describe(ScoreServerOutcome outcome, string playName)
        {
            var text = new StringBuilder();
            // Messages are sentences of their own (the server's end with a full stop); this puts them inside one.
            string? message = outcome.Message?.TrimEnd('.');

            switch (outcome.Kind)
            {
                case ScoreServerOutcomeKind.Queued:
                    text.Append(CultureInfo.InvariantCulture, $"Score server: {message}. {playName} is kept and sent with a later play once the server takes it");
                    text.Append(outcome.Queued > 1 ? FormattableString.Invariant($" ({outcome.Queued} plays waiting).") : ".");
                    break;

                case ScoreServerOutcomeKind.Refused:
                    text.Append(CultureInfo.InvariantCulture, $"Score server: {playName} was not sent: {message}.");

                    if (outcome.SetAside)
                        text.Append(CultureInfo.InvariantCulture, $" It is kept in \"{ScoreServerClient.REFUSED_DIRECTORY}\".");
                    break;

                default:
                    describeResult(text, outcome, playName);
                    break;
            }

            if (outcome.SentFromOutbox > 0)
                text.Append(CultureInfo.InvariantCulture, $" {outcome.SentFromOutbox} earlier {(outcome.SentFromOutbox == 1 ? "play" : "plays")} sent too.");

            return text.ToString();
        }

        private static void describeResult(StringBuilder text, ScoreServerOutcome outcome, string playName)
        {
            var result = outcome.Result!;

            text.Append(CultureInfo.InvariantCulture, $"Unofficial pp, {playName}: ");

            if (result.AlreadyStored)
                text.Append("already on your score server. ");
            else if (result.AwardsPp)
            {
                text.Append(CultureInfo.InvariantCulture, $"{result.Pp:N2}pp");
                text.Append(result.TopPosition is int top ? FormattableString.Invariant($", #{top} of your best plays. ") : ", below your best on this map. ");
            }
            else
                text.Append(CultureInfo.InvariantCulture, $"{result.Pp:N2}pp if it counted, but {lowerFirst(result.Reason)}. ");

            double change = result.TotalPp - result.TotalPpBefore;

            text.Append(Math.Abs(change) >= 0.005
                ? FormattableString.Invariant($"Total {result.TotalPp:N2}pp ({change:+0.00;-0.00}).")
                : FormattableString.Invariant($"Total {result.TotalPp:N2}pp."));

            if (result.Pp is double serverPp && result.ClientPp is double clientPp && Math.Abs(serverPp - clientPp) >= pp_tolerance)
            {
                text.Append(CultureInfo.InvariantCulture,
                    $" The game showed {clientPp:N2}pp: its copy of the calculator differs from the server's ({result.CalculatorVersion}).");
            }

            if (!string.IsNullOrEmpty(result.LiveCalculatorVersion))
                text.Append(CultureInfo.InvariantCulture, $" osu! now rates with {result.LiveCalculatorVersion}: update the score server (server/scripts/server-update).");
        }

        private static string lowerFirst(string? reason)
            => string.IsNullOrEmpty(reason) ? "it does not award pp" : char.ToLowerInvariant(reason[0]) + reason[1..];
    }
}
