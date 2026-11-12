using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameVerse.Api.IntegrationTests.Accounts;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Session;
using GameVerse.Infrastructure.Persistence;
using GameVerse.Platform.Sessions;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;
using GameVerse.Rules.Random;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVerse.Api.IntegrationTests.Sessions;

/// <summary>Ludo sessions: house rules, the manifest's initial state and dice commit-reveal (Prompt 1.3, ADR-0008).</summary>
public class LudoSessionTests(StackFixture stack)
{
    [Fact]
    public async Task House_rules_shape_the_initial_state()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);
        var rules = new Dictionary<string, string> { [LudoHouseRules.QuickMode] = "true", [LudoHouseRules.MustCapture] = "true", [LudoHouseRules.Placings] = "3" };

        using var created = await PostAsync(api, guest.AccessToken, new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 3, HouseRules = rules }, ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        var state = (await created.Content.ReadFromJsonAsync<SessionManifest<SessionConfig<Dictionary<string, string>>, LudoStateDto>>(ct)).ShouldNotBeNull().InitialState!;
        state.Rules.QuickMode.ShouldBeTrue();
        state.Rules.MustCapture.ShouldBeTrue();
        state.Rules.Placings.ShouldBe(2, "placings 3 with 3 players is capped at players minus 1: the manifest shows what the engine plays to");
        state.Left.ShouldBeEmpty();
        state.Rules.SafeSquares.ShouldBeTrue("unlisted rules keep the Classic Naija defaults");
        state.Tokens.ShouldAllBe(seat => seat.SequenceEqual(new[] { 0, -1, -1, -1 }));
    }

    [Theory]
    [InlineData("blocks", "true", 2)] // not a rule we have
    [InlineData("safeSquares", "yes", 2)]
    [InlineData("safeSquares", "TRUE", 2)] // exactly "true" or "false"
    [InlineData("placings", "4", 4)]
    [InlineData("placings", "-1", 4)]
    [InlineData("teams", "true", 2)] // teams need 4 players
    public async Task Bad_house_rules_are_refused_with_the_reason(string key, string value, int players)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var response = await PostAsync(api, guest.AccessToken,
            new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = players, HouseRules = new() { [key] = value } }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        problem.RootElement.GetProperty("errors").GetProperty("HouseRules")[0].GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Team_ludo_with_four_players_is_accepted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var created = await PostAsync(api, guest.AccessToken,
            new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 4, HouseRules = new() { [LudoHouseRules.Teams] = "true" } }, ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.Content.ReadFromJsonAsync<SessionManifest<SessionConfig<Dictionary<string, string>>, LudoStateDto>>(ct))!.InitialState!.Rules.Teams.ShouldBeTrue();
    }

    [Fact]
    public async Task The_seed_is_committed_at_creation_and_revealed_only_after_the_end()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var owner = await api.CreateGuestAsync(ct);
        var stranger = await api.CreateGuestAsync(ct);
        using var created = await PostAsync(api, owner.AccessToken, new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 2 }, ct);
        var sessionId = (await created.Content.ReadFromJsonAsync<SessionManifest>(ct)).ShouldNotBeNull().SessionId;
        var path = $"/sessions/{sessionId}/fairness";

        // During the game: the commitment only.
        var during = await GetFairnessAsync(api, path, owner.AccessToken, ct);
        during.Commitment.ShouldNotBeNull().ShouldMatch("^[0-9a-f]{64}$");
        during.Revealed.ShouldBeFalse();
        during.Secret.ShouldBeNull();
        during.Seed.ShouldBeNull();
        using (var theirs = await GetAsync(api, path, stranger.AccessToken, ct))
        {
            theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound, "non-participants get the same answer as a missing session");
        }

        // The server's engine plays with the seed behind that commitment.
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var serverSeed = (await sessions.GetSeedAsync(Guid.Parse(sessionId), ct)).ShouldNotBeNull();
        var serverRolls = FirstRolls(serverSeed);
        var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
        (await db.Database.SqlQuery<int>($"SELECT length(seed_secret) AS \"Value\" FROM sessions.game_sessions WHERE id = {Guid.Parse(sessionId)}").SingleAsync(ct)).ShouldBe(32);

        (await sessions.RecordResultAsync(Guid.Parse(sessionId), new MatchOutcome("win", [0], "all_tokens_home", new Dictionary<int, int> { [0] = 1, [1] = 2 }), ct)).ShouldBeTrue();

        // After the end: anyone seated can check the secret against the commitment and replay the dice.
        var after = await GetFairnessAsync(api, path, owner.AccessToken, ct);
        after.Revealed.ShouldBeTrue();
        after.Commitment.ShouldBe(during.Commitment);
        var secret = Convert.FromHexString(after.Secret.ShouldNotBeNull());
        SeedCommitment.Verify(after.Commitment!, secret).ShouldBeTrue();
        ulong.Parse(after.Seed.ShouldNotBeNull(), System.Globalization.CultureInfo.InvariantCulture).ShouldBe(SeedCommitment.DeriveSeed(secret));
        FirstRolls(SeedCommitment.DeriveSeed(secret)).ShouldBe(serverRolls);
    }

    [Fact]
    public async Task Every_session_gets_its_own_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);
        var commitments = new HashSet<string>();
        for (var i = 0; i < 3; i++)
        {
            using var created = await PostAsync(api, guest.AccessToken, new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 2 }, ct);
            var id = (await created.Content.ReadFromJsonAsync<SessionManifest>(ct))!.SessionId;
            commitments.Add((await GetFairnessAsync(api, $"/sessions/{id}/fairness", guest.AccessToken, ct)).Commitment!);
        }

        commitments.Count.ShouldBe(3);
    }

    /// <summary>The first ten dice of a 2-player default game with this seed, rolling only (no moves needed for the PRNG).</summary>
    private static List<int> FirstRolls(ulong seed)
    {
        var engine = new LudoEngine();
        var state = engine.NewGame(LudoConfig.Default, [new SeatInfo(0, "a", false), new SeatInfo(1, "b", true)], seed);
        var rolls = new List<int>();
        while (rolls.Count < 10)
        {
            var action = engine.LegalActions(state, state.CurrentSeat)[0];
            state = engine.Apply(state, state.CurrentSeat, action).State;
            if (action.IsRoll)
            {
                rolls.Add(state.LastRoll);
            }
        }

        return rolls;
    }

    private static async Task<SessionFairness> GetFairnessAsync(AccountsApi api, string path, string accessToken, CancellationToken ct)
    {
        using var response = await GetAsync(api, path, accessToken, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SessionFairness>(ct)).ShouldNotBeNull();
    }

    private static async Task<HttpResponseMessage> PostAsync(AccountsApi api, string? accessToken, CreateSessionRequest body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/sessions", UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await api.Client.SendAsync(request, ct);
    }

    private static async Task<HttpResponseMessage> GetAsync(AccountsApi api, string path, string? accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await api.Client.SendAsync(request, ct);
    }
}
