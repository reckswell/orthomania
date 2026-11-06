// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ManiaOrtho.ScoreServerContract;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.ManiaOrtho.ScoreServer
{
    /// <summary>
    /// Turns a finished play into what the score server receives.
    /// </summary>
    public static class ScoreServerPlays
    {
        /// <summary>
        /// The upload for <paramref name="score"/>, without the game's own stars and pp (worked out later, off the update thread).
        /// Reads the score's mods, so call it where they belong (the update thread).
        /// </summary>
        public static PlayUpload Create(ScoreInfo score, BeatmapInfo beatmap, int keys, string scoreId, string? clientVersion)
        {
            return new PlayUpload
            {
                Client = ManiaOrthoRuleset.SHORT_NAME,
                ClientVersion = clientVersion,
                ScoreId = scoreId,
                PlayedAt = score.Date,
                Passed = score.Passed,
                Rank = score.Rank.ToString(),
                Beatmap = new BeatmapUpload
                {
                    Md5 = beatmap.MD5Hash,
                    OnlineId = beatmap.OnlineID,
                    SetOnlineId = beatmap.BeatmapSet?.OnlineID ?? -1,
                    Status = beatmap.Status.ToString(),
                    Artist = beatmap.Metadata.Artist,
                    Title = beatmap.Metadata.Title,
                    Version = beatmap.DifficultyName,
                    Creator = beatmap.Metadata.Author.Username,
                    Keys = keys,
                },
                Mods = score.APIMods.ToList(),
                Statistics = score.Statistics.Where(s => s.Value != 0).ToDictionary(s => s.Key.ToString(), s => s.Value),
                MaximumStatistics = score.MaximumStatistics.Where(s => s.Value != 0).ToDictionary(s => s.Key.ToString(), s => s.Value),
                MaxCombo = score.MaxCombo,
                Accuracy = score.Accuracy,
                TotalScore = score.TotalScore,
                Settings = new Dictionary<string, string?>(),
            };
        }

        /// <summary>
        /// A beatmap's <c>.osu</c> file as osu! stores it (<c>files/</c>, by its SHA-256, <see cref="BeatmapInfo.Hash"/>), or null
        /// if it is not there or is not the file the play was made on (<see cref="BeatmapInfo.MD5Hash"/>).
        /// </summary>
        public static byte[]? ReadBeatmapFile(Storage gameStorage, string hash, string md5)
        {
            if (hash.Length < 2)
                return null;

            string path = Path.Combine("files", hash.Remove(1), hash.Remove(2), hash);

            if (!gameStorage.Exists(path))
                return null;

            byte[] data = File.ReadAllBytes(gameStorage.GetFullPath(path));

            return Md5(data) == md5 ? data : null;
        }

        public static string Md5(byte[] data) => System.Convert.ToHexStringLower(MD5.HashData(data));
    }
}
