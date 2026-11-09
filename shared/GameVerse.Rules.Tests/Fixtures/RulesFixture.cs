using System.Text.Json;

namespace GameVerse.Rules.Tests.Fixtures;

/// <summary>
/// One golden fixture file from shared/rules-fixtures/&lt;game&gt;/*.json.
/// Format is documented in shared/rules-fixtures/README.md.
/// </summary>
public sealed class RulesFixture
{
    public string Game { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public JsonElement? Config { get; set; }

    public List<FixtureSeat> Seats { get; set; } = [];

    public ulong Seed { get; set; }

    public JsonElement? InitialState { get; set; }

    public List<FixtureStep> Steps { get; set; } = [];

    public FixtureOutcome? Outcome { get; set; }

    /// <summary>Set by the loader; used in failure messages.</summary>
    public string SourceFile { get; set; } = string.Empty;
}

public sealed class FixtureSeat
{
    public int Seat { get; set; }

    public string PlayerId { get; set; } = string.Empty;

    public bool IsAI { get; set; }
}

public sealed class FixtureStep
{
    public int Seat { get; set; }

    public JsonElement Action { get; set; }

    public FixtureExpect Expect { get; set; } = new();
}

public sealed class FixtureExpect
{
    /// <summary>Whether the engine must accept the action.</summary>
    public bool Ok { get; set; } = true;

    /// <summary>Required RuleError code when <see cref="Ok"/> is false.</summary>
    public string? Error { get; set; }

    /// <summary>Exact legal actions for the step's seat, checked BEFORE the action is applied.</summary>
    public JsonElement? LegalActions { get; set; }

    /// <summary>Partial state checked AFTER the action: every listed field must match, unlisted fields are ignored.</summary>
    public JsonElement? State { get; set; }
}

public sealed class FixtureOutcome
{
    /// <summary>"win", "draw", "abandoned", or "none" when the game must still be running.</summary>
    public string Kind { get; set; } = string.Empty;

    public List<int>? WinningSeats { get; set; }

    public string? Reason { get; set; }
}
