// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Configuration;

namespace osu.Game.Rulesets.ManiaOrtho.Configuration
{
    /// <summary>
    /// One of orthomania's settings managers kept in a file of orthomania's own data folder (<see cref="OrthomaniaData"/>),
    /// <c>settings/&lt;name&gt;.json</c>, instead of in osu!'s database. Each manager has its own file, so their saves never meet.
    /// </summary>
    /// <remarks>
    /// The managers are lazer's <c>RulesetConfigManager</c>s, given no database (<see cref="StoreFor"/>): they keep working as
    /// before, and this loads their values once they have their defaults, and writes them all whenever they save.
    /// </remarks>
    public sealed class OwnSettings
    {
        public const string DIRECTORY = "settings";

        // Readable by hand: only JSON's own escapes, the file is never shown as a web page.
        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly string path;
        private readonly object writing = new object();

        private OwnSettings(string path)
        {
            this.path = path;
        }

        /// <summary>
        /// The database a manager is given: none when orthomania has its own data folder.
        /// </summary>
        public static SettingsStore? StoreFor(SettingsStore? settings) => OrthomaniaData.Current != null ? null : settings;

        /// <summary>
        /// Loads the stored values into <paramref name="bindables"/>, the manager's settings with their defaults.
        /// </summary>
        /// <returns>Where they are saved; null without an own data folder, when lazer's database keeps them.</returns>
        public static OwnSettings? Load<TLookup>(string name, int? variant, IDictionary<TLookup, IBindable> bindables)
            where TLookup : struct, Enum
        {
            if (OrthomaniaData.Current is not { } data)
                return null;

            string file = variant is int v && v != 0 ? $"{name}-{v.ToString(CultureInfo.InvariantCulture)}.json" : $"{name}.json";
            var settings = new OwnSettings(Path.Combine(data.Folder, DIRECTORY, file));

            foreach (var (key, value) in settings.read())
            {
                if (!Enum.TryParse(key, out TLookup lookup) || !bindables.TryGetValue(lookup, out var bindable) || bindable is not IParseable parseable)
                    continue;

                try
                {
                    parseable.Parse(value, CultureInfo.InvariantCulture);
                }
                catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or OverflowException)
                {
                    Logger.Log($"orthomania: setting {key} in {settings.path} could not be read (\"{value}\"); using its default.");
                }
            }

            return settings;
        }

        /// <summary>
        /// Writes every value, whole: to a file beside it first, then over it, so a stopped game never leaves half a file.
        /// </summary>
        public void Save<TLookup>(IDictionary<TLookup, IBindable> bindables)
            where TLookup : struct, Enum
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);

            // Every setting is added while the manager is made; saves come after.
            foreach (var (lookup, bindable) in bindables.ToList())
                values[lookup.ToString()] = bindable is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : bindable.ToString() ?? string.Empty;

            lock (writing)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                    string temporary = path + ".new";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(values, json_options));
                    File.Move(temporary, path, overwrite: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Logger.Log($"orthomania: settings could not be saved to {path} ({e.Message}); trying again at the next change.", LoggingTarget.Runtime, LogLevel.Important);
                }
            }
        }

        private Dictionary<string, string> read()
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                Logger.Log($"orthomania: settings in {path} could not be read ({e.Message}); using the defaults.", LoggingTarget.Runtime, LogLevel.Important);
            }

            return new Dictionary<string, string>();
        }
    }
}
