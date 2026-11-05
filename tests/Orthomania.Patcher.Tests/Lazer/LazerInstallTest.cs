using System.IO;
using NUnit.Framework;
using Orthomania.Patcher.Lazer;

namespace Orthomania.Patcher.Tests.Lazer
{
    [TestFixture]
    public class LazerInstallTest
    {
        [Test]
        public void TestFindsNothingWithoutTheGame()
        {
            using var temp = new TempFolder();
            Assert.That(LazerInstall.Find(temp.Path), Is.Null);
        }

        [Test]
        public void TestReadsReleaseFromVelopack()
        {
            using var temp = new TempFolder();
            var install = Fakes.Lazer(temp.Path, "2026.1026.0-lazer");

            var release = install.ReadRelease();

            Assert.That(release.Version, Is.EqualTo(LazerVersion.Parse("2026.1026.0")));
            Assert.That(release.VersionText, Is.EqualTo("2026.1026.0-lazer"));
            Assert.That(release.AppId, Is.EqualTo("osulazer"));
            Assert.That(release.Channel, Is.EqualTo("win"));
        }

        [Test]
        public void TestUnknownReleaseIsAnError()
        {
            using var temp = new TempFolder();
            var install = Fakes.Lazer(temp.Path);
            File.Delete(install.VersionManifest);

            Assert.That(() => install.ReadRelease(), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void TestContains()
        {
            using var temp = new TempFolder();
            var install = Fakes.Lazer(temp.Path);

            Assert.That(install.Contains(install.GameExecutable), Is.True);
            Assert.That(install.Contains(temp.Path), Is.True);
            Assert.That(install.Contains(temp.Path + "-other"), Is.False);
        }
    }
}
