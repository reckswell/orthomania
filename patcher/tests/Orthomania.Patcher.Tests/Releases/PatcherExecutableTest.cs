using System.IO;
using NUnit.Framework;
using Orthomania.Patcher.Releases;

namespace Orthomania.Patcher.Tests.Releases
{
    [TestFixture]
    public class PatcherExecutableTest
    {
        private TempFolder temp = null!;
        private PatcherExecutable self = null!;

        [SetUp]
        public void SetUp()
        {
            temp = new TempFolder();
            self = new PatcherExecutable(temp.Combine("orthomania.exe"));
            File.WriteAllText(self.Path, "MZ old");
        }

        [TearDown]
        public void TearDown() => temp.Dispose();

        [Test]
        public void TestReplacesItselfAndKeepsTheOldOneAside()
        {
            File.WriteAllText(self.DownloadPath, "MZ new");

            self.Replace(self.DownloadPath);

            Assert.That(File.ReadAllText(self.Path), Is.EqualTo("MZ new"));
            Assert.That(File.ReadAllText(self.OldPath), Is.EqualTo("MZ old"));
            Assert.That(File.Exists(self.DownloadPath), Is.False);
        }

        [Test]
        public void TestRefusesADownloadThatIsNotAProgram()
        {
            File.WriteAllText(self.DownloadPath, "<html>not found</html>");

            Assert.That(() => self.Replace(self.DownloadPath), Throws.TypeOf<InvalidDataException>());
            Assert.That(File.ReadAllText(self.Path), Is.EqualTo("MZ old"));
            Assert.That(File.Exists(self.OldPath), Is.False);
        }

        [Test]
        public void TestCleanUpRemovesWhatAnUpdateLeft()
        {
            File.WriteAllText(self.OldPath, "MZ older");
            File.WriteAllText(self.DownloadPath, "MZ half");

            self.CleanUp(_ => { });

            Assert.That(File.Exists(self.OldPath), Is.False);
            Assert.That(File.Exists(self.DownloadPath), Is.False);
            Assert.That(File.ReadAllText(self.Path), Is.EqualTo("MZ old"));
        }
    }
}
