// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.ManiaOrtho.Scoring
{
    /// <summary>
    /// <c>maniaortho/plays.csv</c> in the game's data folder: one row per pass or fail, for spreadsheets and for rating
    /// plays again when osu!mania's calculators change (the raw judgement counts and each mod's settings are kept). Each
    /// play's replay is saved next to it in <see cref="REPLAY_DIRECTORY"/>, because osu! keeps no replay for a ruleset
    /// that is not one of its four own.
    /// </summary>
    public static class PlayLog
    {
        public const string DIRECTORY = "maniaortho";

        public const string FILENAME = "plays.csv";

        public const string REPLAY_DIRECTORY = "replays";

        public static readonly string[] COLUMNS =
        {
            "time", "result", "rank", "artist", "title", "difficulty", "beatmap_md5", "keys", "mods",
            "score", "accuracy", "max_combo", "perfect", "great", "good", "ok", "meh", "miss",
            "stars", "pp", "difficulty_version", "preset", "camera", "wall", "wall_scale", "lift", "wall_keeps_visible_time",
            "scroll_speed", "mods_json", "replay", "score_id",
        };

        private static readonly object write_lock = new object();

        private static readonly char[] needs_quotes = { ',', '"', '\r', '\n' };

        private static readonly byte[] utf8_bom = { 0xEF, 0xBB, 0xBF };

        private static readonly UTF8Encoding strict_utf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// Appends <paramref name="values"/> (in <see cref="COLUMNS"/> order) to the log in <paramref name="storage"/>. A new file
        /// starts with a byte-order mark (so spreadsheets read titles as UTF-8) and the header. A file whose header differs (an
        /// older set of columns) is kept under a dated name and a new one is started.
        /// </summary>
        public static void Append(Storage storage, IReadOnlyList<string?> values)
        {
            lock (write_lock)
            {
                string path = storage.GetFullPath(FILENAME, true);
                string header = FormatRow(COLUMNS);

                if (File.Exists(path) && readFirstLine(path) is string existing && existing.Length > 0 && existing != header)
                    File.Move(path, storage.GetFullPath($"plays until {DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture)}.csv"));

                // A plain append: the storage's own write streams may replace the file rather than add to it.
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    bool fresh = stream.Length == 0;

                    using (var writer = new StreamWriter(stream, new UTF8Encoding(fresh)))
                    {
                        if (fresh)
                            writer.Write(header + "\r\n");

                        writer.Write(FormatRow(values) + "\r\n");
                    }
                }
            }
        }

        /// <summary>
        /// One CSV line (RFC 4180): fields with a comma, quote or line break are quoted, quotes doubled; null is empty.
        /// </summary>
        public static string FormatRow(IReadOnlyList<string?> values)
        {
            var row = new StringBuilder();

            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                    row.Append(',');

                string value = values[i] ?? string.Empty;

                if (value.IndexOfAny(needs_quotes) < 0)
                    row.Append(value);
                else
                    row.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
            }

            return row.ToString();
        }

        /// <summary>
        /// The log's rows after the header, each in <see cref="COLUMNS"/> order; null without a log, or with a log of other
        /// columns (the next play starts a new one).
        /// </summary>
        /// <exception cref="InvalidDataException">The log is not UTF-8 (a spreadsheet saved it in another encoding).</exception>
        public static List<string[]>? ReadPlays(Storage storage)
        {
            lock (write_lock)
                return readPlays(storage.GetFullPath(FILENAME, true));
        }

        /// <summary>
        /// Lets <paramref name="change"/> edit the log's rows (as <see cref="ReadPlays"/> reads them), and writes them back
        /// when it returns true, the file as it was kept first under <paramref name="backupName"/> (numbered if that is
        /// taken). Holds the log's lock throughout, so no play is added meanwhile: keep <paramref name="change"/> quick.
        /// </summary>
        /// <returns>Whether the log was rewritten.</returns>
        /// <exception cref="InvalidDataException">The log is not UTF-8 (a spreadsheet saved it in another encoding).</exception>
        public static bool Rewrite(Storage storage, Func<List<string[]>, bool> change, string backupName)
        {
            lock (write_lock)
            {
                string path = storage.GetFullPath(FILENAME, true);
                List<string[]>? plays = readPlays(path);

                if (plays == null || !change(plays))
                    return false;

                string backup = storage.GetFullPath(backupName);

                for (int i = 2; File.Exists(backup); i++)
                    backup = storage.GetFullPath($"{Path.GetFileNameWithoutExtension(backupName)} ({i}){Path.GetExtension(backupName)}");

                // Kept first, then written beside it and moved over it: a failure leaves the old file whole, and nothing else.
                string temporary = path + ".new";
                File.Copy(path, backup, false);

                try
                {
                    using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true)))
                    {
                        writer.Write(FormatRow(COLUMNS) + "\r\n");

                        foreach (string[] play in plays)
                            writer.Write(FormatRow(play) + "\r\n");
                    }

                    File.Move(temporary, path, true);
                }
                catch
                {
                    // Each on its own: a file another program holds must not hide why the write failed.
                    tryDelete(temporary);
                    tryDelete(backup);
                    throw;
                }

                return true;
            }
        }

        /// <summary>
        /// The rows of a CSV text (RFC 4180, as <see cref="FormatRow"/> writes them): quoted fields may hold commas, doubled
        /// quotes and line breaks.
        /// </summary>
        public static List<string[]> ParseRows(string text)
        {
            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            bool inRow = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (quoted)
                {
                    if (c != '"')
                        field.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"')
                        field.Append(text[++i]);
                    else
                        quoted = false;

                    continue;
                }

                switch (c)
                {
                    case '"':
                        quoted = true;
                        inRow = true;
                        break;

                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        inRow = true;
                        break;

                    // Unquoted, a carriage return only ends a line.
                    case '\r':
                        break;

                    case '\n':
                        fields.Add(field.ToString());
                        field.Clear();
                        rows.Add(fields.ToArray());
                        fields.Clear();
                        inRow = false;
                        break;

                    default:
                        field.Append(c);
                        inRow = true;
                        break;
                }
            }

            if (inRow)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }

            return rows;
        }

        private static List<string[]>? readPlays(string path)
        {
            if (!File.Exists(path))
                return null;

            List<string[]> rows = ParseRows(readText(path));

            if (rows.Count == 0 || FormatRow(rows[0]) != FormatRow(COLUMNS))
                return null;

            rows.RemoveAt(0);
            return rows;
        }

        /// <summary>
        /// The file as UTF-8 (after its byte-order mark), refusing anything else: text read in another encoding and written
        /// back would lose its accents.
        /// </summary>
        private static string readText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int start = bytes.AsSpan().StartsWith(utf8_bom) ? utf8_bom.Length : 0;

            try
            {
                return strict_utf8.GetString(bytes, start, bytes.Length - start);
            }
            catch (DecoderFallbackException e)
            {
                throw new InvalidDataException($"{path} is not UTF-8 (was it saved by a spreadsheet in another encoding?), so it is left as it is", e);
            }
        }

        private static void tryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Left behind; the next rewrite writes over the temporary file and numbers its backup.
            }
        }

        private static string? readFirstLine(string path)
        {
            // The reader skips the byte-order mark.
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
                return reader.ReadLine();
        }
    }
}
