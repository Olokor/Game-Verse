using System.Text.Json;

namespace GameVerse.Rules.Tests.Fixtures;

/// <summary>Finds and reads golden fixtures from shared/rules-fixtures/.</summary>
public static class FixtureLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Repo root, found by walking up from the test binaries to the folder holding CLAUDE.md.</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Could not find the repo root (CLAUDE.md) above " + AppContext.BaseDirectory);
        }
    }

    public static string FixtureDir(string game) => Path.Combine(RepoRoot, "shared", "rules-fixtures", game);

    /// <summary>File names (not paths) of every fixture for a game, sorted for stable test ordering.</summary>
    public static IReadOnlyList<string> FileNames(string game)
    {
        var dir = FixtureDir(game);
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException("No fixtures for '" + game + "' at " + dir);
        }

        return Directory.GetFiles(dir, "*.json").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList()!;
    }

    public static RulesFixture Load(string game, string fileName)
    {
        var path = Path.Combine(FixtureDir(game), fileName);
        var fixture = Parse(File.ReadAllText(path), path);
        if (!string.Equals(fixture.Game, game, StringComparison.Ordinal))
        {
            throw new InvalidDataException(path + ": \"game\" is '" + fixture.Game + "' but the file is in the '" + game + "' folder.");
        }

        return fixture;
    }

    public static RulesFixture Parse(string json, string sourceName)
    {
        var fixture = JsonSerializer.Deserialize<RulesFixture>(json, JsonOptions)
            ?? throw new InvalidDataException(sourceName + ": empty fixture.");
        fixture.SourceFile = sourceName;
        return fixture;
    }

    /// <summary>Reads any JSON document under shared/rules-fixtures/, e.g. the PRNG vectors.</summary>
    public static JsonDocument LoadDocument(params string[] relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine([RepoRoot, "shared", "rules-fixtures", .. relativePath])));
}
