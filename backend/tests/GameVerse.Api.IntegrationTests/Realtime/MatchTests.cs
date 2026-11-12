using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameVerse.Api.IntegrationTests.Accounts;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Platform.Realtime;
using GameVerse.Platform.Sessions;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace GameVerse.Api.IntegrationTests.Realtime;

/// <summary>Live matches over /ws/play, played by the server's match host (Prompt 1.4, ADR-0009).</summary>
public class MatchTests(StackFixture stack)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_vs_AI_game_is_played_by_the_server_to_the_end_over_the_socket()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var player = await api.CreateGuestAsync(ct);

        using var created = await PostSessionAsync(api, player.AccessToken, new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 2, AiLevel = 2 }, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var manifest = (await created.Content.ReadFromJsonAsync<SessionManifest<SessionConfig<Dictionary<string, string>>, LudoStateDto>>(ct)).ShouldNotBeNull();
        var realtime = manifest.Realtime.ShouldNotBeNull("vs AI is hosted by the server (ADR-0008), so the manifest carries a socket ticket");
        realtime.WsUrl.ShouldBe($"ws://localhost/ws/play/{manifest.SessionId}");

        await using var client = await PlayClient.ConnectAsync(api.Factory, realtime.WsUrl, realtime.Ticket, ct);
        var snapshot = await client.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        snapshot.StateVersion.ShouldBe(0);
        client.TurnSeat.ShouldBe(0);
        client.TurnDeadline.ShouldBeNull("vs AI has no turn clock");

        var ended = (await client.PlayAsync(seat: 0, ct)).ShouldNotBeNull();

        ended.Reason.ShouldBe("all_tokens_home");
        ended.Placements.Count.ShouldBe(2);
        client.Received.ShouldNotContain(e => e.Payload.ToString().Contains("rng", StringComparison.OrdinalIgnoreCase), "no client ever sees the dice PRNG");
        await AssertReplayMatchesAsync(api, player.AccessToken, Guid.Parse(manifest.SessionId), ended, client.StateVersion, ct);
    }

    [Fact]
    public async Task Two_players_on_two_API_instances_play_a_full_online_game()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var apiA = RealtimeApi.Start(stack);
        await using var apiB = RealtimeApi.Start(stack);
        var ada = await apiA.CreateGuestAsync(ct);
        var bayo = await apiA.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(apiA, [ada, bayo], ct);

        // Ada's socket is on instance A, Bayo's on B: every message crosses Redis (pub/sub fan-out, CLAUDE.md 4.5).
        await using var adaClient = await RealtimeApi.ConnectAsync(apiA, ada.AccessToken, sessionId, ct);
        await using var bayoClient = await RealtimeApi.ConnectAsync(apiB, bayo.AccessToken, sessionId, ct);
        await adaClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        await bayoClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        adaClient.TurnDeadline.ShouldNotBeNull("online play has a turn clock");

        var results = await Task.WhenAll(adaClient.PlayAsync(0, ct), bayoClient.PlayAsync(1, ct));

        results[0].ShouldNotBeNull().Reason.ShouldBe("all_tokens_home");
        results[1].ShouldNotBeNull().Placements.ShouldBe(results[0]!.Placements);
        adaClient.StateVersion.ShouldBe(bayoClient.StateVersion);
        await AssertReplayMatchesAsync(apiA, ada.AccessToken, sessionId, results[0]!, adaClient.StateVersion, ct);
    }

    [Fact]
    public async Task A_killed_match_host_resumes_from_Redis_on_another_instance()
    {
        var ct = TestContext.Current.CancellationToken;
        var shortLease = new Dictionary<string, string?> { ["GameVerse:Realtime:LeaseSeconds"] = "2" };
        var apiA = RealtimeApi.Start(stack, shortLease);
        await using var apiB = RealtimeApi.Start(stack, shortLease);
        var ada = await apiA.CreateGuestAsync(ct);
        var bayo = await apiA.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(apiA, [ada, bayo], ct);

        // Play on instance A until 20 actions are in.
        long versionBeforeCrash;
        await using (var adaOnA = await RealtimeApi.ConnectAsync(apiA, ada.AccessToken, sessionId, ct))
        await using (var bayoOnA = await RealtimeApi.ConnectAsync(apiA, bayo.AccessToken, sessionId, ct))
        {
            using var untilTwenty = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var play = Task.WhenAll(adaOnA.PlayAsync(0, untilTwenty.Token), bayoOnA.PlayAsync(1, untilTwenty.Token));
            while (Math.Min(adaOnA.StateVersion, bayoOnA.StateVersion) < 20)
            {
                await Task.Delay(20, ct);
            }

            await untilTwenty.CancelAsync();
            await Should.ThrowAsync<Exception>(play);

            // Kill the host the way a crash would: no clean shutdown, the lease is left behind.
            apiA.Factory.Services.GetRequiredService<MatchHostRegistry>().IsHosting(sessionId).ShouldBeTrue();
            await apiA.Factory.Services.GetRequiredService<MatchHostRegistry>().AbortAllAsync();
            versionBeforeCrash = Math.Max(adaOnA.StateVersion, bayoOnA.StateVersion);
        }

        var redis = apiB.Factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        (await redis.KeyExistsAsync($"rt:match:{sessionId}:lease")).ShouldBeTrue("a crashed host's lease stays until it expires");
        (await redis.KeyExistsAsync($"rt:match:{sessionId}:snap")).ShouldBeTrue();
        await apiA.DisposeAsync();

        // Both players reconnect to instance B, which takes the match over once the lease expires.
        await using var adaOnB = await RealtimeApi.ConnectAsync(apiB, ada.AccessToken, sessionId, ct);
        await using var bayoOnB = await RealtimeApi.ConnectAsync(apiB, bayo.AccessToken, sessionId, ct);
        await adaOnB.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        await bayoOnB.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        apiB.Factory.Services.GetRequiredService<MatchHostRegistry>().IsHosting(sessionId).ShouldBeTrue();
        adaOnB.StateVersion.ShouldBeGreaterThanOrEqualTo(versionBeforeCrash, "the resumed match continues where the old host stopped");

        var results = await Task.WhenAll(adaOnB.PlayAsync(0, ct), bayoOnB.PlayAsync(1, ct));

        var ended = results[0].ShouldNotBeNull();
        ended.Reason.ShouldBe("all_tokens_home");
        await AssertReplayMatchesAsync(apiB, ada.AccessToken, sessionId, ended, adaOnB.StateVersion, ct);
    }

    [Fact]
    public async Task Acting_out_of_turn_is_refused_with_the_rule_code_and_the_seq()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);
        await using var bayoClient = await RealtimeApi.ConnectAsync(api, bayo.AccessToken, sessionId, ct);
        await bayoClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);

        var seq = await bayoClient.SendActionAsync(new Dictionary<string, bool> { [LudoActions.Roll] = true }, ct);
        var error = await bayoClient.WaitForAsync(e => e.T == MessageTypes.Error, ct);
        error.Payload.GetProperty("code").GetString().ShouldBe("not_your_turn");
        error.Payload.GetProperty("seq").GetInt64().ShouldBe(seq);

        await bayoClient.SendActionAsync(new Dictionary<string, string> { ["dance"] = "now" }, ct);
        (await bayoClient.WaitForAsync(e => e.T == MessageTypes.Error, ct)).Payload.GetProperty("code").GetString().ShouldBe("bad_action");
    }

    [Fact]
    public async Task A_resent_action_is_applied_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);
        await using var adaClient = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct);
        await adaClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);

        var roll = new Dictionary<string, bool> { [LudoActions.Roll] = true };
        await adaClient.SendAsync(MessageTypes.Action, 1, roll, ct);
        await adaClient.WaitForAsync(e => e.T == MessageTypes.State, ct);
        await adaClient.SendAsync(MessageTypes.Action, 1, roll, ct); // the same seq again, as after a reconnect

        var answer = await adaClient.WaitForAsync(e => e.T is MessageTypes.State or MessageTypes.Snapshot or MessageTypes.Error, ct);
        answer.T.ShouldBe(MessageTypes.Snapshot, "a duplicate is answered with where the game is, not applied again");
        answer.StateVersion.ShouldBe(1);
    }

    private static async Task AssertReplayMatchesAsync(AccountsApi api, string accessToken, Guid sessionId, MatchEndedEvent ended, long finalVersion, CancellationToken ct)
    {
        // The seed is revealed after the end; replaying the logged moves with it must give the same game (ADR-0008).
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/sessions/{sessionId}/fairness", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await api.Client.SendAsync(request, ct);
        var fairness = (await response.Content.ReadFromJsonAsync<SessionFairness>(ct)).ShouldNotBeNull();
        fairness.Revealed.ShouldBeTrue("the result is recorded, so the seed is public");
        var seed = ulong.Parse(fairness.Seed!, CultureInfo.InvariantCulture);

        await using var scope = api.Factory.Services.CreateAsyncScope();
        var moves = await scope.ServiceProvider.GetRequiredService<ISessionService>().ListMovesAsync(sessionId, 0, ct);
        moves.Count.ShouldBe((int)finalVersion);
        moves.Select(m => m.Seq).ShouldBe(Enumerable.Range(1, moves.Count));

        var engine = new LudoEngine();
        var state = engine.NewGame(LudoConfig.Default, [new SeatInfo(0, "a", false), new SeatInfo(1, "b", false)], seed);
        foreach (var move in moves)
        {
            var action = move.Action.TryGetProperty("move", out var token) ? LudoAction.Move(token.GetInt32())
                : move.Action.TryGetProperty("forfeit", out _) ? LudoAction.Forfeit
                : LudoAction.Roll;
            var applied = engine.Apply(state, move.Seat, action);
            applied.IsOk.ShouldBeTrue($"move {move.Seq} replays");
            state = applied.State;
        }

        var outcome = engine.Outcome(state).ShouldNotBeNull();
        outcome.Placements.ShouldBe(ended.Placements);
        outcome.Reason.ShouldBe(ended.Reason);
    }

    private static async Task<HttpResponseMessage> PostSessionAsync(AccountsApi api, string accessToken, CreateSessionRequest body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/sessions", UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await api.Client.SendAsync(request, ct);
    }
}
