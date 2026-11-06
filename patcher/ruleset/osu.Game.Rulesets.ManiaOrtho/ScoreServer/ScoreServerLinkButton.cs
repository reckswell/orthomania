// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using ManiaOrtho.ScoreServerContract;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Online.Chat;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.ManiaOrtho.ScoreServer
{
    /// <summary>
    /// "Link with osu!": links the game to the player's account on the score server by signing in with osu! in the browser
    /// (<see cref="ScoreServerLinking"/>), or unlinks it. While linking, <see cref="Message"/> says what to do and pressing again
    /// cancels.
    /// </summary>
    public partial class ScoreServerLinkButton : SettingsButtonV2
    {
        /// <summary>
        /// What the score server address setting should say while linking, or after linking failed; null otherwise.
        /// </summary>
        public readonly Bindable<SettingsNote.Data?> Message = new Bindable<SettingsNote.Data?>();

        [Resolved]
        private OsuGame? game { get; set; }

        private readonly ScoreServerConfigManager config;
        private readonly IBindable<string> address;
        private readonly Bindable<string> linkToken;
        private readonly Bindable<string> linkUsername;

        private CancellationTokenSource? linking;

        public ScoreServerLinkButton(ScoreServerConfigManager config, IBindable<string> address)
        {
            this.config = config;
            this.address = address.GetBoundCopy();

            linkToken = config.GetBindable<string>(ScoreServerSetting.LinkToken);
            linkUsername = config.GetBindable<string>(ScoreServerSetting.LinkUsername);

            TooltipText = "Signs in with your osu! account on osu!'s own page, in your browser, so plays go to your account on the score server. "
                          + "The game and the score server never see your osu! password.";
            Keywords = new[] { "login", "sign in", "account", "osu!", "link", "server" };
            Action = toggle;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            linkToken.BindValueChanged(_ => updateText());
            linkUsername.BindValueChanged(_ => updateText());
            address.BindValueChanged(_ => Message.Value = null);
            updateText();
        }

        private void updateText()
        {
            if (linking != null)
                Text = "Stop linking";
            else if (ScoreServerLinking.Current(config) is { } link)
                Text = string.IsNullOrEmpty(link.Username) ? "Unlink from the score server" : $"Unlink {link.Username}";
            else
                Text = "Link with osu!";
        }

        private void toggle()
        {
            if (linking != null)
            {
                linking.Cancel();
                return;
            }

            if (ScoreServerLinking.Current(config) is { } current)
            {
                ScoreServerLinking.Clear(config);
                Message.Value = null;
                Task.Run(() => ScoreServerLinking.UnlinkAsync(current));
                return;
            }

            if (ScoreServerClient.ParseAddress(address.Value, out string? problem) is not Uri server)
            {
                Message.Value = new SettingsNote.Data($"Cannot link: {problem}.", SettingsNote.Type.Critical);
                return;
            }

            var cancellation = linking = new CancellationTokenSource();
            Message.Value = new SettingsNote.Data("Asking the score server...", SettingsNote.Type.Informational);
            updateText();

            Task.Run(async () =>
            {
                try
                {
                    var start = await ScoreServerLinking.StartAsync(server, cancellation.Token).ConfigureAwait(false);

                    Schedule(() =>
                    {
                        if (cancellation.IsCancellationRequested)
                            return;

                        // Checked to be on the score server itself (StartAsync); it sends the browser on to osu!'s sign-in.
                        game?.OpenUrlExternally(start.Url, LinkWarnMode.NeverWarn);
                        Message.Value = new SettingsNote.Data($"Sign in with osu! in your browser and allow it there. The page it ends on shows {start.UserCode}.",
                            SettingsNote.Type.Informational);
                    });

                    var status = await ScoreServerLinking.WaitAsync(server, start, cancellation.Token).ConfigureAwait(false);

                    Schedule(() => finish(server, status));
                }
                catch (OperationCanceledException)
                {
                    Schedule(() => Message.Value = null);
                }
                catch (ScoreServerLinkException e)
                {
                    Schedule(() => Message.Value = new SettingsNote.Data($"Not linked: {e.Message}", SettingsNote.Type.Warning));
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Linking with the score server failed");
                    Schedule(() => Message.Value = new SettingsNote.Data("Not linked: something went wrong (see the log).", SettingsNote.Type.Warning));
                }
                finally
                {
                    Schedule(() =>
                    {
                        if (linking == cancellation)
                            linking = null;

                        cancellation.Dispose();
                        updateText();
                    });
                }
            }, cancellation.Token);
        }

        private void finish(Uri server, LinkStatus status)
        {
            switch (status.Status)
            {
                case LinkStatus.LINKED:
                    ScoreServerLinking.Save(config, server, status);
                    Message.Value = null;
                    break;

                case LinkStatus.DENIED:
                    Message.Value = new SettingsNote.Data("Not linked: it was not allowed on osu!.", SettingsNote.Type.Warning);
                    break;

                default:
                    Message.Value = new SettingsNote.Data("Not linked: the sign-in took too long. Press \"Link with osu!\" to try again.", SettingsNote.Type.Warning);
                    break;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            linking?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
