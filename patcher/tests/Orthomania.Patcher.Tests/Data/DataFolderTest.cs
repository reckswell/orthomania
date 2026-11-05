using System.IO;
using NUnit.Framework;
using Orthomania.Patcher.Data;

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
            Assert.That(Directory.GetFileSystemEntries(data.Path), Has.Length.EqualTo(1), "only the note");
            Assert.That(data.HoldsOsuData(out _), Is.False);
        }

        [TestCase("client.realm")]
        [TestCase("client_52.realm")]
        [TestCase("game.ini")]
        [TestCase("game.dev.ini")]
        public void TestSeesOsuDataByName(string name)
        {
            using var temp = new TempFolder();
            var data = new DataFolder(temp.Path);

            File.WriteAllText(temp.Combine(name), "osu!'s");

            Assert.That(data.HoldsOsuData(out string? found), Is.True);
            Assert.That(found, Is.EqualTo(name));
        }

        [Test]
        public void TestOrthomaniasOwnFilesAreNotOsuData()
        {
            using var temp = new TempFolder();
            var data = new DataFolder(temp.Path);

            Directory.CreateDirectory(temp.Combine("maniaortho", "replays"));
            File.WriteAllText(temp.Combine("maniaortho", "plays.csv"), "map,score");
            File.WriteAllText(temp.Combine("maniaortho", "client.realm"), "not at the top");

            Assert.That(data.HoldsOsuData(out string? found), Is.False, found);
            Assert.That(new DataFolder(temp.Combine("missing")).HoldsOsuData(out _), Is.False);
        }

        [Test]
        public void TestOsuDataFolderIsOnlyAPath()
        {
            using var temp = new TempFolder();

            Directory.CreateDirectory(temp.Combine("osu"));
            File.WriteAllText(temp.Combine("osu", "storage.ini"), $"FullPath = {temp.Combine("moved")}\r\n");

            Assert.That(DataFolder.OsuDataFolder(temp.Path), Is.EqualTo(temp.Combine("osu")), "osu!'s storage.ini is not read");
        }
    }
}
