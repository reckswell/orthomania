namespace Orthomania.Patcher.Flow
{
    /// <summary>
    /// Where the patcher is, in the order a launch goes through it.
    /// </summary>
    public enum PatcherStage
    {
        Starting,

        /// <summary>
        /// A lazer is running (plain or orthomania's); the patcher waits for it to close and carries on by itself.
        /// </summary>
        WaitingForGame,

        UpdatingLazer,
        GettingOrthomania,
        Preparing,

        /// <summary>
        /// Everything is checked: Launch starts osu! with orthomania.
        /// </summary>
        Ready,

        /// <summary>
        /// No orthomania build for the installed lazer yet, but an earlier pair that worked is kept and can be played.
        /// </summary>
        Fallback,

        NotInstalled,

        /// <summary>
        /// Something stops orthomania from starting; the detail says what.
        /// </summary>
        Problem,

        /// <summary>
        /// osu! is running with orthomania.
        /// </summary>
        Running,
    }

    /// <summary>
    /// What the patcher window shows: a big line, a small line, and the progress bar when something is downloading.
    /// </summary>
    /// <param name="Progress">From 0 to 1; null when there is nothing to measure.</param>
    public sealed record PatcherStatus(PatcherStage Stage, string Title, string Detail, double? Progress = null)
    {
        public static PatcherStatus Starting => new PatcherStatus(PatcherStage.Starting, "Hi!", "checking for osu! updates...");
    }
}
