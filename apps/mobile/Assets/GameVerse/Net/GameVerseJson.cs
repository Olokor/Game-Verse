using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace GameVerse.Net
{
    /// <summary>
    /// Newtonsoft settings that read and write exactly what the server's System.Text.Json (Web defaults) produces for
    /// GameVerse.Contracts types. Verified against golden JSON written by the backend (shared/rules-fixtures/contracts,
    /// EditMode test WireFormatTests). Use these settings for every REST and realtime payload; never the defaults.
    /// </summary>
    public static class GameVerseJson
    {
        /// <summary>Shared, immutable after construction. Thread-safe for serialization.</summary>
        public static readonly JsonSerializerSettings Settings = CreateSettings();

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings)!;

        private static JsonSerializerSettings CreateSettings() => new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                // camelCase properties like JsonNamingPolicy.CamelCase, but leave dictionary keys alone:
                // System.Text.Json does not rename keys ("Frame" stays "Frame"), Newtonsoft's CamelCasePropertyNamesContractResolver would.
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = false },
            },

            // STJ Web defaults write nulls.
            NullValueHandling = NullValueHandling.Include,

            // No "$type" metadata: wire types are plain POCOs, and type names from the wire are a deserialization risk.
            TypeNameHandling = TypeNameHandling.None,

            // Keep ISO strings as strings; STJ does not guess dates.
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            Culture = CultureInfo.InvariantCulture,
            Formatting = Formatting.None,

            // Unknown fields are ignored (forward compatible), like STJ.
            MissingMemberHandling = MissingMemberHandling.Ignore,
            MaxDepth = 64,
        };
    }
}
