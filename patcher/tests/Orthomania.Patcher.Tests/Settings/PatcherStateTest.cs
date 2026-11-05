using NUnit.Framework;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Settings;

namespace Orthomania.Patcher.Tests.Settings
{
    [TestFixture]
    public class PatcherStateTest
    {
        private static readonly LazerVersion october = LazerVersion.Parse("2026.1026.0");

        [Test]
        public void TestSavesAndLoads()
        {
            using var temp = new TempFolder();
            string path = temp.Combine("state.json");

            var state = new PatcherState();
            state.RecordStarted(october, "0.1.0");
            state.RecordWorking(october, "0.1.0");
            JsonFile.Write(path, state);

            var read = JsonFile.ReadOrDefault<PatcherState>(path, out string? problem);

            Assert.That(problem, Is.Null);
            Assert.That(read.LastWorking, Is.EqualTo(new PairRecord("2026.1026.0", "0.1.0")));
            Assert.That(read.LastWorking!.Version, Is.EqualTo(october));
            Assert.That(read.LastStarted, Is.EqualTo(new PairRecord("2026.1026.0", "0.1.0")));
            Assert.That(System.IO.File.ReadAllText(path), Does.Not.Contain("version\""), "derived values are not saved");
        }

        [Test]
        public void TestReadsAnEarlierPatchersState()
        {
            using var temp = new TempFolder();
            string path = temp.Combine("state.json");
            System.IO.File.WriteAllText(path, "{ \"lastWorking\": { \"lazer\": \"2026.1026.0\", \"build\": \"0.1.0\" }, \"dataOpenedBy\": \"2026.1026.0\" }");

            var read = JsonFile.ReadOrDefault<PatcherState>(path, out string? problem);

            Assert.That(problem, Is.Null);
            Assert.That(read.LastWorking, Is.EqualTo(new PairRecord("2026.1026.0", "0.1.0")));
        }

        [Test]
        public void TestUnreadableFileStartsOver()
        {
            using var temp = new TempFolder();
            string path = temp.Combine("state.json");
            System.IO.File.WriteAllText(path, "{ not json");

            var read = JsonFile.ReadOrDefault<PatcherState>(path, out string? problem);

            Assert.That(read.LastWorking, Is.Null);
            Assert.That(problem, Does.Contain("state.json"));
        }
    }
}
