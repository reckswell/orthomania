using System.IO;
using System.Linq;
using ManiaOrtho.ScoreServerContract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OrthoServer.Tests
{
    /// <summary>
    /// The contract's examples read as the shared types with nothing left over, and write back with the same fields.
    /// </summary>
    [TestFixture]
    public class ContractTest
    {
        private static readonly JsonSerializerSettings strict = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };

        [TestCase("play-upload.example.json", typeof(PlayUpload))]
        [TestCase("play-result.example.json", typeof(PlayResult))]
        public void TestExampleMatchesType(string file, System.Type type)
        {
            string json = File.ReadAllText(TestPlays.ContractPath(file));

            object read = JsonConvert.DeserializeObject(json, type, strict)!;

            Assert.That(fieldNames(JToken.FromObject(read)), Is.EquivalentTo(fieldNames(JToken.Parse(json))));
        }

        private static string[] fieldNames(JToken token)
            => token.SelectTokens("..*").OfType<JValue>().Select(v => v.Path).Where(p => !p.StartsWith("mods", System.StringComparison.Ordinal)).ToArray();
    }
}
