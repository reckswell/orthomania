using System;
using System.Text.Json.Serialization;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Settings
{
    /// <summary>
    /// What the patcher remembers between launches (<c>state.json</c>).
    /// </summary>
    public sealed class PatcherState
    {
        /// <summary>
        /// The lazer release and orthomania build last started without a crash at start-up: what the patcher falls back to.
        /// </summary>
        public PairRecord? LastWorking { get; set; }

        /// <summary>
        /// The pair last started, whatever happened.
        /// </summary>
        public PairRecord? LastStarted { get; set; }

        /// <summary>
        /// The newest lazer release that has opened the data folder. A newer lazer can upgrade the database past what an older one
        /// opens, so the patcher never starts an older one on it.
        /// </summary>
        public string? DataOpenedBy { get; set; }

        public DateTime? LastCheckUtc { get; set; }

        [JsonIgnore]
        public LazerVersion? DataOpenedByVersion => LazerVersion.TryParse(DataOpenedBy, out var version) ? version : null;

        /// <summary>
        /// Whether <paramref name="lazer"/> may open the data folder: no newer release has opened it.
        /// </summary>
        public bool MayOpenData(LazerVersion lazer)
        {
            var opened = DataOpenedByVersion;
            return opened == null || lazer >= opened;
        }

        /// <summary>
        /// Whether starting <paramref name="lazer"/> will be the data folder's first time on it.
        /// </summary>
        public bool IsFirstStartOn(LazerVersion lazer)
        {
            var opened = DataOpenedByVersion;
            return opened == null || lazer > opened;
        }

        public void RecordStarted(LazerVersion lazer, string build)
        {
            LastStarted = new PairRecord(lazer.Number, build);

            if (IsFirstStartOn(lazer))
                DataOpenedBy = lazer.Number;
        }

        public void RecordWorking(LazerVersion lazer, string build) => LastWorking = new PairRecord(lazer.Number, build);
    }

    public sealed record PairRecord(string Lazer, string Build)
    {
        [JsonIgnore]
        public LazerVersion? Version => LazerVersion.TryParse(Lazer, out var version) ? version : null;
    }
}
