// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.ManiaOrtho.Scoring;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Scoring
{
    [TestFixture]
    public class PlayLogTest
    {
        [Test]
        public void TestFieldsThatNeedItAreQuoted()
        {
            string row = PlayLog.FormatRow(new[] { "plain", "a, b", "say \"hi\"", null, "two\nlines" });

            Assert.That(row, Is.EqualTo("plain,\"a, b\",\"say \"\"hi\"\"\",,\"two\nlines\""));
            Assert.That(ParseRow(row), Is.EqualTo(new[] { "plain", "a, b", "say \"hi\"", "", "two\nlines" }));
        }

        [Test]
        public void TestHeaderAndMarkOnlyStartAFile()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log"))
            {
                PlayLog.Append(storage, row("first"));
                PlayLog.Append(storage, row("second"));

                byte[] bytes = File.ReadAllBytes(storage.GetFullPath(PlayLog.FILENAME));
                byte[] mark = Encoding.UTF8.GetPreamble();
                string text = Encoding.UTF8.GetString(bytes, mark.Length, bytes.Length - mark.Length);

                Assert.That(bytes.Take(mark.Length), Is.EqualTo(mark), "starts with a byte-order mark");
                // By character: culture-aware string search ignores a zero-width mark and finds it anywhere.
                Assert.That(text.IndexOf('\uFEFF'), Is.EqualTo(-1), "only one mark");

                string[] lines = text.Split("\r\n");

                Assert.That(lines, Has.Length.EqualTo(4), "header, two rows, and the final line break");
                Assert.That(lines[0], Is.EqualTo(string.Join(",", PlayLog.COLUMNS)));
                Assert.That(ParseRow(lines[1])[0], Is.EqualTo("first"));
                Assert.That(ParseRow(lines[2])[0], Is.EqualTo("second"));
                Assert.That(lines[3], Is.Empty);
            }
        }

        [Test]
        public void TestAnOlderHeaderStartsANewFile()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log-columns"))
            {
                File.WriteAllText(storage.GetFullPath(PlayLog.FILENAME, true), "time,result\r\nold,pass\r\n", new UTF8Encoding(true));

                PlayLog.Append(storage, row("new"));

                string[] kept = storage.GetFiles(".", "plays until *.csv").ToArray();

                Assert.That(kept, Has.Length.EqualTo(1), "the old file is kept under a dated name");
                Assert.That(File.ReadAllText(storage.GetFullPath(kept[0])), Is.EqualTo("time,result\r\nold,pass\r\n"));
                Assert.That(File.ReadAllLines(storage.GetFullPath(PlayLog.FILENAME)), Is.EqualTo(new[] { string.Join(",", PlayLog.COLUMNS), PlayLog.FormatRow(row("new")) }));
            }
        }

        [Test]
        public void TestRowsReadBack()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log-read"))
            {
                string?[] tricky = row("first");
                tricky[3] = "a, \"quoted\" title";
                tricky[4] = "two\r\nlines";
                PlayLog.Append(storage, tricky);
                PlayLog.Append(storage, row("second"));

                var rows = PlayLog.ParseRows(File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME)));

                Assert.That(rows, Has.Count.EqualTo(3));
                Assert.That(rows[0], Is.EqualTo(PlayLog.COLUMNS));
                Assert.That(rows[1], Is.EqualTo(tricky.Select(v => v ?? string.Empty)));
                Assert.That(rows[2], Is.EqualTo(row("second").Select(v => v ?? string.Empty)));
            }
        }

        [Test]
        public void TestRewriteKeepsTheOldFile()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log-rewrite"))
            {
                PlayLog.Append(storage, row("first"));
                PlayLog.Append(storage, row("second"));
                string before = File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME));

                Assert.That(PlayLog.Rewrite(storage, _ => false, "unused.csv"), Is.False, "nothing to write");
                Assert.That(storage.Exists("unused.csv"), Is.False);

                Assert.That(PlayLog.Rewrite(storage, plays =>
                {
                    plays[1][0] = "changed";
                    return true;
                }, "backup.csv"), Is.True);

                Assert.That(File.ReadAllText(storage.GetFullPath("backup.csv")), Is.EqualTo(before));
                Assert.That(File.ReadAllBytes(storage.GetFullPath(PlayLog.FILENAME)).Take(3), Is.EqualTo(Encoding.UTF8.GetPreamble()), "still marked UTF-8");
                Assert.That(File.ReadAllLines(storage.GetFullPath(PlayLog.FILENAME)),
                    Is.EqualTo(new[] { string.Join(",", PlayLog.COLUMNS), PlayLog.FormatRow(row("first")), PlayLog.FormatRow(row("changed")) }));

                PlayLog.Append(storage, row("third"));
                Assert.That(File.ReadAllLines(storage.GetFullPath(PlayLog.FILENAME)), Has.Length.EqualTo(4), "plays are added after it as before");
            }
        }

        [Test]
        public void TestRewriteLeavesALogOfOtherColumns()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log-rewrite-columns"))
            {
                File.WriteAllText(storage.GetFullPath(PlayLog.FILENAME, true), "time,result\r\nold,pass\r\n", new UTF8Encoding(true));

                Assert.That(PlayLog.Rewrite(storage, _ => true, "backup.csv"), Is.False);
                Assert.That(File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME)), Is.EqualTo("time,result\r\nold,pass\r\n"));
                Assert.That(storage.Exists("backup.csv"), Is.False);
            }
        }

        [Test]
        public void TestRewriteNumbersATakenBackup()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log-rewrite-backups"))
            {
                PlayLog.Append(storage, row("first"));
                File.WriteAllText(storage.GetFullPath("backup.csv"), "an earlier backup");

                Assert.That(PlayLog.Rewrite(storage, _ => true, "backup.csv"), Is.True);
                Assert.That(PlayLog.Rewrite(storage, _ => true, "backup.csv"), Is.True);

                Assert.That(File.ReadAllText(storage.GetFullPath("backup.csv")), Is.EqualTo("an earlier backup"));
                Assert.That(storage.Exists("backup (2).csv") && storage.Exists("backup (3).csv"), Is.True);
            }
        }

        /// <summary>
        /// A log a spreadsheet saved in another encoding (Excel's "CSV" is the system code page): read as UTF-8 and written
        /// back, its accents would be lost.
        /// </summary>
        [Test]
        public void TestLogInAnotherEncodingIsLeftAlone()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log-encoding"))
            {
                string?[] accented = row("first");
                accented[4] = "Café";
                byte[] latin1 = Encoding.Latin1.GetBytes(PlayLog.FormatRow(PlayLog.COLUMNS) + "\r\n" + PlayLog.FormatRow(accented) + "\r\n");
                File.WriteAllBytes(storage.GetFullPath(PlayLog.FILENAME, true), latin1);

                Assert.Throws<InvalidDataException>(() => PlayLog.ReadPlays(storage));
                Assert.Throws<InvalidDataException>(() => PlayLog.Rewrite(storage, _ => true, "backup.csv"));
                Assert.That(File.ReadAllBytes(storage.GetFullPath(PlayLog.FILENAME)), Is.EqualTo(latin1));
                Assert.That(storage.Exists("backup.csv"), Is.False);

                File.WriteAllText(storage.GetFullPath(PlayLog.FILENAME), Encoding.Latin1.GetString(latin1), new UTF8Encoding(false));
                Assert.That(PlayLog.ReadPlays(storage)![0][4], Is.EqualTo("Café"), "UTF-8 without a byte-order mark reads");
            }
        }

        /// <summary>
        /// A log open in a program that locks it (Excel does): nothing is written, and nothing is left beside it.
        /// </summary>
        [Test]
        public void TestLockedLogIsLeftWhole()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log-locked"))
            {
                PlayLog.Append(storage, row("first"));
                string before = File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME));

                using (new FileStream(storage.GetFullPath(PlayLog.FILENAME), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.Throws(Is.InstanceOf<IOException>(), () => PlayLog.Rewrite(storage, _ => true, "backup.csv"));

                Assert.That(File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME)), Is.EqualTo(before));
                Assert.That(storage.GetFiles(".").Select(Path.GetFileName), Is.EqualTo(new[] { PlayLog.FILENAME }));
            }
        }

        /// <summary>
        /// Writing the new file fails (here a folder is in its way): the old file is whole, and neither the new file nor the
        /// backup is left.
        /// </summary>
        [Test]
        public void TestFailedRewriteLeavesTheOldFile()
        {
            using (var storage = new TemporaryNativeStorage("maniaortho-play-log-rewrite-fails"))
            {
                PlayLog.Append(storage, row("first"));
                string before = File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME));
                Directory.CreateDirectory(storage.GetFullPath(PlayLog.FILENAME + ".new"));

                Assert.Throws(Is.InstanceOf<UnauthorizedAccessException>().Or.InstanceOf<IOException>(), () => PlayLog.Rewrite(storage, _ => true, "backup.csv"));

                Assert.That(File.ReadAllText(storage.GetFullPath(PlayLog.FILENAME)), Is.EqualTo(before));
                Assert.That(storage.Exists("backup.csv"), Is.False);
            }
        }

        private static string?[] row(string time)
        {
            string?[] values = new string?[PlayLog.COLUMNS.Length];
            values[0] = time;
            return values;
        }

        /// <summary>
        /// Splits one CSV line written by <see cref="PlayLog.FormatRow"/>.
        /// </summary>
        public static string[] ParseRow(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                        quoted = false;
                    else
                        field.Append(c);
                }
                else if (c == '"')
                    quoted = true;
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                    field.Append(c);
            }

            fields.Add(field.ToString());
            return fields.ToArray();
        }
    }
}
