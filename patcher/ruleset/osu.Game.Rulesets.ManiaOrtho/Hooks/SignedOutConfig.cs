// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Configuration;

namespace osu.Game.Rulesets.ManiaOrtho.Hooks
{
    /// <summary>
    /// osu!'s settings as lazer's API sees them while orthomania keeps osu! signed out: lazer's defaults, so no saved username or
    /// sign-in, held only in memory. Nothing is read from osu!'s settings file and nothing is written to it, so a sign-in in this
    /// session is forgotten when the game closes, and the sign-in plain osu! saved stays as it was.
    /// </summary>
    public sealed class SignedOutConfig : OsuConfigManager
    {
        public SignedOutConfig()
            : base(null!)
        {
        }

        protected override void PerformLoad()
        {
        }

        protected override bool PerformSave() => true;
    }
}
