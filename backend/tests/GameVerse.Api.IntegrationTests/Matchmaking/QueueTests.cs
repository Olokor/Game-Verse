using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameVerse.Api.IntegrationTests.Accounts;
using GameVerse.Api.IntegrationTests.Realtime;
using GameVerse.Contracts.Accounts;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Infrastructure.Persistence;
using GameVerse.Platform.Matchmaking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace GameVerse.Api.IntegrationTests.Matchmaking;

/// <summary>
/// Public matchmaking with simulated players (Prompt 1.5, ADR-0010). The matcher's background loop is parked in test
/// hosts; these tests drive single ticks on a fake clock. All matchmaking tests live in this one class, so they run one
/// at a time against the shared Redis, and each starts from empty queues.
/// </summary>
public class QueueTests(StackFixture stack)
{
    // Simulated players all share the test's IP address.
    private static readonly Dictionary<string, string?> ManyGuests = new()
    {
        ["GameVerse:Auth:RateLimits:CreatePerMinute"] = "1000",
        ["GameVerse:Matchmaking:JoinsPerMinute"] = "1000",
    };

    [Fact]
    public async Task Two_close_ranked_players_are_matched_at_once_into_a_ranked_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(ct);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);

        var joined = await JoinAsync(api, ada, "ranked", 2, ct);
        joined.Status.ShouldBe(QueueStatuses.Searching);
        joined.RatingBand.ShouldBe(100);
        await JoinAsync(api, bayo, "ranked", 2, ct);

        (await TickAsync(api)).ShouldBe(1);

        var adaStatus = await StatusAsync(api, ada, ct);
        adaStatus.Status.ShouldBe(QueueStatuses.Matched);
        (await StatusAsync(api, bayo, ct)).SessionId.ShouldBe(adaStatus.SessionId);
        var manifest = await ManifestAsync(api, ada, adaStatus.SessionId!, ct);
        manifest.Mode.ShouldBe(GameModes.OnlineRanked);
        manifest.Players.Select(p => p.Id).ShouldBe([ada.UserId, bayo.UserId], ignoreOrder: true);
        manifest.Players.ShouldAllBe(p => !p.IsAI, "ranked never uses AI");
        manifest.Realtime.ShouldNotBeNull();
    }

    [Fact]
    public async Task Far_apart_ratings_wait_until_the_bands_have_widened_enough()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(ct);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        await SetRatingAsync(api, ada, 1500, ct);
        await SetRatingAsync(api, bayo, 2000, ct);
        await JoinAsync(api, ada, "ranked", 2, ct);
        await JoinAsync(api, bayo, "ranked", 2, ct);

        (await TickAsync(api)).ShouldBe(0);
        api.Time.Advance(TimeSpan.FromSeconds(30)); // band 400: 500 apart is still too far
        (await TickAsync(api)).ShouldBe(0);
        (await StatusAsync(api, ada, ct)).RatingBand.ShouldBe(400);

        api.Time.Advance(TimeSpan.FromSeconds(10)); // band 500
        (await TickAsync(api)).ShouldBe(1);
        (await StatusAsync(api, bayo, ct)).Status.ShouldBe(QueueStatuses.Matched);
    }

    [Fact]
    public async Task Four_player_ranked_takes_four_and_the_fifth_keeps_waiting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(ct);
        var players = new List<GuestCreatedResponse>();
        for (var i = 0; i < 5; i++)
        {
            players.Add(await api.CreateGuestAsync(ct));
            await JoinAsync(api, players[^1], "ranked", 4, ct);
            api.Time.Advance(TimeSpan.FromMilliseconds(10)); // distinct join times: the first four are the oldest
        }

        (await TickAsync(api)).ShouldBe(1);

        var statuses = new List<QueueStatusDto>();
        foreach (var player in players)
        {
            statuses.Add(await StatusAsync(api, player, ct));
        }

        statuses.Take(4).ShouldAllBe(s => s.Status == QueueStatuses.Matched && s.SessionId == statuses[0].SessionId);
        statuses[4].Status.ShouldBe(QueueStatuses.Searching);
        (await ManifestAsync(api, players[0], statuses[0].SessionId!, ct)).Players.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Casual_fills_with_AI_after_the_wait_and_ranked_never_does()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(ct);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var chidi = await api.CreateGuestAsync(ct);
        var dayo = await api.CreateGuestAsync(ct);
        await JoinAsync(api, ada, "quick-match", 2, ct);
        await JoinAsync(api, bayo, "quick-match", 4, ct);
        await JoinAsync(api, chidi, "quick-match", 4, ct);
        await JoinAsync(api, dayo, "ranked", 2, ct);

        (await TickAsync(api)).ShouldBe(0);
        (await StatusAsync(api, ada, ct)).RatingBand.ShouldBeNull("casual ignores ratings");
        api.Time.Advance(TimeSpan.FromSeconds(19));
        (await TickAsync(api)).ShouldBe(0);
        api.Time.Advance(TimeSpan.FromSeconds(2));
        (await TickAsync(api)).ShouldBe(2);

        var duel = await ManifestAsync(api, ada, (await StatusAsync(api, ada, ct)).SessionId!, ct);
        duel.Mode.ShouldBe(GameModes.OnlineCasual);
        duel.Players.Count(p => p.IsAI).ShouldBe(1);
        var four = await ManifestAsync(api, bayo, (await StatusAsync(api, bayo, ct)).SessionId!, ct);
        four.Players.Count(p => p.IsAI).ShouldBe(2);
        four.Players.Where(p => !p.IsAI).Select(p => p.Id).ShouldBe([bayo.UserId, chidi.UserId], ignoreOrder: true);

        api.Time.Advance(TimeSpan.FromMinutes(5));
        (await TickAsync(api)).ShouldBe(0);
        (await StatusAsync(api, dayo, ct)).Status.ShouldBe(QueueStatuses.Searching, "a lone ranked player waits for a person");
    }

    [Fact]
    public async Task Joining_is_checked_and_a_search_can_be_cancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(ct);
        var ada = await api.CreateGuestAsync(ct);

        (await JoinRawAsync(api, ada, new JoinQueueRequest { GameId = "ludo", ModeKey = "private-room", PlayerCount = 2 }, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JoinRawAsync(api, ada, new JoinQueueRequest { GameId = "ludo", ModeKey = "vs-ai", PlayerCount = 2 }, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JoinRawAsync(api, ada, new JoinQueueRequest { GameId = "ludo", ModeKey = "ranked", PlayerCount = 3 }, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JoinRawAsync(api, ada, new JoinQueueRequest { GameId = "chess", ModeKey = "blitz-3-2", PlayerCount = 2 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // coming soon
        (await JoinRawAsync(api, ada, new JoinQueueRequest { GameId = "nope", ModeKey = "ranked", PlayerCount = 2 }, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await JoinAsync(api, ada, "ranked", 2, ct);
        (await JoinRawAsync(api, ada, new JoinQueueRequest { GameId = "ludo", ModeKey = "quick-match", PlayerCount = 2 }, ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using (var cancel = await SendAsync(api, ada, HttpMethod.Delete, "/matchmaking/queue", null, ct))
        {
            cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var status = await SendAsync(api, ada, HttpMethod.Get, "/matchmaking/queue", null, ct))
        {
            status.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        await JoinAsync(api, ada, "quick-match", 2, ct); // free to search again
    }

    [Fact]
    public async Task Forty_simulated_players_all_get_games_within_both_players_bands()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(ct);
        var random = new Random(20261112);
        var players = new List<(GuestCreatedResponse Player, int Rating)>();
        for (var i = 0; i < 40; i++)
        {
            var player = await api.CreateGuestAsync(ct);
            var rating = (int)Math.Round(1500 + (random.NextDouble() * 2 - 1) * 400);
            await SetRatingAsync(api, player, rating, ct);
            players.Add((player, rating));
        }

        // Two or three players arrive every simulated second; the matcher ticks every second.
        var options = new MatchmakingOptions();
        var sessions = new Dictionary<string, List<(int Rating, int Waited)>>();
        var queued = 0;
        var matched = new HashSet<string>();
        for (var second = 0; second < 180 && matched.Count < players.Count; second++)
        {
            for (var arrivals = random.Next(2, 4); arrivals > 0 && queued < players.Count; arrivals--, queued++)
            {
                await JoinAsync(api, players[queued].Player, "ranked", 2, ct);
            }

            await TickAsync(api);
            foreach (var (player, rating) in players.Take(queued).Where(p => !matched.Contains(p.Player.UserId)))
            {
                var status = await StatusAsync(api, player, ct);
                if (status.Status == QueueStatuses.Matched)
                {
                    matched.Add(player.UserId);
                    sessions.TryAdd(status.SessionId!, []);
                    sessions[status.SessionId!].Add((rating, status.WaitedSeconds));
                }
            }

            api.Time.Advance(TimeSpan.FromSeconds(1));
        }

        matched.Count.ShouldBe(40, "everyone gets a game");
        sessions.Count.ShouldBe(20);
        foreach (var pair in sessions.Values)
        {
            pair.Count.ShouldBe(2, "two players per game, nobody in two games");
            var gap = Math.Abs(pair[0].Rating - pair[1].Rating);
            foreach (var (_, waited) in pair)
            {
                if (options.Band(TimeSpan.FromSeconds(waited)) is { } band)
                {
                    gap.ShouldBeLessThanOrEqualTo(band + options.BandStep, $"gap {gap} after waiting {waited}s"); // whole seconds round down
                }
            }
        }
    }

    [Fact]
    public async Task Two_instances_ticking_together_never_put_a_player_in_two_games()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var apiA = await StartAsync(ct);
        await using var apiB = RealtimeApi.Start(stack, ManyGuests);
        var players = new List<GuestCreatedResponse>();
        for (var i = 0; i < 20; i++)
        {
            players.Add(await apiA.CreateGuestAsync(ct));
            await JoinAsync(apiA, players[^1], "ranked", 2, ct);
        }

        var formed = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => TickAsync(i % 2 == 0 ? apiA : apiB)));

        formed.Sum().ShouldBe(10);
        await using var scope = apiA.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
        var ids = players.Select(p => (Guid?)Guid.Parse(p.UserId)).ToList();
        var seatsPerPlayer = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM sessions.session_players WHERE user_id = ANY({0}) GROUP BY user_id", ids.ToArray()).ToListAsync(ct);
        seatsPerPlayer.Count.ShouldBe(20);
        seatsPerPlayer.ShouldAllBe(seats => seats == 1);
    }

    [Fact]
    public async Task Ranked_results_move_ratings_and_casual_results_do_not()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(ct);

        // Ranked 2p: Bayo resigns.
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var duel = await MatchAsync(api, "ranked", [ada, bayo], ct);
        await ResignAsync(api, bayo, duel, ct);
        var adaRating = await RatingAsync(api, ada, ct);
        var bayoRating = await RatingAsync(api, bayo, ct);
        adaRating.Rating.ShouldBeGreaterThan(1500);
        bayoRating.Rating.ShouldBeLessThan(1500);
        adaRating.GamesPlayed.ShouldBe(1);
        adaRating.Deviation.ShouldBeLessThan(350);
        adaRating.Provisional.ShouldBeTrue();

        // Casual: no change.
        var chidi = await api.CreateGuestAsync(ct);
        var dayo = await api.CreateGuestAsync(ct);
        var casual = await MatchAsync(api, "quick-match", [chidi, dayo], ct);
        await ResignAsync(api, dayo, casual, ct);
        (await RatingAsync(api, chidi, ct)).GamesPlayed.ShouldBe(0);
        (await RatingAsync(api, dayo, ct)).Rating.ShouldBe(1500);

        // Ranked 4p: three resign in turn, so they finish 4th, 3rd and 2nd (a later leaver places higher).
        var four = new List<GuestCreatedResponse>();
        for (var i = 0; i < 4; i++)
        {
            four.Add(await api.CreateGuestAsync(ct));
        }

        var game = await MatchAsync(api, "ranked", four, ct);
        await ResignAsync(api, four[0], game, ct, expectEnd: false);
        await ResignAsync(api, four[1], game, ct, expectEnd: false);
        await ResignAsync(api, four[2], game, ct);
        var expected = Glicko2.RateFinishingOrder([GlickoRating.Default, GlickoRating.Default, GlickoRating.Default, GlickoRating.Default], [4, 3, 2, 1]);
        for (var i = 0; i < 4; i++)
        {
            (await RatingAsync(api, four[i], ct)).Rating.ShouldBe((int)Math.Round(expected[i].Rating), $"player {i}");
        }
    }

    [Fact]
    public async Task Two_players_queue_for_casual_get_matched_by_the_running_matcher_and_play_to_the_end()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(ct, new() { ["GameVerse:Matchmaking:TickMilliseconds"] = "200" });
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        await JoinAsync(api, ada, "quick-match", 2, ct);
        await JoinAsync(api, bayo, "quick-match", 2, ct);

        var adaMatch = await PollMatchedAsync(api, ada, ct);
        var bayoMatch = await PollMatchedAsync(api, bayo, ct);
        bayoMatch.SessionId.ShouldBe(adaMatch.SessionId);

        var adaManifest = await ManifestAsync(api, ada, adaMatch.SessionId!, ct);
        var bayoManifest = await ManifestAsync(api, bayo, bayoMatch.SessionId!, ct);
        var adaSeat = adaManifest.Players.Single(p => p.Id == ada.UserId).Seat;
        await using var adaClient = await PlayClient.ConnectAsync(api.Factory, adaManifest.Realtime!.WsUrl, adaManifest.Realtime.Ticket, ct);
        await using var bayoClient = await PlayClient.ConnectAsync(api.Factory, bayoManifest.Realtime!.WsUrl, bayoManifest.Realtime.Ticket, ct);
        var results = await Task.WhenAll(adaClient.PlayAsync(adaSeat, ct), bayoClient.PlayAsync(1 - adaSeat, ct));

        results[0].ShouldNotBeNull().Reason.ShouldBe("all_tokens_home");
        results[1].ShouldNotBeNull().Placements.ShouldBe(results[0]!.Placements);
    }

    private async Task<AccountsApi> StartAsync(CancellationToken ct, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>(ManyGuests);
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        var api = RealtimeApi.Start(stack, settings);

        // Empty queues for every test (they share one Redis).
        var redis = api.Factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        foreach (var key in server.Keys(pattern: "mm:*"))
        {
            await redis.GetDatabase().KeyDeleteAsync(key);
        }

        ct.ThrowIfCancellationRequested();
        return api;
    }

    private static Task<int> TickAsync(AccountsApi api) => api.Factory.Services.GetRequiredService<MatchmakerService>().RunOnceAsync();

    private static async Task<Guid> MatchAsync(AccountsApi api, string mode, List<GuestCreatedResponse> players, CancellationToken ct)
    {
        foreach (var player in players)
        {
            await JoinAsync(api, player, mode, players.Count, ct);
        }

        (await TickAsync(api)).ShouldBe(1);
        return Guid.Parse((await StatusAsync(api, players[0], ct)).SessionId!);
    }

    private static async Task ResignAsync(AccountsApi api, GuestCreatedResponse player, Guid sessionId, CancellationToken ct, bool expectEnd = true)
    {
        var manifest = await ManifestAsync(api, player, sessionId.ToString(), ct);
        await using var client = await PlayClient.ConnectAsync(api.Factory, manifest.Realtime!.WsUrl, manifest.Realtime.Ticket, ct);
        await client.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        await client.SendActionAsync(new Dictionary<string, bool> { [LudoActions.Forfeit] = true }, ct);
        if (expectEnd)
        {
            await client.WaitForAsync(e => e.T == MessageTypes.Ended, ct);
            await WaitForRatingsAsync(api, sessionId, ct);
        }
        else
        {
            await client.WaitForAsync(e => e.T == MessageTypes.State, ct);
        }
    }

    /// <summary>The result (and so the rating update) is written by the match host just before it sends "ended".</summary>
    private static async Task WaitForRatingsAsync(AccountsApi api, Guid sessionId, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            await using var scope = api.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
            var status = await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM sessions.game_sessions WHERE id = {sessionId}").SingleAsync(ct);
            if (status == SessionStatuses.Finished)
            {
                return;
            }

            await Task.Delay(50, ct);
        }
    }

    private static async Task SetRatingAsync(AccountsApi api, GuestCreatedResponse player, double rating, CancellationToken ct)
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
        db.Set<PlayerRating>().Add(new PlayerRating
        {
            UserId = Guid.Parse(player.UserId),
            GameId = "ludo",
            Rating = rating,
            Deviation = 60,
            Volatility = 0.06,
            GamesPlayed = 30,
            LastPlayedAt = api.Time.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<QueueStatusDto> PollMatchedAsync(AccountsApi api, GuestCreatedResponse player, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            var status = await StatusAsync(api, player, ct);
            if (status.Status == QueueStatuses.Matched)
            {
                return status;
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException("Never matched.");
    }

    private static async Task<QueueStatusDto> JoinAsync(AccountsApi api, GuestCreatedResponse player, string mode, int players, CancellationToken ct)
    {
        using var response = await JoinRawAsync(api, player, new JoinQueueRequest { GameId = "ludo", ModeKey = mode, PlayerCount = players }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<QueueStatusDto>(ct))!;
    }

    private static Task<HttpResponseMessage> JoinRawAsync(AccountsApi api, GuestCreatedResponse player, JoinQueueRequest body, CancellationToken ct) =>
        SendAsync(api, player, HttpMethod.Post, "/matchmaking/queue", body, ct);

    private static async Task<QueueStatusDto> StatusAsync(AccountsApi api, GuestCreatedResponse player, CancellationToken ct)
    {
        using var response = await SendAsync(api, player, HttpMethod.Get, "/matchmaking/queue", null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<QueueStatusDto>(ct))!;
    }

    private static async Task<RatingDto> RatingAsync(AccountsApi api, GuestCreatedResponse player, CancellationToken ct)
    {
        using var response = await SendAsync(api, player, HttpMethod.Get, "/matchmaking/ratings/ludo", null, ct);
        return (await response.Content.ReadFromJsonAsync<RatingDto>(ct))!;
    }

    private static async Task<SessionManifest<SessionConfig<Dictionary<string, string>>, LudoStateDto>> ManifestAsync(
        AccountsApi api, GuestCreatedResponse player, string sessionId, CancellationToken ct)
    {
        using var response = await SendAsync(api, player, HttpMethod.Get, $"/sessions/{sessionId}/manifest", null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SessionManifest<SessionConfig<Dictionary<string, string>>, LudoStateDto>>(ct))!;
    }

    private static async Task<HttpResponseMessage> SendAsync(AccountsApi api, GuestCreatedResponse player, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", player.AccessToken);
        return await api.Client.SendAsync(request, ct);
    }
}
