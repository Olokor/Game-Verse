using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameVerse.Api.IntegrationTests.Accounts;
using GameVerse.Api.IntegrationTests.Realtime;
using GameVerse.Contracts.Accounts;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;

namespace GameVerse.Api.IntegrationTests.Matchmaking;

/// <summary>Private rooms (CLAUDE.md 4.6, ADR-0009): codes, seats, ready, host rules, series and rematch.</summary>
public class RoomTests(StackFixture stack)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Two_players_create_and_join_a_room_by_code_and_play_a_full_game_through_the_real_backend()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);

        // Ada opens a room and follows its lobby.
        var room = await CreateAsync(api, ada, new CreateRoomRequest { GameId = "ludo", PlayerCount = 2 }, ct);
        room.Code.ShouldMatch("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$");
        room.ShareUrl.ShouldBe("https://gameverse.app/r/" + room.Code);
        room.Status.ShouldBe(RoomStatuses.Lobby);
        room.Seats.Select(s => s.PlayerId).ShouldBe([ada.UserId, null]);
        await using var lobby = await ConnectLobbyAsync(api, ada, room.Code, ct);
        (await lobby.WaitForAsync(e => e.T == MessageTypes.Room, ct)).Payload.Deserialize<RoomDto>(Json)!.Code.ShouldBe(room.Code);

        // Bayo joins with the code as typed (lower case), everyone readies up, and the lobby sees each step.
        (await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code.ToLowerInvariant()}/join", null, ct)).Seats[1].PlayerId.ShouldBe(bayo.UserId);
        await NextRoomAsync(lobby, r => r.Seats[1].PlayerId == bayo.UserId, ct);
        await SendAsync(api, ada, HttpMethod.Post, $"/rooms/{room.Code}/ready", new RoomReadyRequest { Ready = true }, ct);
        await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/ready", new RoomReadyRequest { Ready = true }, ct);
        await NextRoomAsync(lobby, r => r.Seats.All(s => s.Ready), ct);

        using (var notHost = await RawAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/start", null, ct))
        {
            notHost.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var started = await SendAsync(api, ada, HttpMethod.Post, $"/rooms/{room.Code}/start", null, ct);
        started.Status.ShouldBe(RoomStatuses.Playing);
        var sessionId = Guid.Parse(started.SessionId.ShouldNotBeNull());
        (await NextRoomAsync(lobby, r => r.Status == RoomStatuses.Playing, ct)).SessionId.ShouldBe(started.SessionId);

        // Both load the game from its manifest (online_private, with a socket ticket) and play it to the end.
        var adaManifest = await ManifestAsync(api, ada, sessionId, ct);
        var bayoManifest = await ManifestAsync(api, bayo, sessionId, ct);
        adaManifest.Mode.ShouldBe(GameModes.OnlinePrivate);
        await using var adaClient = await PlayClient.ConnectAsync(api.Factory, adaManifest.Realtime!.WsUrl, adaManifest.Realtime.Ticket, ct);
        await using var bayoClient = await PlayClient.ConnectAsync(api.Factory, bayoManifest.Realtime!.WsUrl, bayoManifest.Realtime.Ticket, ct);
        var results = await Task.WhenAll(adaClient.PlayAsync(0, ct), bayoClient.PlayAsync(1, ct));

        var ended = results[0].ShouldNotBeNull();
        ended.Reason.ShouldBe("all_tokens_home");
        var after = await NextRoomAsync(lobby, r => r.Status == RoomStatuses.SeriesOver, ct);
        after.SeriesWinners.ShouldBe(ended.WinningSeats);
        after.Seats[ended.WinningSeats[0]].Wins.ShouldBe(1);
    }

    [Fact]
    public async Task Room_rules_are_enforced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var chidi = await api.CreateGuestAsync(ct);

        using (var teamsOfTwo = await RawAsync(api, ada, HttpMethod.Post, "/rooms", new CreateRoomRequest { GameId = "ludo", PlayerCount = 2, HouseRules = { [LudoHouseRules.Teams] = "true" } }, ct))
        {
            teamsOfTwo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var bestOfTwo = await RawAsync(api, ada, HttpMethod.Post, "/rooms", new CreateRoomRequest { GameId = "ludo", PlayerCount = 2, BestOf = 2 }, ct))
        {
            bestOfTwo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        var room = await CreateAsync(api, ada, new CreateRoomRequest { GameId = "ludo", PlayerCount = 2 }, ct);
        await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/join", null, ct);
        await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/ready", new RoomReadyRequest { Ready = true }, ct);

        await ExpectAsync(api, chidi, HttpMethod.Post, $"/rooms/{room.Code}/join", null, HttpStatusCode.Conflict, ct); // full
        await ExpectAsync(api, ada, HttpMethod.Get, "/rooms/ZZZZZZ", null, HttpStatusCode.NotFound, ct);
        await ExpectAsync(api, ada, HttpMethod.Get, "/rooms/ABC0I1", null, HttpStatusCode.NotFound, ct); // not a possible code
        await ExpectAsync(api, ada, HttpMethod.Post, $"/rooms/{room.Code}/start", null, HttpStatusCode.Conflict, ct); // Ada is not ready
        await ExpectAsync(api, bayo, HttpMethod.Patch, $"/rooms/{room.Code}/rules", new RoomRulesRequest { HouseRules = { [LudoHouseRules.QuickMode] = "true" } }, HttpStatusCode.Forbidden, ct);
        await ExpectAsync(api, ada, HttpMethod.Patch, $"/rooms/{room.Code}/rules", new RoomRulesRequest { HouseRules = { ["blocks"] = "true" } }, HttpStatusCode.BadRequest, ct);
        using (var stranger = await RealtimeApi.RequestTicketAsync(api, chidi.AccessToken, new TicketRequest { RoomCode = room.Code }, ct))
        {
            stranger.StatusCode.ShouldBe(HttpStatusCode.NotFound, "only members follow a lobby");
        }

        var changed = await SendAsync(api, ada, HttpMethod.Patch, $"/rooms/{room.Code}/rules", new RoomRulesRequest { HouseRules = { [LudoHouseRules.QuickMode] = "true" } }, ct);
        changed.HouseRules.ShouldContainKeyAndValue(LudoHouseRules.QuickMode, "true");
        changed.Seats.ShouldAllBe(s => !s.Ready, "new rules: everyone agrees again");
    }

    [Fact]
    public async Task An_idle_lobby_expires_and_its_code_stops_working()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var room = await CreateAsync(api, ada, new CreateRoomRequest { GameId = "ludo", PlayerCount = 2 }, ct);

        api.Time.Advance(TimeSpan.FromMinutes(31));

        await ExpectAsync(api, ada, HttpMethod.Get, $"/rooms/{room.Code}", null, HttpStatusCode.Gone, ct);
        await ExpectAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/join", null, HttpStatusCode.Gone, ct);
    }

    [Fact]
    public async Task The_host_role_passes_on_and_an_empty_room_closes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var room = await CreateAsync(api, ada, new CreateRoomRequest { GameId = "ludo", PlayerCount = 3 }, ct);
        await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/join", null, ct);

        var afterAda = await SendAsync(api, ada, HttpMethod.Post, $"/rooms/{room.Code}/leave", null, ct);
        afterAda.HostSeat.ShouldBe(1);
        afterAda.Seats[0].PlayerId.ShouldBeNull();

        (await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/leave", null, ct)).Status.ShouldBe(RoomStatuses.Closed);
        await ExpectAsync(api, ada, HttpMethod.Get, $"/rooms/{room.Code}", null, HttpStatusCode.Gone, ct);
    }

    [Fact]
    public async Task Best_of_three_goes_to_two_wins_then_a_rematch_starts_a_new_series()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var room = await CreateAsync(api, ada, new CreateRoomRequest { GameId = "ludo", PlayerCount = 2, BestOf = 3 }, ct);
        await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/join", null, ct);
        await ReadyAllAsync(api, room.Code, [ada, bayo], ct);
        var current = await SendAsync(api, ada, HttpMethod.Post, $"/rooms/{room.Code}/start", null, ct);

        // Game 1: Bayo resigns. Game 2: Ada resigns. Game 3: Bayo resigns. Ada takes the series 2-1.
        var resigners = new[] { bayo, ada, bayo };
        var seats = new Dictionary<string, int> { [ada.UserId] = 0, [bayo.UserId] = 1 };
        for (var game = 1; game <= 3; game++)
        {
            current.GameNumber.ShouldBe(game);
            current.Status.ShouldBe(RoomStatuses.Playing);
            await ResignAsync(api, resigners[game - 1], Guid.Parse(current.SessionId!), ct);
            current = await WaitForRoomAsync(api, ada, room.Code, r => r.Status != RoomStatuses.Playing, ct);
            if (game < 3)
            {
                current.Status.ShouldBe(RoomStatuses.BetweenGames);
                current = await ReadyAllAsync(api, room.Code, [ada, bayo], ct); // the last ready starts the next game
            }
        }

        current.Status.ShouldBe(RoomStatuses.SeriesOver);
        current.SeriesWinners.ShouldBe([seats[ada.UserId]]);
        current.Seats.Select(s => s.Wins).ShouldBe([2, 1]);

        var rematch = await ReadyAllAsync(api, room.Code, [ada, bayo], ct);
        rematch.Status.ShouldBe(RoomStatuses.Playing);
        rematch.GameNumber.ShouldBe(1);
        rematch.Seats.Select(s => s.Wins).ShouldBe([0, 0]);
        rematch.SessionId.ShouldNotBe(current.SessionId);
    }

    [Fact]
    public async Task Leaving_between_games_ends_the_series_and_opens_the_seat()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var room = await CreateAsync(api, ada, new CreateRoomRequest { GameId = "ludo", PlayerCount = 2, BestOf = 3 }, ct);
        await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/join", null, ct);
        await ReadyAllAsync(api, room.Code, [ada, bayo], ct);
        var playing = await SendAsync(api, ada, HttpMethod.Post, $"/rooms/{room.Code}/start", null, ct);
        await ResignAsync(api, bayo, Guid.Parse(playing.SessionId!), ct);
        (await WaitForRoomAsync(api, ada, room.Code, r => r.Status == RoomStatuses.BetweenGames, ct)).Seats[0].Wins.ShouldBe(1);

        var left = await SendAsync(api, bayo, HttpMethod.Post, $"/rooms/{room.Code}/leave", null, ct);

        left.Status.ShouldBe(RoomStatuses.Lobby);
        left.Seats[1].PlayerId.ShouldBeNull();
        left.Seats.ShouldAllBe(s => s.Wins == 0);
        left.GameNumber.ShouldBe(0);
    }

    private static async Task ResignAsync(AccountsApi api, GuestCreatedResponse player, Guid sessionId, CancellationToken ct)
    {
        var manifest = await ManifestAsync(api, player, sessionId, ct);
        await using var client = await PlayClient.ConnectAsync(api.Factory, manifest.Realtime!.WsUrl, manifest.Realtime.Ticket, ct);
        await client.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        await client.SendActionAsync(new Dictionary<string, bool> { [LudoActions.Forfeit] = true }, ct);
        (await client.WaitForAsync(e => e.T == MessageTypes.Ended, ct)).Payload.GetProperty("reason").GetString().ShouldBe("opponents_left");
    }

    private static async Task<RoomDto> ReadyAllAsync(AccountsApi api, string code, GuestCreatedResponse[] players, CancellationToken ct)
    {
        RoomDto last = null!;
        foreach (var player in players)
        {
            last = await SendAsync(api, player, HttpMethod.Post, $"/rooms/{code}/ready", new RoomReadyRequest { Ready = true }, ct);
        }

        return last;
    }

    private static async Task<RoomDto> WaitForRoomAsync(AccountsApi api, GuestCreatedResponse viewer, string code, Func<RoomDto, bool> match, CancellationToken ct)
    {
        for (var i = 0; i < 200; i++)
        {
            var room = await SendAsync(api, viewer, HttpMethod.Get, $"/rooms/{code}", null, ct);
            if (match(room))
            {
                return room;
            }

            await Task.Delay(50, ct);
        }

        throw new TimeoutException("The room never reached the expected state.");
    }

    private static async Task<RoomDto> NextRoomAsync(PlayClient lobby, Func<RoomDto, bool> match, CancellationToken ct)
    {
        var envelope = await lobby.WaitForAsync(e => e.T == MessageTypes.Room && match(e.Payload.Deserialize<RoomDto>(Json)!), ct, TimeSpan.FromSeconds(60));
        return envelope.Payload.Deserialize<RoomDto>(Json)!;
    }

    private static async Task<PlayClient> ConnectLobbyAsync(AccountsApi api, GuestCreatedResponse player, string code, CancellationToken ct)
    {
        using var response = await RealtimeApi.RequestTicketAsync(api, player.AccessToken, new TicketRequest { RoomCode = code }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var info = (await response.Content.ReadFromJsonAsync<RealtimeInfo>(ct))!;
        info.WsUrl.ShouldBe($"ws://localhost/ws/room/{code}");
        return await PlayClient.ConnectAsync(api.Factory, info.WsUrl, info.Ticket, ct);
    }

    private static async Task<SessionManifest<SessionConfig<Dictionary<string, string>>, LudoStateDto>> ManifestAsync(
        AccountsApi api, GuestCreatedResponse player, Guid sessionId, CancellationToken ct)
    {
        using var response = await RawAsync(api, player, HttpMethod.Get, $"/sessions/{sessionId}/manifest", null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SessionManifest<SessionConfig<Dictionary<string, string>>, LudoStateDto>>(ct))!;
    }

    private static async Task<RoomDto> CreateAsync(AccountsApi api, GuestCreatedResponse host, CreateRoomRequest body, CancellationToken ct)
    {
        using var response = await RawAsync(api, host, HttpMethod.Post, "/rooms", body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<RoomDto>(ct))!;
    }

    private static async Task<RoomDto> SendAsync(AccountsApi api, GuestCreatedResponse player, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await RawAsync(api, player, method, path, body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<RoomDto>(ct))!;
    }

    private static async Task ExpectAsync(AccountsApi api, GuestCreatedResponse player, HttpMethod method, string path, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await RawAsync(api, player, method, path, body, ct);
        response.StatusCode.ShouldBe(expected, $"{method} {path}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private static async Task<HttpResponseMessage> RawAsync(AccountsApi api, GuestCreatedResponse player, HttpMethod method, string path, object? body, CancellationToken ct)
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
