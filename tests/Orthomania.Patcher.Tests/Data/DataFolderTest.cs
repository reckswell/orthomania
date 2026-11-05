using System.IO;
using NUnit.Framework;
using Orthomania.Patcher.Data;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Tests.Data
{
    [TestFixture]
    public class DataFolderTest
    {
        [Test]
        public void TestPrepareLeavesANote()
        {
            using var temp = new TempFolder();
            var data = new DataFolder(temp.Combine("ortho-data"));

            data.Prepare();

            Assert.That(File.Exists(Path.Combine(data.Path, DataFolder.NOTE)), Is.True);
        }

        [Test]
        public void TestAdoptsTheNewestDevelopmentDatabase()
        {
            using var temp = new TempFolder();
            var data = new DataFolder(temp.Path);

            File.WriteAllText(temp.Combine("client_9.realm"), "schema 9");
            File.WriteAllText(temp.Combine("client_52.realm"), "schema 52");
            File.WriteAllText(temp.Combine("client_52.realm.lock"), "lock");
            File.WriteAllText(temp.Combine("client_52_newer_version.realm"), "a backup lazer made");

            Assert.That(data.AdoptDevelopmentDatabase(), Is.EqualTo(temp.Combine("client_52.realm")));
            Assert.That(File.ReadAllText(data.Database), Is.EqualTo("schema 52"));
            Assert.That(File.Exists(temp.Combine("client_52.realm")), Is.True, "the original stays");

            File.WriteAllText(data.Database, "played on since");
            Assert.That(data.AdoptDevelopmentDatabase(), Is.Null, "never replaces a release database");
            Assert.That(File.ReadAllText(data.Database), Is.EqualTo("played on since"));
        }

        [Test]
        public void TestBacksUpTheDatabase()
        {
            using var temp = new TempFolder();
            var data = new DataFolder(temp.Combine("data"));
            string backups = temp.Combine("backups");

            data.Prepare();
            Assert.That(data.BackupDatabase(backups, LazerVersion.Parse("2026.1105.0")), Is.Null, "nothing to back up yet");

            File.WriteAllText(data.Database, "scores");

            string? backup = data.BackupDatabase(backups, LazerVersion.Parse("2026.1105.0"));

            Assert.That(backup, Is.Not.Null);
            Assert.That(Path.GetFileName(backup), Does.StartWith("client-before-2026.1105.0-"));
            Assert.That(File.ReadAllText(backup!), Is.EqualTo("scores"));
        }

        [Test]
        public void TestKeepsTheNewestBackups()
        {
            using var temp = new TempFolder();
            var data = new DataFolder(temp.Combine("data"));
            string backups = temp.Combine("backups");

            data.Prepare();
            File.WriteAllText(data.Database, "scores");
            Directory.CreateDirectory(backups);

            for (int i = 0; i < 4; i++)
            {
                string old = Path.Combine(backups, $"client-before-2026.{100 + i}.0-old.realm");
                File.WriteAllText(old, "old");
                File.SetLastWriteTimeUtc(old, new System.DateTime(2026, 1, 1 + i, 0, 0, 0, System.DateTimeKind.Utc));
            }

            string? newest = data.BackupDatabase(backups, LazerVersion.Parse("2026.1105.0"), keep: 2);

            Assert.That(Directory.GetFiles(backups, "*.realm"), Has.Length.EqualTo(2));
            Assert.That(File.Exists(newest), Is.True);
            Assert.That(File.Exists(Path.Combine(backups, "client-before-2026.103.0-old.realm")), Is.True);
        }

        [Test]
        public void TestMovesScreenshotsWithoutReplacing()
        {
            using var temp = new TempFolder();
            var data = new DataFolder(temp.Combine("data"));
            string stable = temp.Combine("osu!");

            Directory.CreateDirectory(data.Screenshots);
            Directory.CreateDirectory(Path.Combine(stable, "Screenshots"));

            File.WriteAllText(Path.Combine(data.Screenshots, "a.png"), "new a");
            File.WriteAllText(Path.Combine(data.Screenshots, "b.png"), "new b");
            File.WriteAllText(Path.Combine(stable, "Screenshots", "b.png"), "stable's b");

            Assert.That(data.MoveScreenshots(stable), Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(stable, "Screenshots", "a.png")), Is.EqualTo("new a"));
            Assert.That(File.ReadAllText(Path.Combine(stable, "Screenshots", "b.png")), Is.EqualTo("stable's b"));
            Assert.That(File.Exists(Path.Combine(data.Screenshots, "b.png")), Is.True, "kept until it can move");
        }

        [Test]
        public void TestNoStableNoMove()
        {
            using var temp = new TempFolder();
            var data = new DataFolder(temp.Combine("data"));

            Directory.CreateDirectory(data.Screenshots);
            File.WriteAllText(Path.Combine(data.Screenshots, "a.png"), "a");

            Assert.That(data.MoveScreenshots(temp.Combine("no-stable")), Is.Zero);
        }

        [Test]
        public void TestFindsPlainLazerData()
        {
            using var temp = new TempFolder();

            Assert.That(DataFolder.PlainLazerDataFolders(temp.Path), Is.EqualTo(new[] { temp.Combine("osu") }));

            Directory.CreateDirectory(temp.Combine("osu"));
            File.WriteAllText(temp.Combine("osu", "storage.ini"), $"FullPath = {temp.Combine("moved")}\r\n");

            Assert.That(DataFolder.PlainLazerDataFolders(temp.Path), Is.EqualTo(new[] { temp.Combine("osu"), temp.Combine("moved") }));
        }
    }
}
