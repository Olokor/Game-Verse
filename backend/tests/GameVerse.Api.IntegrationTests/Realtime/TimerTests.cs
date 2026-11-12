using System.Net.WebSockets;
using System.Text.Json;
using GameVerse.Api.IntegrationTests.Accounts;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Realtime;
using GameVerse.Platform.Realtime;
using GameVerse.Platform.Sessions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace GameVerse.Api.IntegrationTests.Realtime;

/// <summary>
/// Server clocks (CLAUDE.md 4.5): turn timeouts, disconnect grace, heartbeat, and the Hangfire safety net. The game clock
/// is the test's FakeTimeProvider, so a minute passes in milliseconds.
/// </summary>
public class TimerTests(StackFixture stack)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>These tests skip pongs, so the heartbeat must not close their sockets as time jumps ahead.</summary>
    private static readonly Dictionary<string, string?> PatientHeartbeat = new() { ["GameVerse:Realtime:PongTimeoutSeconds"] = "3600" };

    [Fact]
    public async Task When_the_turn_clock_runs_out_the_server_plays_for_the_seat()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack, PatientHeartbeat);
        var (ada, bayo, sessionId) = await StartOnlineAsync(api, ct);
        await using var adaClient = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct);
        await using var bayoClient = await RealtimeApi.ConnectAsync(api, bayo.AccessToken, sessionId, ct);
        await adaClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        await bayoClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        var deadline = adaClient.TurnDeadline.ShouldNotBeNull();
        Math.Abs(deadline - api.Time.GetUtcNow().AddSeconds(15).ToUnixTimeMilliseconds()).ShouldBeLessThanOrEqualTo(1000);

        await AdvanceAsync(api, TimeSpan.FromSeconds(16));

        var auto = await bayoClient.WaitForAsync(e => e.T == MessageTypes.State, ct);
        auto.StateVersion.ShouldBe(1);
        auto.Payload.GetProperty("appliedSeq").ValueKind.ShouldBe(JsonValueKind.Null, "nobody sent it: the server rolled for seat 0");
        (await MovesAsync(api, sessionId, ct)).ShouldHaveSingleItem().Seat.ShouldBe(0);
    }

    [Fact]
    public async Task A_player_who_stays_away_past_the_grace_window_forfeits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack, PatientHeartbeat);
        var (ada, bayo, sessionId) = await StartOnlineAsync(api, ct);
        await using var adaClient = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct);
        await adaClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        await using (var bayoClient = await RealtimeApi.ConnectAsync(api, bayo.AccessToken, sessionId, ct))
        {
            await bayoClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
            await bayoClient.CloseAsync(ct);
        }

        var away = await adaClient.WaitForAsync(e => e.T == MessageTypes.Presence && !e.Payload.GetProperty("connected").GetBoolean(), ct);
        var graceEnds = away.Payload.GetProperty("graceEndsAt").GetInt64();
        Math.Abs(graceEnds - api.Time.GetUtcNow().AddSeconds(60).ToUnixTimeMilliseconds()).ShouldBeLessThanOrEqualTo(1000, "private rooms are casual: 60 seconds");

        await AdvanceAsync(api, TimeSpan.FromSeconds(61));

        var ended = (await adaClient.WaitForAsync(e => e.T == MessageTypes.Ended, ct)).Payload.Deserialize<MatchEndedEvent>(Json)!;
        ended.Reason.ShouldBe("opponents_left");
        ended.WinningSeats.ShouldBe([0]);
        ended.Placements.ShouldBe([0, 1]);
        (await MovesAsync(api, sessionId, ct)).ShouldContain(m => m.Seat == 1 && IsForfeit(m));
    }

    [Fact]
    public async Task Reconnecting_within_the_grace_window_restores_the_seat_with_a_snapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack, PatientHeartbeat);
        var (ada, bayo, sessionId) = await StartOnlineAsync(api, ct);
        await using var adaClient = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct);
        await adaClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        await using (var first = await RealtimeApi.ConnectAsync(api, bayo.AccessToken, sessionId, ct))
        {
            await first.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
            await first.CloseAsync(ct);
        }

        await adaClient.WaitForAsync(e => e.T == MessageTypes.Presence && !e.Payload.GetProperty("connected").GetBoolean(), ct);
        await AdvanceAsync(api, TimeSpan.FromSeconds(30));

        await using var back = await RealtimeApi.ConnectAsync(api, bayo.AccessToken, sessionId, ct);
        var snapshot = await back.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        snapshot.StateVersion.ShouldBe((await MovesAsync(api, sessionId, ct)).Count, "the snapshot is the game as it is now, turn timeouts included");
        await adaClient.WaitForAsync(e => e.T == MessageTypes.Presence && e.Payload.GetProperty("seat").GetInt32() == 1 && e.Payload.GetProperty("connected").GetBoolean(), ct);

        await AdvanceAsync(api, TimeSpan.FromSeconds(45)); // past where the grace window would have ended

        (await MovesAsync(api, sessionId, ct)).ShouldNotContain(m => IsForfeit(m));
        adaClient.Ended.ShouldBeNull();
    }

    [Fact]
    public async Task A_socket_that_never_answers_pings_is_closed_after_45_seconds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var (ada, _, sessionId) = await StartOnlineAsync(api, ct);
        await using var silent = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct);
        await silent.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);

        await AdvanceAsync(api, TimeSpan.FromSeconds(30));
        silent.SocketState.ShouldBe(WebSocketState.Open, "two missed pongs are tolerated");
        await AdvanceAsync(api, TimeSpan.FromSeconds(16));

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        while (await silent.ReceiveAsync(limit.Token) is not null)
        {
        }

        silent.CloseStatus.ShouldBe(WebSocketCloseStatus.PolicyViolation);
        silent.Received.Count(e => e.T == MessageTypes.Ping).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task The_watchdog_resumes_an_overdue_match_nobody_hosts_and_the_absent_player_forfeits()
    {
        var ct = TestContext.Current.CancellationToken;
        var shortLease = new Dictionary<string, string?>(PatientHeartbeat) { ["GameVerse:Realtime:LeaseSeconds"] = "1" };
        var apiA = RealtimeApi.Start(stack, shortLease);
        await using var apiB = RealtimeApi.Start(stack, shortLease);
        var (ada, _, sessionId) = await StartOnlineAsync(apiA, ct);

        // Ada starts the match on A and leaves; Bayo never shows up. Then A crashes.
        await using (var adaClient = await RealtimeApi.ConnectAsync(apiA, ada.AccessToken, sessionId, ct))
        {
            await adaClient.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        }

        await apiA.Factory.Services.GetRequiredService<MatchHostRegistry>().AbortAllAsync();
        await apiA.DisposeAsync();
        var redis = apiB.Factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        (await redis.SortedSetScoreAsync("rt:deadlines", sessionId.ToString())).ShouldNotBeNull("the match is indexed by its next deadline");
        while (await redis.KeyExistsAsync($"rt:match:{sessionId}:lease"))
        {
            await Task.Delay(100, ct);
        }

        apiB.Time.Advance(TimeSpan.FromMinutes(2)); // the deadlines are now overdue
        var started = await RunWatchdogAsync(apiB);

        // Other tests share this Redis, so their orphaned matches may be picked up too: check this one.
        started.ShouldBeGreaterThanOrEqualTo(1);
        apiB.Factory.Services.GetRequiredService<MatchHostRegistry>().IsHosting(sessionId).ShouldBeTrue();
        await WaitUntilAsync(async () => (await MovesAsync(apiB, sessionId, ct)).Any(IsForfeit), ct);
        await WaitUntilAsync(async () => (await redis.SortedSetScoreAsync("rt:deadlines", sessionId.ToString())) is null, ct);
    }

    private static bool IsForfeit(LoggedMove move) => move.Action.TryGetProperty("forfeit", out var flag) && flag.GetBoolean();

    private static Task<int> RunWatchdogAsync(AccountsApi api) => api.Factory.Services.GetRequiredService<MatchWatchdog>().RunAsync();

    private static async Task<(Contracts.Accounts.GuestCreatedResponse Ada, Contracts.Accounts.GuestCreatedResponse Bayo, Guid SessionId)> StartOnlineAsync(AccountsApi api, CancellationToken ct)
    {
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        return (ada, bayo, await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct));
    }

    /// <summary>Moves the fake clock in one-second steps, giving timers a moment to re-arm between steps.</summary>
    private static async Task AdvanceAsync(AccountsApi api, TimeSpan by)
    {
        for (var passed = TimeSpan.Zero; passed < by; passed += TimeSpan.FromSeconds(1))
        {
            api.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(15);
        }
    }

    private static async Task<IReadOnlyList<LoggedMove>> MovesAsync(AccountsApi api, Guid sessionId, CancellationToken ct)
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISessionService>().ListMovesAsync(sessionId, 0, ct);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken ct)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50, ct);
        }

        throw new TimeoutException("The condition never became true.");
    }
}
