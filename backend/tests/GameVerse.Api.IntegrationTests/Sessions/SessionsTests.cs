using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameVerse.Api.IntegrationTests.Accounts;
using GameVerse.Contracts.Accounts;
using GameVerse.Contracts.Session;
using GameVerse.Infrastructure.Persistence;
using GameVerse.Platform.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameVerse.Api.IntegrationTests.Sessions;

/// <summary>Sessions, the SessionManifest, the append-only move log and results (Prompt 1.2, ADR-0007).</summary>
public class SessionsTests(StackFixture stack)
{
    [Fact]
    public async Task A_vs_AI_Ludo_session_returns_its_manifest_with_the_player_and_three_AI_seats()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var created = await PostAsync(api, guest.AccessToken, new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 4, AiLevel = 2 }, ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        using var manifest = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));
        var m = manifest.RootElement;
        created.Headers.Location!.ToString().ShouldBe($"/sessions/{m.GetProperty("sessionId").GetString()}/manifest");
        m.GetProperty("gameId").GetString().ShouldBe("ludo");
        m.GetProperty("gameVersion").GetString().ShouldBe("0.1.0");
        m.GetProperty("mode").GetString().ShouldBe("vs_ai", "the manifest mode uses the CLAUDE.md 4.2 kind vocabulary");
        m.GetProperty("localPlayerId").GetString().ShouldBe(guest.UserId);
        m.GetProperty("config").GetProperty("aiLevel").GetInt32().ShouldBe(2);
        m.GetProperty("realtime").ValueKind.ShouldBe(JsonValueKind.Null);
        m.GetProperty("initialState").ValueKind.ShouldBe(JsonValueKind.Null);

        var players = m.GetProperty("players").EnumerateArray().ToList();
        players.Count.ShouldBe(4);
        players[0].GetProperty("id").GetString().ShouldBe(guest.UserId);
        players[0].GetProperty("isAI").GetBoolean().ShouldBeFalse();
        players[0].GetProperty("displayName").GetString().ShouldNotBeNull().ShouldMatch("^Player[0-9]{4}$");
        players.Skip(1).ShouldAllBe(p => p.GetProperty("isAI").GetBoolean() && p.GetProperty("displayName").GetString() == "AI level 2");
        players.Select(p => p.GetProperty("seat").GetInt32()).ShouldBe([0, 1, 2, 3]);
    }

    [Fact]
    public async Task Pass_and_play_seats_are_local_players_not_AI()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var created = await PostAsync(api, guest.AccessToken, new CreateSessionRequest { GameId = "ludo", Mode = "pass-and-play", PlayerCount = 2 }, ct);

        var manifest = (await created.Content.ReadFromJsonAsync<SessionManifest>(ct)).ShouldNotBeNull();
        manifest.Mode.ShouldBe("local");
        manifest.Players[1].IsAI.ShouldBeFalse();
        manifest.Players[1].Id.ShouldBe("local-1");
        manifest.Players[1].DisplayName.ShouldBe("Player 2");
    }

    [Fact]
    public async Task Only_a_participant_can_read_the_manifest_and_their_sessions_list_shows_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var owner = await api.CreateGuestAsync(ct);
        var stranger = await api.CreateGuestAsync(ct);
        using var created = await PostAsync(api, owner.AccessToken, new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 2 }, ct);
        var sessionId = (await created.Content.ReadFromJsonAsync<SessionManifest>(ct)).ShouldNotBeNull().SessionId;

        using var mine = await GetAsync(api, $"/sessions/{sessionId}/manifest", owner.AccessToken, ct);
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var theirs = await GetAsync(api, $"/sessions/{sessionId}/manifest", stranger.AccessToken, ct);
        theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound, "same answer as a missing session");
        using var anonymous = await GetAsync(api, $"/sessions/{sessionId}/manifest", null, ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var list = await GetAsync(api, "/sessions/me?status=active", owner.AccessToken, ct);
        var sessions = (await list.Content.ReadFromJsonAsync<List<SessionSummary>>(ct)).ShouldNotBeNull();
        sessions.ShouldContain(s => s.SessionId == sessionId && s.GameId == "ludo" && s.Status == SessionStatuses.Active);
        using var strangerList = await GetAsync(api, "/sessions/me", stranger.AccessToken, ct);
        (await strangerList.Content.ReadFromJsonAsync<List<SessionSummary>>(ct)).ShouldNotBeNull().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("whot", "vs-ai", 2, HttpStatusCode.Conflict)] // coming soon
    [InlineData("minecraft", "vs-ai", 2, HttpStatusCode.NotFound)]
    [InlineData("ludo", "no-such-mode", 2, HttpStatusCode.BadRequest)]
    [InlineData("ludo", "vs-ai", 5, HttpStatusCode.BadRequest)] // Ludo seats 2 to 4
    [InlineData("ludo", "quick-match", 2, HttpStatusCode.BadRequest)] // online: rooms and matchmaking only
    public async Task Sessions_are_refused_for_unplayable_games_bad_modes_and_counts(string game, string mode, int players, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var response = await PostAsync(api, guest.AccessToken, new CreateSessionRequest { GameId = game, Mode = mode, PlayerCount = players }, ct);

        response.StatusCode.ShouldBe(expected);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Creating_a_session_needs_a_token()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);

        using var response = await PostAsync(api, null, new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 2 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Moves_append_in_order_and_the_database_refuses_to_change_or_delete_them()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var sessionId = await NewSessionAsync(api, ct);
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var roll = JsonDocument.Parse("""{"type":"roll"}""").RootElement;

        (await sessions.AppendMoveAsync(sessionId, 1, 0, roll, 1, ct)).ShouldBe(AppendMoveResult.Appended);
        (await sessions.AppendMoveAsync(sessionId, 2, 1, roll, 2, ct)).ShouldBe(AppendMoveResult.Appended);
        (await sessions.AppendMoveAsync(sessionId, 2, 1, roll, 2, ct)).ShouldBe(AppendMoveResult.OutOfOrder, "duplicate seq");
        (await sessions.AppendMoveAsync(sessionId, 4, 0, roll, 4, ct)).ShouldBe(AppendMoveResult.OutOfOrder, "gap");
        (await sessions.AppendMoveAsync(Guid.NewGuid(), 1, 0, roll, 1, ct)).ShouldBe(AppendMoveResult.UnknownSession);

        var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
        var update = await Should.ThrowAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sessions.move_logs SET seat = 3 WHERE session_id = {sessionId}", ct));
        update.MessageText.ShouldContain("append-only");
        var delete = await Should.ThrowAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM sessions.move_logs WHERE session_id = {sessionId}", ct));
        delete.MessageText.ShouldContain("append-only");
        (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM sessions.move_logs WHERE session_id = {sessionId}").SingleAsync(ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Recording_a_result_finishes_the_session_once_and_stops_further_moves()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var sessionId = await NewSessionAsync(api, ct);
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var outcome = new MatchOutcome("win", [1], "all_tokens_home", new Dictionary<int, int> { [1] = 1, [0] = 2 });

        (await sessions.RecordResultAsync(sessionId, outcome, ct)).ShouldBeTrue();
        (await sessions.RecordResultAsync(sessionId, outcome, ct)).ShouldBeFalse("one result per session");
        (await sessions.AppendMoveAsync(sessionId, 1, 0, JsonDocument.Parse("{}").RootElement, 1, ct)).ShouldBe(AppendMoveResult.NotActive);

        var db = scope.ServiceProvider.GetRequiredService<GameVerseDbContext>();
        (await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM sessions.game_sessions WHERE id = {sessionId}").SingleAsync(ct)).ShouldBe(SessionStatuses.Finished);
        (await db.Database.SqlQuery<string>($"SELECT placements_json::text AS \"Value\" FROM sessions.match_results WHERE session_id = {sessionId}").SingleAsync(ct))
            .ShouldContain("\"place\": 1");
    }

    private static async Task<Guid> NewSessionAsync(AccountsApi api, CancellationToken ct)
    {
        var guest = await api.CreateGuestAsync(ct);
        using var created = await PostAsync(api, guest.AccessToken, new CreateSessionRequest { GameId = "ludo", Mode = "vs-ai", PlayerCount = 2 }, ct);
        return Guid.Parse((await created.Content.ReadFromJsonAsync<SessionManifest>(ct)).ShouldNotBeNull().SessionId);
    }

    private static async Task<HttpResponseMessage> PostAsync(AccountsApi api, string? accessToken, CreateSessionRequest body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/sessions", UriKind.Relative)) { Content = JsonContent.Create(body) };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await api.Client.SendAsync(request, ct);
    }

    private static async Task<HttpResponseMessage> GetAsync(AccountsApi api, string path, string? accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await api.Client.SendAsync(request, ct);
    }
}
