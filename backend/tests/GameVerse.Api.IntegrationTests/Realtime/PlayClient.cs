using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Realtime;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GameVerse.Api.IntegrationTests.Realtime;

/// <summary>
/// A simulated game client on /ws/play: it speaks the envelope protocol exactly as Unity and web will (CLAUDE.md 4.5).
/// Its Ludo strategy is deliberately dumb (roll, then try tokens 0 to 3 until one is legal), because it has no engine:
/// the server is the only authority.
/// </summary>
internal sealed class PlayClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WebSocket socket;
    private long seq;

    private PlayClient(WebSocket socket, string sessionId)
    {
        this.socket = socket;
        SessionId = sessionId;
    }

    public string SessionId { get; }

    public long StateVersion { get; private set; } = -1;

    public LudoStateDto? State { get; private set; }

    public int? TurnSeat { get; private set; }

    public long? TurnDeadline { get; private set; }

    public MatchEndedEvent? Ended { get; private set; }

    public List<Envelope<JsonElement>> Received { get; } = [];

    public WebSocketState SocketState => socket.State;

    public WebSocketCloseStatus? CloseStatus => socket.CloseStatus;

    public static async Task<PlayClient> ConnectAsync(WebApplicationFactory<Program> factory, string wsUrl, string ticket, CancellationToken ct)
    {
        var client = factory.Server.CreateWebSocketClient();
        var socket = await client.ConnectAsync(new Uri(wsUrl + "?ticket=" + Uri.EscapeDataString(ticket)), ct);
        return new PlayClient(socket, wsUrl[(wsUrl.LastIndexOf('/') + 1)..]);
    }

    public Task SendRawAsync(string text, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);

    public Task SendBinaryAsync(CancellationToken ct) =>
        socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, ct);

    public async Task<long> SendActionAsync(object payload, CancellationToken ct)
    {
        var mine = ++seq;
        await SendAsync(MessageTypes.Action, mine, payload, ct);
        return mine;
    }

    public Task SendAsync(string type, long envelopeSeq, object? payload, CancellationToken ct) =>
        SendRawAsync(JsonSerializer.Serialize(new Envelope<object?> { T = type, Seq = envelopeSeq, SessionId = SessionId, Payload = payload }, Json), ct);

    /// <summary>The next message, or null when the server closed the socket.</summary>
    public async Task<Envelope<JsonElement>?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var length = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), ct);
            length += result.Count;
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
        }
        while (!result.EndOfMessage);

        var envelope = JsonSerializer.Deserialize<Envelope<JsonElement>>(buffer.AsSpan(0, length), Json)!;
        Received.Add(envelope);
        Track(envelope);
        return envelope;
    }

    /// <summary>Receives until a message matches, failing after <paramref name="timeout"/>.</summary>
    public async Task<Envelope<JsonElement>> WaitForAsync(Func<Envelope<JsonElement>, bool> match, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            var envelope = await ReceiveAsync(limit.Token) ?? throw new InvalidOperationException("The server closed the socket.");
            if (match(envelope))
            {
                return envelope;
            }
        }
    }

    /// <summary>
    /// Plays as <paramref name="seat"/> until the match ends (or <paramref name="maxActions"/> of its own actions have been
    /// accepted). Acts only on the newest state and only with nothing in flight, as a real client should.
    /// </summary>
    public async Task<MatchEndedEvent?> PlayAsync(int seat, CancellationToken ct, int maxActions = int.MaxValue)
    {
        long? pending = null;
        var tryToken = 0;
        var accepted = 0;
        while (Ended is null && accepted < maxActions)
        {
            if (pending is null && State is not null && TurnSeat == seat)
            {
                if (State.Phase == "roll")
                {
                    pending = await SendActionAsync(new Dictionary<string, bool> { [LudoActions.Roll] = true }, ct);
                }
                else if (State.Phase == "move")
                {
                    pending = await SendActionAsync(new Dictionary<string, int> { [LudoActions.Move] = tryToken }, ct);
                }
            }

            var envelope = await WaitForAsync(_ => true, ct, TimeSpan.FromSeconds(30));
            var payloadSeq = envelope.T is MessageTypes.State or MessageTypes.Snapshot && envelope.Payload.TryGetProperty("appliedSeq", out var applied) && applied.ValueKind == JsonValueKind.Number
                ? applied.GetInt64()
                : (long?)null;
            if (envelope.T is MessageTypes.State or MessageTypes.Snapshot && pending is not null && (payloadSeq == pending || envelope.T == MessageTypes.Snapshot))
            {
                pending = null;
                tryToken = 0;
                accepted++;
            }
            else if (envelope.T == MessageTypes.Error && pending is not null && envelope.Payload.GetProperty("seq").GetInt64() == pending)
            {
                var code = envelope.Payload.GetProperty("code").GetString();
                pending = null;
                tryToken = code == "illegal_move" ? tryToken + 1 : 0;
                if (tryToken > 3)
                {
                    throw new InvalidOperationException("No token was legal: the server's move phase disagrees with the client's view.");
                }
            }
        }

        return Ended;
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
        }
    }

    public ValueTask DisposeAsync()
    {
        socket.Abort();
        socket.Dispose();
        return ValueTask.CompletedTask;
    }

    private void Track(Envelope<JsonElement> envelope)
    {
        switch (envelope.T)
        {
            case MessageTypes.State or MessageTypes.Snapshot when envelope.StateVersion >= StateVersion:
                var update = envelope.Payload.Deserialize<StateUpdate<LudoStateDto>>(Json)!;
                StateVersion = envelope.StateVersion!.Value;
                State = update.State;
                TurnSeat = update.TurnSeat;
                TurnDeadline = update.TurnDeadline;
                break;
            case MessageTypes.Ended:
                Ended = envelope.Payload.Deserialize<MatchEndedEvent>(Json);
                break;
        }
    }
}
