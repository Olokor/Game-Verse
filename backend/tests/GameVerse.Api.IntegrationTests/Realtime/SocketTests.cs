using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;

namespace GameVerse.Api.IntegrationTests.Realtime;

/// <summary>Tickets and the per-connection limits of /ws/play (CLAUDE.md sections 4.5 and 7).</summary>
public class SocketTests(StackFixture stack)
{
    [Fact]
    public async Task A_ticket_opens_one_socket_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);
        var info = await RealtimeApi.TicketAsync(api, ada.AccessToken, sessionId, ct);

        await using (var first = await PlayClient.ConnectAsync(api.Factory, info.WsUrl, info.Ticket, ct))
        {
            await first.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);
        }

        var reuse = await Should.ThrowAsync<InvalidOperationException>(() => PlayClient.ConnectAsync(api.Factory, info.WsUrl, info.Ticket, ct));
        reuse.Message.ShouldContain("401");
    }

    [Fact]
    public async Task A_ticket_is_bound_to_its_session_and_expires()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack, new() { ["GameVerse:Realtime:TicketSeconds"] = "1" });
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var first = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);
        var second = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);

        var forFirst = await RealtimeApi.TicketAsync(api, ada.AccessToken, first, ct);
        var wrongSession = forFirst.WsUrl.Replace(first.ToString(), second.ToString(), StringComparison.Ordinal);
        (await Should.ThrowAsync<InvalidOperationException>(() => PlayClient.ConnectAsync(api.Factory, wrongSession, forFirst.Ticket, ct))).Message.ShouldContain("401");

        var late = await RealtimeApi.TicketAsync(api, ada.AccessToken, first, ct);
        await Task.Delay(TimeSpan.FromSeconds(1.5), ct); // Redis expiry is wall clock
        (await Should.ThrowAsync<InvalidOperationException>(() => PlayClient.ConnectAsync(api.Factory, late.WsUrl, late.Ticket, ct))).Message.ShouldContain("401");
    }

    [Fact]
    public async Task Only_seated_players_of_server_hosted_sessions_get_tickets()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var stranger = await api.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);

        using (var theirs = await RealtimeApi.RequestTicketAsync(api, stranger.AccessToken, new TicketRequest { SessionId = sessionId.ToString() }, ct))
        {
            theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // Pass-and-play runs on the device: no socket, no ticket, and none in its manifest.
        using var create = new HttpRequestMessage(HttpMethod.Post, new Uri("/sessions", UriKind.Relative))
        {
            Content = JsonContent.Create(new CreateSessionRequest { GameId = "ludo", Mode = "pass-and-play", PlayerCount = 2 }),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ada.AccessToken);
        using var created = await api.Client.SendAsync(create, ct);
        var local = (await created.Content.ReadFromJsonAsync<SessionManifest>(ct)).ShouldNotBeNull();
        local.Realtime.ShouldBeNull();
        using (var offline = await RealtimeApi.RequestTicketAsync(api, ada.AccessToken, new TicketRequest { SessionId = local.SessionId }, ct))
        {
            offline.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var both = await RealtimeApi.RequestTicketAsync(api, ada.AccessToken, new TicketRequest { SessionId = sessionId.ToString(), RoomCode = "ABCDEF" }, ct);
        both.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oversized_and_binary_messages_close_the_socket()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);

        await using (var big = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct))
        {
            await big.SendRawAsync(new string('x', 4097), ct);
            await DrainUntilClosedAsync(big, ct);
            big.CloseStatus.ShouldBe(WebSocketCloseStatus.MessageTooBig);
        }

        await using var binary = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct);
        await binary.SendBinaryAsync(ct);
        await DrainUntilClosedAsync(binary, ct);
        binary.CloseStatus.ShouldBe(WebSocketCloseStatus.InvalidMessageType);
    }

    [Fact]
    public async Task Malformed_envelopes_get_an_error_and_five_in_a_row_close_the_socket()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack);
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);
        await using var client = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct);
        await client.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);

        string[] junk = ["not json", "{}", "{\"t\":\"action\",\"seq\":0}", $"{{\"t\":\"resync\",\"sessionId\":\"{Guid.NewGuid()}\"}}", "[1,2]"];
        foreach (var text in junk)
        {
            await client.SendRawAsync(text, ct);
        }

        await DrainUntilClosedAsync(client, ct);
        client.Received.Count(e => e.T == MessageTypes.Error && e.Payload.GetProperty("code").GetString() == "malformed_envelope").ShouldBe(5);
        client.CloseStatus.ShouldBe(WebSocketCloseStatus.PolicyViolation);
    }

    [Fact]
    public async Task A_flood_of_messages_is_refused_then_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = RealtimeApi.Start(stack, new() { ["GameVerse:Realtime:MessagesPerSecond"] = "3" });
        var ada = await api.CreateGuestAsync(ct);
        var bayo = await api.CreateGuestAsync(ct);
        var sessionId = await RealtimeApi.CreateOnlineSessionAsync(api, [ada, bayo], ct);
        await using var client = await RealtimeApi.ConnectAsync(api, ada.AccessToken, sessionId, ct);
        await client.WaitForAsync(e => e.T == MessageTypes.Snapshot, ct);

        for (var i = 1; i <= 8; i++)
        {
            await client.SendAsync(MessageTypes.Resync, i, null, ct);
        }

        await DrainUntilClosedAsync(client, ct);
        var refused = client.Received.Where(e => e.T == MessageTypes.Error && e.Payload.GetProperty("code").GetString() == "rate_limited").ToList();
        refused.Select(e => e.Payload.GetProperty("seq").GetInt64()).ShouldBe([4L, 5, 6, 7, 8], "each refusal names the message it refused");
        client.CloseStatus.ShouldBe(WebSocketCloseStatus.PolicyViolation);
    }

    private static async Task DrainUntilClosedAsync(PlayClient client, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        while (await client.ReceiveAsync(limit.Token) is not null)
        {
            // keep reading until the close frame
        }
    }
}
