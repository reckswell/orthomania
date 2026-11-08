// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using osu.Game.Rulesets.ManiaOrtho.Highway;

namespace osu.Game.Rulesets.ManiaOrtho.Configuration
{
    /// <summary>
    /// What each game preset remembers of its settings (Alpha 2026-11-08: "chunithm should have a saved wall preset depending
    /// on what the previous option was"), stored as one string setting: <c>Chunithm:LaneTilt=60,Wall=0.3;Voltex:ScrollSpeed=9.5</c>.
    /// Keys are setting names, values as <see cref="HighwayConfigManager"/> writes them; neither contains a separator.
    /// </summary>
    public class GameMemory
    {
        private readonly Dictionary<HighwayPreset, SortedDictionary<string, string>> games = new Dictionary<HighwayPreset, SortedDictionary<string, string>>();

        /// <summary>
        /// Games this version does not know (stored by a newer one), kept as they were so writing does not drop them.
        /// </summary>
        private readonly List<string> unknownGames = new List<string>();

        /// <summary>
        /// Reads a stored string. Malformed parts are skipped and games this version does not know are kept as text, so a
        /// string from a newer version still loads and survives being written again.
        /// </summary>
        public static GameMemory Parse(string? text)
        {
            var memory = new GameMemory();

            if (string.IsNullOrEmpty(text))
                return memory;

            foreach (string game in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = game.IndexOf(':');

                if (colon <= 0)
                    continue;

                if (!Enum.TryParse(game.AsSpan(0, colon), out HighwayPreset preset) || !Enum.IsDefined(preset))
                {
                    memory.unknownGames.Add(game);
                    continue;
                }

                foreach (string pair in game.Substring(colon + 1).Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    int equals = pair.IndexOf('=');

                    if (equals > 0)
                        memory.Set(preset, pair.Substring(0, equals), pair.Substring(equals + 1));
                }
            }

            return memory;
        }

        /// <summary>
        /// The value <paramref name="game"/> remembers for <paramref name="key"/>, or null.
        /// </summary>
        public string? Get(HighwayPreset game, string key)
            => games.TryGetValue(game, out var values) && values.TryGetValue(key, out string? value) ? value : null;

        /// <summary>
        /// Remembers <paramref name="value"/> for <paramref name="key"/> in <paramref name="game"/>, or forgets it (null).
        /// Returns whether anything changed.
        /// </summary>
        public bool Set(HighwayPreset game, string key, string? value)
        {
            if (value == null)
            {
                if (!games.TryGetValue(game, out var values) || !values.Remove(key))
                    return false;

                if (values.Count == 0)
                    games.Remove(game);

                return true;
            }

            if (!games.TryGetValue(game, out var stored))
                games[game] = stored = new SortedDictionary<string, string>(StringComparer.Ordinal);
            else if (stored.TryGetValue(key, out string? old) && old == value)
                return false;

            stored[key] = value;
            return true;
        }

        /// <summary>
        /// Whether <paramref name="game"/> remembers anything.
        /// </summary>
        public bool Remembers(HighwayPreset game) => games.ContainsKey(game);

        /// <summary>
        /// The keys <paramref name="game"/> remembers.
        /// </summary>
        public IEnumerable<string> KeysOf(HighwayPreset game) => games.TryGetValue(game, out var values) ? values.Keys : Enumerable.Empty<string>();

        public override string ToString()
        {
            var text = new StringBuilder();

            foreach (var (game, values) in games.OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
            {
                if (text.Length > 0)
                    text.Append(';');

                text.Append(game).Append(':').AppendJoin(',', values.Select(v => $"{v.Key}={v.Value}"));
            }

            foreach (string game in unknownGames)
            {
                if (text.Length > 0)
                    text.Append(';');

                text.Append(game);
            }

            return text.ToString();
        }
    }
}
