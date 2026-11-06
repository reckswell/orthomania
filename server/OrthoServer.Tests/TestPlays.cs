using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;
using osu.Game.Online.API;
using OrthoServer.Scoring;

namespace OrthoServer.Tests
{
    public static class TestPlays
    {
        public static string ContractPath(string file) => Path.Combine(AppContext.BaseDirectory, "Contract", file);

        public static byte[] Map(int keys = 4, int notes = 400, double bpm = 180, double overallDifficulty = 8)
            => Encoding.UTF8.GetBytes(SyntheticManiaMap.Create(keys, notes, bpm, overallDifficulty));

        /// <summary>
        /// The contract's example upload, on <paramref name="map"/>.
        /// </summary>
        public static PlayUpload Upload(byte[] map, string? scoreId = null, params string[] mods)
        {
            var upload = JsonConvert.DeserializeObject<PlayUpload>(File.ReadAllText(ContractPath("play-upload.example.json")))!;

            upload.Beatmap.Md5 = PlayCalculator.Md5(map);
            upload.ScoreId = scoreId ?? Guid.NewGuid().ToString();
            upload.Mods = new List<APIMod>();

            foreach (string mod in mods)
                upload.Mods.Add(new APIMod { Acronym = mod });

            return upload;
        }

        /// <summary>
        /// A stored play with a given rating, for profile maths.
        /// </summary>
        public static PlayRecord Rated(long id, int beatmapId, double pp, long totalScore = 500000, double accuracy = 0.97, bool awardsPp = true, params string[] mods)
        {
            var play = new PlayRecord
            {
                Id = id,
                BeatmapId = beatmapId,
                BeatmapMd5 = $"md5-{beatmapId}",
                Pp = pp,
                TotalScore = totalScore,
                Accuracy = accuracy,
                AwardsPp = awardsPp,
                Passed = true,
            };

            foreach (string mod in mods)
                play.Mods.Add(new APIMod { Acronym = mod });

            return play;
        }
    }
}
