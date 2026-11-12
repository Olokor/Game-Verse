using System;
using System.Collections.Generic;
using System.Globalization;
using GameVerse.Rules.Random;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameVerse.Platform.Diagnostics
{
    /// <summary>
    /// Runs the shared PRNG golden vectors (shared/rules-fixtures/prng/xoshiro256ss.json, the file `dotnet test` reads)
    /// inside the player. On a device this executes GameVerse.Rules compiled by IL2CPP, proving the client produces the
    /// same sequence as the server. The EditMode test runs the same check in the editor.
    /// </summary>
    public static class RulesSelfCheck
    {
        public const string FixtureResource = "Fixtures/prng/xoshiro256ss";
        public const string LogPrefix = "GV_SELFCHECK";

        public sealed class Report
        {
            public Report(bool ok, int checkedValues, string firstValue, string detail)
            {
                Ok = ok;
                CheckedValues = checkedValues;
                FirstValue = firstValue;
                Detail = detail;
            }

            public bool Ok { get; }

            /// <summary>Number of PRNG outputs and dice rolls compared.</summary>
            public int CheckedValues { get; }

            /// <summary>First output of the first fromSeed vector, to compare by eye with dotnet test.</summary>
            public string FirstValue { get; }

            public string Detail { get; }

            public string ToLogLine() => string.Format(
                CultureInfo.InvariantCulture,
                "{0} prng {1} n={2} first={3} runtime={4} {5}",
                LogPrefix, Ok ? "ok" : "FAIL", CheckedValues, FirstValue, RuntimeName(), Detail).TrimEnd();
        }

        public static Report Run()
        {
            var asset = Resources.Load<TextAsset>(FixtureResource);
            if (asset == null)
            {
                return new Report(false, 0, "-", "fixture missing: run `make rules` before building");
            }

            try
            {
                return Check(JObject.Parse(asset.text));
            }
            catch (Exception e)
            {
                return new Report(false, 0, "-", "error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Compares every vector in the fixture. Public so the EditMode test can call it with the same data.</summary>
        public static Report Check(JObject vectors)
        {
            var count = 0;
            string first = null;

            foreach (var vector in vectors["fromState"])
            {
                var rng = Xoshiro256StarStar.FromState(ULongs(vector["state"]));
                foreach (var expected in ULongs(vector["outputs"]))
                {
                    var actual = rng.NextULong();
                    count++;
                    if (actual != expected) return Mismatch("fromState", count, expected, actual);
                }
            }

            foreach (var vector in vectors["fromSeed"])
            {
                var rng = new Xoshiro256StarStar(ULong(vector["seed"]));
                var state = rng.GetState();
                var expectedState = ULongs(vector["state"]);
                for (var i = 0; i < expectedState.Count; i++)
                {
                    if (state[i] != expectedState[i]) return Mismatch("seed state", i, expectedState[i], state[i]);
                }

                foreach (var expected in ULongs(vector["outputs"]))
                {
                    var actual = rng.NextULong();
                    first ??= actual.ToString(CultureInfo.InvariantCulture);
                    count++;
                    if (actual != expected) return Mismatch("fromSeed", count, expected, actual);
                }
            }

            var dice = vectors["dice"];
            var diceRng = new Xoshiro256StarStar(ULong(dice["seed"]));
            foreach (var roll in dice["rolls"])
            {
                var expected = roll.Value<int>();
                var actual = diceRng.NextInt(6) + 1;
                count++;
                if (actual != expected) return Mismatch("dice", count, (ulong)expected, (ulong)actual);
            }

            return new Report(true, count, first ?? "-", string.Empty);
        }

        private static Report Mismatch(string what, int index, ulong expected, ulong actual) =>
            new Report(false, index, "-", string.Format(CultureInfo.InvariantCulture, "mismatch {0} #{1} expected={2} actual={3}", what, index, expected, actual));

        // Fixture stores ulongs as strings (JSON numbers cannot hold 64-bit values safely in every parser).
        private static ulong ULong(JToken token) => ulong.Parse(token.Value<string>(), NumberStyles.None, CultureInfo.InvariantCulture);

        private static List<ulong> ULongs(JToken array)
        {
            var list = new List<ulong>();
            foreach (var item in array) list.Add(ULong(item));
            return list;
        }

        private static string RuntimeName()
        {
#if ENABLE_IL2CPP
            return "il2cpp";
#else
            return "mono";
#endif
        }
    }
}
