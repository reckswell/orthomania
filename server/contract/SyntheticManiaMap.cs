using System.Globalization;
using System.Text;

namespace ManiaOrtho.ScoreServerContract
{
    /// <summary>
    /// Writes a small osu!mania map of our own (no song, no copyrighted content) as <c>.osu</c> text. Compiled into both the
    /// game's tests and the score server's tests, so both rate exactly the same file.
    /// </summary>
    public static class SyntheticManiaMap
    {
        /// <param name="keys">Key count (CircleSize).</param>
        /// <param name="notes">Number of notes; every seventh is a short hold, every fifth has a second note beside it.</param>
        /// <param name="bpm">Notes are a quarter beat apart.</param>
        /// <param name="overallDifficulty">OverallDifficulty.</param>
        public static string Create(int keys, int notes, double bpm, double overallDifficulty)
        {
            var text = new StringBuilder();
            double beat = 60000 / bpm;
            double step = beat / 4;
            const int start = 1000;

            text.Append("osu file format v14\n\n");
            text.Append("[General]\nAudioFilename: audio.mp3\nAudioLeadIn: 0\nPreviewTime: -1\nCountdown: 0\nSampleSet: Normal\nStackLeniency: 0.7\nMode: 3\nLetterboxInBreaks: 0\nSpecialStyle: 0\nWidescreenStoryboard: 0\n\n");
            text.Append(CultureInfo.InvariantCulture,
                $"[Metadata]\nTitle:Synthetic {keys}K\nTitleUnicode:Synthetic {keys}K\nArtist:maniaortho tests\nArtistUnicode:maniaortho tests\nCreator:maniaortho\nVersion:{notes} notes {bpm} bpm OD{overallDifficulty}\nSource:\nTags:\nBeatmapID:0\nBeatmapSetID:-1\n\n");
            text.Append(CultureInfo.InvariantCulture,
                $"[Difficulty]\nHPDrainRate:8\nCircleSize:{keys}\nOverallDifficulty:{overallDifficulty}\nApproachRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n\n");
            text.Append("[Events]\n//Background and Video events\n\n");
            text.Append(CultureInfo.InvariantCulture, $"[TimingPoints]\n{start},{beat},4,1,0,60,1,0\n\n");
            text.Append("[HitObjects]\n");

            for (int i = 0; i < notes; i++)
            {
                // A pattern that walks the columns and doubles up now and then, so it has chords, jacks and streams. Holds end before
                // the next note, so no column has two objects at once.
                int column = (i * 3 + i / keys) % keys;
                int x = (int)((column + 0.5) * 512 / keys);
                int time = (int)(start + i * step);

                if (i % 7 == 6)
                    text.Append(CultureInfo.InvariantCulture, $"{x},192,{time},128,0,{(int)(time + step * 0.8)}:0:0:0:0:\n");
                else
                    text.Append(CultureInfo.InvariantCulture, $"{x},192,{time},1,0,0:0:0:0:\n");

                if (i % 5 == 0)
                {
                    int second = (column + keys / 2) % keys;
                    text.Append(CultureInfo.InvariantCulture, $"{(int)((second + 0.5) * 512 / keys)},192,{time},1,0,0:0:0:0:\n");
                }
            }

            return text.ToString();
        }
    }
}
