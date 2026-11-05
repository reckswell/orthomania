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
        /// The lazer release and orthomania build last started without a crash at start-up.
        /// </summary>
        public PairRecord? LastWorking { get; set; }

        /// <summary>
        /// The pair last started, whatever happened.
        /// </summary>
        public PairRecord? LastStarted { get; set; }

        public DateTime? LastCheckUtc { get; set; }

        public void RecordStarted(LazerVersion lazer, string build) => LastStarted = new PairRecord(lazer.Number, build);

        public void RecordWorking(LazerVersion lazer, string build) => LastWorking = new PairRecord(lazer.Number, build);
    }

    public sealed record PairRecord(string Lazer, string Build)
    {
        [JsonIgnore]
        public LazerVersion? Version => LazerVersion.TryParse(Lazer, out var version) ? version : null;
    }
}
