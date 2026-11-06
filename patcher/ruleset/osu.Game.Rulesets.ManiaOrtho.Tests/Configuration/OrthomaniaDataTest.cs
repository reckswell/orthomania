// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mania.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Configuration;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Rulesets.ManiaOrtho.Hooks;
using osu.Game.Rulesets.ManiaOrtho.Scoring;
using osu.Game.Rulesets.ManiaOrtho.ScoreServer;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Configuration
{
    /// <summary>
    /// What orthomania.exe promises for a build it starts (<c>orthomania-game.json</c>): orthomania's own data in its own folder,
    /// never in osu!'s, and osu! signed out unless the person allowed the osu! sign-in.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class OrthomaniaDataTest
    {
        private string folder = null!;
        private OrthomaniaData? before;

        private string data => Path.Combine(folder, "ortho-data");

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "maniaortho-own-data-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            before = OrthomaniaData.Current;
        }

        [TearDown]
        public void TearDown()
        {
            OrthomaniaData.Current = before;

            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        private void writeFile(string json) => File.WriteAllText(Path.Combine(folder, OrthomaniaData.FILE_NAME), json);

        [Test]
        public void TestReadsWhatThePatcherWrites()
        {
            // As orthomania.exe's GameSettings writes it.
            writeFile($"{{\r\n  \"format\": 1,\r\n  \"dataFolder\": {System.Text.Json.JsonSerializer.Serialize(data)},\r\n  \"osuSignIn\": false\r\n}}\r\n");

            var read = OrthomaniaData.Read(folder);

            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Folder, Is.EqualTo(data));
            Assert.That(read.OsuSignIn, Is.False);

            writeFile($"{{\"format\":1,\"dataFolder\":{System.Text.Json.JsonSerializer.Serialize(data)},\"osuSignIn\":true}}");
            Assert.That(OrthomaniaData.Read(folder)!.OsuSignIn, Is.True);
        }

        [Test]
        public void TestNoFileKeepsLazersStorage()
        {
            Assert.That(OrthomaniaData.Read(folder), Is.Null);
            Assert.That(OrthomaniaData.Read(null), Is.Null);

            // The tests themselves run without one.
            Assert.That(before, Is.Null);
        }

        [TestCase("not json")]
        [TestCase("{\"format\":2,\"dataFolder\":\"C:\\\\data\",\"osuSignIn\":true}")]
        [TestCase("{\"format\":1,\"dataFolder\":\"relative\\\\data\",\"osuSignIn\":true}")]
        [TestCase("{\"format\":1,\"osuSignIn\":true}")]
        [TestCase("null")]
        public void TestAFileThatCannotBeUsedStillKeepsOsuApart(string json)
        {
            writeFile(json);

            var read = OrthomaniaData.Read(folder);

            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Folder, Is.EqualTo(OrthomaniaData.FallbackFolder));
            Assert.That(read.OsuSignIn, Is.False);
        }

        [Test]
        public void TestPlayLogGoesToTheOwnFolder()
        {
            using var game = new TemporaryNativeStorage("maniaortho-own-data-game");

            OrthomaniaData.Current = new OrthomaniaData(data, false);

            string log = OrthomaniaData.PlayLogFor(game).GetFullPath(".");
            Assert.That(log, Does.StartWith(Path.Combine(data, PlayLog.DIRECTORY)));
            Assert.That(Directory.Exists(log), Is.True);
            Assert.That(Directory.Exists(game.GetFullPath(PlayLog.DIRECTORY)), Is.False);

            // Without orthomania.exe, lazer's storage, as before.
            OrthomaniaData.Current = null;
            Assert.That(OrthomaniaData.PlayLogFor(game).GetFullPath("."), Does.StartWith(game.GetFullPath(PlayLog.DIRECTORY)));
        }

        [Test]
        public void TestSettingsAreKeptInTheOwnFolder()
        {
            OrthomaniaData.Current = new OrthomaniaData(data, false);

            Assert.That(OwnSettings.StoreFor(new SettingsStore(null!)), Is.Null, "no setting goes to osu!'s database");

            var ruleset = new ManiaOrthoRuleset().RulesetInfo;

            using (var config = new ManiaOrthoRulesetConfigManager(null, ruleset))
            {
                config.SetValue(ManiaRulesetSetting.ScrollSpeed, 23.5);
                config.Highway.SetValue(HighwaySetting.Preset, HighwayPreset.Custom);
                config.Highway.SetValue(HighwaySetting.Wall, 0.25f);
                config.ScoreServer.SetValue(ScoreServerSetting.LinkToken, "the server's own token");
                config.Save();
                config.Highway.Save();
                config.ScoreServer.Save();
            }

            Assert.That(Directory.GetFiles(Path.Combine(data, OwnSettings.DIRECTORY)).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "mania.json", "highway.json", "score-server.json" }));
            Assert.That(File.ReadAllText(Path.Combine(data, OwnSettings.DIRECTORY, "score-server.json")), Does.Contain("the server's own token"));

            using (var config = new ManiaOrthoRulesetConfigManager(null, ruleset))
            {
                Assert.That(config.Get<double>(ManiaRulesetSetting.ScrollSpeed), Is.EqualTo(23.5));
                Assert.That(config.Highway.Get<HighwayPreset>(HighwaySetting.Preset), Is.EqualTo(HighwayPreset.Custom));
                Assert.That(config.Highway.Get<float>(HighwaySetting.Wall), Is.EqualTo(0.25f));
                Assert.That(config.ScoreServer.Get<string>(ScoreServerSetting.LinkToken), Is.EqualTo("the server's own token"));

                // Still fixed, whatever was stored.
                Assert.That(config.Get<Mania.UI.ManiaScrollingDirection>(ManiaRulesetSetting.ScrollDirection), Is.EqualTo(Mania.UI.ManiaScrollingDirection.Down));
            }
        }

        [Test]
        public void TestASettingThatCannotBeReadKeepsItsDefault()
        {
            OrthomaniaData.Current = new OrthomaniaData(data, false);
            Directory.CreateDirectory(Path.Combine(data, OwnSettings.DIRECTORY));
            File.WriteAllText(Path.Combine(data, OwnSettings.DIRECTORY, "score-server.json"),
                "{\"ScoreServerEnabled\":\"perhaps\",\"ScoreServerAddress\":\"http://127.0.0.1:9000\",\"NoSuchSetting\":\"1\"}");

            using var config = new ScoreServerConfigManager(null, new ManiaOrthoRuleset().RulesetInfo);

            Assert.That(config.Get<bool>(ScoreServerSetting.ScoreServerEnabled), Is.False);
            Assert.That(config.Get<string>(ScoreServerSetting.ScoreServerAddress), Is.EqualTo("http://127.0.0.1:9000"));

            File.WriteAllText(Path.Combine(data, OwnSettings.DIRECTORY, "score-server.json"), "{ not json");
            using var broken = new ScoreServerConfigManager(null, new ManiaOrthoRuleset().RulesetInfo);
            Assert.That(broken.Get<string>(ScoreServerSetting.ScoreServerAddress), Is.EqualTo(ScoreServerConfigManager.DEFAULT_ADDRESS));
        }

        /// <summary>
        /// Also the check, for each lazer release, that the hook finds lazer's API: a release where it does not gets no build.
        /// </summary>
        [Test]
        public void TestOsuStaysSignedOut()
        {
            var game = (OsuGameBase)RuntimeHelpers.GetUninitializedObject(typeof(OsuGameBase));
            var endpoints = new DevelopmentEndpointConfiguration();

            using var osuData = new TemporaryNativeStorage("maniaortho-own-data-osu");
            using var osuSettings = new OsuConfigManager(osuData);
            osuSettings.SetValue(OsuSetting.Username, "someone");

            // Without the hook, lazer's API takes the username osu! saved.
            using (var plain = new APIAccess(game, osuSettings, endpoints, "test"))
                Assert.That(plain.ProvidedUsername, Is.EqualTo("someone"));

            const string token = "access|99999999999|refresh";
            osuSettings.SetValue(OsuSetting.Token, token);
            osuSettings.Save();
            string savedFile = File.ReadAllText(osuData.GetFullPath("game.ini"));

            var harmony = new Harmony(LazerHooks.HARMONY_ID + ".tests");

            try
            {
                Assert.That(LazerHooks.KeepOsuSignedOut(harmony), Is.True);

                using (var api = new APIAccess(game, osuSettings, endpoints, "test"))
                {
                    Assert.That(api.ProvidedUsername, Is.Empty);
                    Assert.That(api.LocalUser.IsDefault, Is.True);

                    Thread.Sleep(200);
                    Assert.That(api.State.Value, Is.EqualTo(APIState.Offline));

                    api.Logout();
                }

                Assert.That(osuSettings.Get<string>(OsuSetting.Username), Is.EqualTo("someone"));
                Assert.That(osuSettings.Get<string>(OsuSetting.Token), Is.EqualTo(token));
                Assert.That(File.ReadAllText(osuData.GetFullPath("game.ini")), Is.EqualTo(savedFile));
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        /// <summary>
        /// The hook goes in as lazer loads this ruleset, so it holds only if lazer loads its rulesets before it makes its API.
        /// </summary>
        [Test]
        public void TestLazerLoadsRulesetsBeforeItMakesItsApi()
        {
            const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            static int indexOf(System.Collections.Generic.List<KeyValuePair<OpCode, object>> body, Type type)
                => body.FindIndex(i => i.Key == OpCodes.Newobj && i.Value is ConstructorInfo c && c.DeclaringType == type);

            var making = typeof(OsuGameBase).GetMethods(declared)
                                            .Select(m => (method: m, body: PatchProcessor.ReadMethodBody(m).ToList()))
                                            .Where(m => indexOf(m.body, typeof(APIAccess)) >= 0)
                                            .ToList();

            Assert.That(making, Has.Count.EqualTo(1), "one place makes lazer's API");

            var (_, body) = making.Single();
            int rulesets = indexOf(body, typeof(RealmRulesetStore));

            Assert.That(rulesets, Is.GreaterThanOrEqualTo(0), "the rulesets are loaded where the API is made");
            Assert.That(rulesets, Is.LessThan(indexOf(body, typeof(APIAccess))));
        }

        [Test]
        public void TestSignedOutSettingsAreNeverWritten()
        {
            using var config = new SignedOutConfig();

            Assert.That(config.Get<string>(OsuSetting.Username), Is.Empty);
            Assert.That(config.Get<string>(OsuSetting.Token), Is.Empty);

            config.SetValue(OsuSetting.Token, "access|99999999999|refresh");
            Assert.That(config.Save(), Is.True);
        }
    }
}
