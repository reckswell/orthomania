// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Playtest
{
    /// <summary>
    /// Beatmaps used by the playtest harness and parity tests.
    /// </summary>
    /// <remarks>
    /// A source is one of:
    /// <list type="bullet">
    /// <item><c>real-4k</c>: a real ranked 4K map from the mania test resources (1419 objects, no audio).</item>
    /// <item><c>synthetic-NK</c> (e.g. <c>synthetic-7k</c>): a generated ~40s chart covering stairs, chords, jacks, long notes, SV changes and a kiai section.</item>
    /// <item><c>synthetic-NK-n1</c> (e.g. <c>synthetic-8k-n1</c>): the same chart marked for stable's N+1 layout (<c>SpecialStyle</c>).</item>
    /// <item>A path to any <c>.osu</c> file, e.g. one from a local osu! Songs folder.</item>
    /// </list>
    /// </remarks>
    public static class PlaytestBeatmaps
    {
        public const string REAL_4K = "real-4k";

        public static readonly string[] DEFAULT_SOURCES = { REAL_4K, "synthetic-4k", "synthetic-7k" };

        /// <summary>
        /// The file behind <see cref="REAL_4K"/>: a 4K chart from osu!mania's own test resources
        /// (osu.Game.Rulesets.Mania.Tests/Resources/Testing/Beatmaps in ppy/osu), copied beside the tests.
        /// </summary>
        public static string Real4KPath => Path.Combine(AppContext.BaseDirectory, "Resources", "4869637.osu");

        /// <summary>
        /// <see cref="Real4KPath"/> packed as an .osz, so a game can import it as a player would.
        /// </summary>
        public static MemoryStream CreateReal4KArchive()
        {
            var stream = new MemoryStream();

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                archive.CreateEntryFromFile(Real4KPath, Path.GetFileName(Real4KPath));

            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// Marks a synthetic chart for stable's N+1 layout: <c>synthetic-8k-n1</c>.
        /// </summary>
        public const string N_PLUS_ONE_SUFFIX = "-n1";

        public static IBeatmap Load(string source)
        {
            if (source == REAL_4K)
                return Decode(Real4KPath);

            if (source.StartsWith("synthetic-", StringComparison.Ordinal) && source.EndsWith('k')
                                                                         && int.TryParse(source.AsSpan("synthetic-".Length, source.Length - "synthetic-".Length - 1), out int columns))
                return CreateSynthetic(columns);

            // The same chart marked for stable's N+1 layout (SpecialStyle), as 8K maps played as 7 keys and a special key are.
            if (source.StartsWith("synthetic-", StringComparison.Ordinal) && source.EndsWith(N_PLUS_ONE_SUFFIX, StringComparison.Ordinal))
            {
                var beatmap = (ManiaBeatmap)Load(source[..^N_PLUS_ONE_SUFFIX.Length]);
                beatmap.SpecialStyle = true;
                return beatmap;
            }

            if (File.Exists(source))
                return Decode(source);

            throw new ArgumentException($"Unknown beatmap source \"{source}\". Use {REAL_4K}, synthetic-<N>k, synthetic-<N>k{N_PLUS_ONE_SUFFIX}, or a path to a .osu file.");
        }

        public static Beatmap Decode(string path)
        {
            try
            {
                return decode(path);
            }
            catch (ArgumentException)
            {
                // Outside a running game (plain NUnit tests), the decoder has no ruleset store to resolve "Mode: 3" with.
                Decoder.RegisterDependencies(new AssemblyRulesetStore());
                return decode(path);
            }

            static Beatmap decode(string path)
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new LineBufferedReader(stream))
                    return Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
            }
        }

        /// <summary>
        /// Creates a deterministic chart that exercises the cases a projected playfield is most likely to get wrong:
        /// dense stairs, chords spanning outer lanes, jacks, long notes overlapping taps, and slow/fast scroll velocity sections.
        /// </summary>
        public static ManiaBeatmap CreateSynthetic(int columns, double bpm = 170)
        {
            if (columns < 1 || columns > ManiaRuleset.MAX_STAGE_KEYS)
                throw new ArgumentOutOfRangeException(nameof(columns));

            var beatmap = new ManiaBeatmap(new StageDefinition(columns))
            {
                BeatmapInfo = new BeatmapInfo
                {
                    Ruleset = new ManiaRuleset().RulesetInfo,
                    Difficulty = new BeatmapDifficulty
                    {
                        CircleSize = columns,
                        OverallDifficulty = 8,
                        DrainRate = 7,
                    },
                    Metadata = new BeatmapMetadata
                    {
                        Artist = "playtest harness",
                        Title = $"synthetic {columns}K",
                    },
                    DifficultyName = $"{columns}K patterns",
                },
                ControlPointInfo = new ControlPointInfo(),
            };

            double beat = 60000 / bpm;
            double bar = beat * 4;
            const double lead_in = 1000;

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beat });
            // A kiai section (bars 8-12), for the optional kiai sway.
            beatmap.ControlPointInfo.Add(lead_in + bar * 8, new EffectControlPoint { KiaiMode = true });
            beatmap.ControlPointInfo.Add(lead_in + bar * 12, new EffectControlPoint());
            beatmap.ControlPointInfo.Add(lead_in + bar * 16, new EffectControlPoint { ScrollSpeed = 0.5 });
            beatmap.ControlPointInfo.Add(lead_in + bar * 20, new EffectControlPoint { ScrollSpeed = 1.75 });
            beatmap.ControlPointInfo.Add(lead_in + bar * 24, new EffectControlPoint { ScrollSpeed = 1 });

            double[] lastEnd = new double[columns];
            Array.Fill(lastEnd, double.MinValue);

            void note(double time, int column)
            {
                column = ((column % columns) + columns) % columns;
                if (time <= lastEnd[column] + 30)
                    return;

                beatmap.HitObjects.Add(new Note { StartTime = time, Column = column });
                lastEnd[column] = time;
            }

            void hold(double time, double duration, int column)
            {
                column = ((column % columns) + columns) % columns;
                if (time <= lastEnd[column] + 30)
                    return;

                beatmap.HitObjects.Add(new HoldNote { StartTime = time, Duration = duration, Column = column });
                lastEnd[column] = time + duration;
            }

            double barStart(int index) => lead_in + bar * index;

            // Bars 0-3: 1/4 stairs, up then down.
            for (int b = 0; b < 4; b++)
            {
                for (int i = 0; i < 16; i++)
                {
                    int step = b % 2 == 0 ? i : columns - 1 - i;
                    note(barStart(b) + i * beat / 4, step);
                }
            }

            // Bars 4-7: 1/2 chords that pair inner and outer lanes (the outer lanes slant most under perspective).
            for (int b = 4; b < 8; b++)
            {
                for (int i = 0; i < 8; i++)
                {
                    double t = barStart(b) + i * beat / 2;
                    note(t, i % columns);
                    note(t, columns - 1 - (i % columns));
                }
            }

            // Bars 8-11: 1/4 jacks, moving lane every beat.
            for (int b = 8; b < 12; b++)
            {
                for (int i = 0; i < 16; i++)
                    note(barStart(b) + i * beat / 4, b * 4 + i / 4);
            }

            // Bars 12-15: long notes with taps in the other lanes.
            for (int b = 12; b < 16; b++)
            {
                for (int i = 0; i < 4; i++)
                {
                    int lnColumn = (b + i) % columns;
                    hold(barStart(b) + i * beat, beat * 1.5, lnColumn);

                    for (int j = 1; j < 4; j++)
                        note(barStart(b) + i * beat + j * beat / 4, lnColumn + j);
                }
            }

            // Bars 16-19: stairs at 0.5x scroll velocity.
            for (int b = 16; b < 20; b++)
            {
                for (int i = 0; i < 8; i++)
                    note(barStart(b) + i * beat / 2, i);
            }

            // Bars 20-23: chords at 1.75x scroll velocity.
            for (int b = 20; b < 24; b++)
            {
                for (int i = 0; i < 4; i++)
                {
                    double t = barStart(b) + i * beat;
                    for (int c = i % 2; c < columns; c += 2)
                        note(t, c);
                }
            }

            // Bars 24-27: staggered long notes back at 1x, including short ones.
            for (int b = 24; b < 28; b++)
            {
                for (int i = 0; i < columns; i++)
                    hold(barStart(b) + i * beat / columns, beat / 2 + (i % 3) * beat / 4, i);
            }

            beatmap.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            return beatmap;
        }
    }
}
