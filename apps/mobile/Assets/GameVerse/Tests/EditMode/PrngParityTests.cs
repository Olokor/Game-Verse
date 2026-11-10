using GameVerse.Platform.Diagnostics;
using GameVerse.Rules.Random;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameVerse.Tests
{
    /// <summary>
    /// GameVerse.Rules' PRNG inside Unity produces exactly the sequences `dotnet test` checks
    /// (shared/GameVerse.Rules.Tests/Random/Xoshiro256StarStarTests.cs), from the same fixture file.
    /// </summary>
    public class PrngParityTests
    {
        private static JObject Vectors()
        {
            var asset = Resources.Load<TextAsset>(RulesSelfCheck.FixtureResource);
            Assert.That(asset, Is.Not.Null, "Fixture missing. Run `make rules` to copy shared/rules-fixtures into Unity.");
            return JObject.Parse(asset.text);
        }

        [Test]
        public void All_golden_vectors_match()
        {
            var report = RulesSelfCheck.Check(Vectors());
            Assert.That(report.Ok, Is.True, report.ToLogLine());
            Assert.That(report.CheckedValues, Is.GreaterThan(20));
            Debug.Log(report.ToLogLine());
        }

        [Test]
        public void First_seeded_output_matches_the_fixture_directly()
        {
            var vector = Vectors()["fromSeed"][0];
            var rng = new Xoshiro256StarStar(ulong.Parse(vector["seed"].Value<string>()));
            Assert.That(rng.NextULong().ToString(), Is.EqualTo(vector["outputs"][0].Value<string>()));
        }

        [Test]
        public void A_corrupted_vector_is_reported_so_the_check_is_not_vacuous()
        {
            var vectors = Vectors();
            var outputs = (JArray)vectors["fromSeed"][0]["outputs"];
            outputs[0] = "1";
            var report = RulesSelfCheck.Check(vectors);
            Assert.That(report.Ok, Is.False);
            StringAssert.Contains("mismatch fromSeed", report.Detail);
        }
    }
}
