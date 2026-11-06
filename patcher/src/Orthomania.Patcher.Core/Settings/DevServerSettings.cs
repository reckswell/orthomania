using System.Collections.Generic;

namespace Orthomania.Patcher.Settings
{
    /// <summary>
    /// For development only: start orthomania's score server with the game when it is not already running, and stop it after
    /// (<c>devServer</c> in <c>settings.json</c>). Off unless turned on; this goes away once the server runs on its own.
    /// </summary>
    public sealed class DevServerSettings
    {
        public bool Enabled { get; set; }

        /// <summary>
        /// The server: <c>OrthoServer.exe</c> from a published build, or <c>OrthoServer.dll</c>, which runs with <c>dotnet</c>.
        /// Null: <c>server\OrthoServer.exe</c> (or <c>.dll</c>) beside the patcher.
        /// </summary>
        public string? Executable { get; set; }

        /// <summary>
        /// What the server is started with. Null: <see cref="DEFAULT_ARGUMENTS"/>. <c>{port}</c>, <c>{data}</c> and <c>{pid}</c>
        /// become the port of <see cref="HealthUrl"/>, the full path of <see cref="DataFolder"/> and the patcher's process id.
        /// </summary>
        public List<string>? Arguments { get; set; }

        /// <summary>
        /// Where the server keeps its accounts, plays and sign-in settings. Null: <c>server</c> in the patcher's folder, which
        /// removing orthomania leaves alone.
        /// </summary>
        public string? DataFolder { get; set; }

        /// <summary>
        /// Where it runs from. Null: the executable's folder.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Answers once the server is ready, without touching its database. Only a server on this computer is ever started or
        /// waited for.
        /// </summary>
        public string HealthUrl { get; set; } = DEFAULT_HEALTH_URL;

        public int StartTimeoutSeconds { get; set; } = 30;

        public const string DEFAULT_HEALTH_URL = "http://127.0.0.1:7727/api/health";

        /// <summary>
        /// The server shuts itself down when the patcher's process is gone, so a patcher that crashes leaves no server behind.
        /// </summary>
        public static readonly IReadOnlyList<string> DEFAULT_ARGUMENTS = new[] { "serve", "--port", "{port}", "--data", "{data}", "--parent-pid", "{pid}" };
    }
}
