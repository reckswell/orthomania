using NUnit.Framework;
using Orthomania.Patcher.Lazer;
using Orthomania.Patcher.Settings;

namespace Orthomania.Patcher.Tests.Settings
{
    [TestFixture]
    public class PatcherStateTest
    {
        private static readonly LazerVersion october = LazerVersion.Parse("2026.1026.0");
        private static readonly LazerVersion november = LazerVersion.Parse("2026.1105.0");

        [Test]
        public void TestDataOnlyMovesForward()
        {
            var state = new PatcherState();

            Assert.That(state.MayOpenData(october), Is.True);
            Assert.That(state.IsFirstStartOn(october), Is.True);

            state.RecordStarted(november, "0.1.0");

            Assert.That(state.DataOpenedBy, Is.EqualTo("2026.1105.0"));
            Assert.That(state.MayOpenData(november), Is.True);
            Assert.That(state.IsFirstStartOn(november), Is.False);
            Assert.That(state.MayOpenData(october), Is.False, "november may have upgraded the database past what october opens");

            state.RecordStarted(october, "0.1.0");
            Assert.That(state.DataOpenedBy, Is.EqualTo("2026.1105.0"), "never moves back");
        }

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
            Assert.That(read.DataOpenedBy, Is.EqualTo("2026.1026.0"));
            Assert.That(System.IO.File.ReadAllText(path), Does.Not.Contain("version\""), "derived values are not saved");
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
