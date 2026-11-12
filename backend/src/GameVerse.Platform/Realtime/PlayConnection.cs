using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using GameVerse.Contracts.Realtime;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameVerse.Platform.Realtime;

/// <summary>
/// One player's socket on <c>/ws/play/{sessionId}</c> (CLAUDE.md 4.5). Forwards valid intents to the match host through
/// the match's Redis inbox and relays the host's output for this seat. Holds no game state: whichever instance hosts the
/// match is the authority, this one only carries messages.
/// <para>Limits: text only, <see cref="RealtimeOptions.MaxMessageBytes"/> per message, a per-connection message budget,
/// malformed envelopes answered with an error and logged, a ping every 15 seconds and a close after 45 without a pong.</para>
/// </summary>
internal sealed class PlayConnection(
    WebSocket socket,
    Guid sessionId,
    int seat,
    MatchHostRegistry registry,
    IConnectionMultiplexer redis,
    RealtimeOptions options,
    TimeProvider time,
    ILogger log)
{
    /// <summary>Consecutive malformed envelopes, or consecutive over-budget messages, before the socket is closed.</summary>
    private const int StrikeLimit = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string connId = Guid.NewGuid().ToString("N");
    private readonly Channel<Outgoing> outgoing = Channel.CreateUnbounded<Outgoing>(new UnboundedChannelOptions { SingleReader = true });
    private long serverSeq;
    private long lastPong;
    private volatile bool gotSnapshot;

    public async Task RunAsync(CancellationToken aborted)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        var ct = cts.Token;
        lastPong = time.GetUtcNow().ToUnixTimeMilliseconds();

        var outbox = await redis.GetSubscriber().SubscribeAsync(RealtimeKeys.Outbox(sessionId)).ConfigureAwait(false);
        outbox.OnMessage(Relay);
        var sender = SendLoopAsync();
        var reason = "client closed";
        Task? heartbeat = null;
        Task? connect = null;
        try
        {
            await registry.EnsureHostAsync(sessionId).ConfigureAwait(false);
            await PushAsync(new HostInbound { Kind = InboundKinds.Connect, ConnId = connId, Seat = seat }).ConfigureAwait(false);
            connect = RetryConnectAsync(ct);
            heartbeat = HeartbeatAsync(cts);
            reason = await ReceiveLoopAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            reason = "closed by server";
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
            reason = "connection lost";
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            outgoing.Writer.TryComplete();
            try
            {
                await PushAsync(new HostInbound { Kind = InboundKinds.Disconnect, ConnId = connId, Seat = seat }).ConfigureAwait(false);
                await outbox.UnsubscribeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
            {
                // Shutting down: the host notices the missing heartbeat instead.
            }

            await Task.WhenAny(sender, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)).ConfigureAwait(false);
            await Quietly(heartbeat).ConfigureAwait(false);
            await Quietly(connect).ConfigureAwait(false);
            RealtimeLog.SocketClosed(log, sessionId, seat, reason);
        }
    }

    private void Relay(ChannelMessage message)
    {
        HostOutbound? output;
        try
        {
            output = JsonSerializer.Deserialize<HostOutbound>(message.Message.ToString(), Json);
        }
        catch (JsonException)
        {
            return;
        }

        if (output is null || !output.IsFor(connId, seat))
        {
            return;
        }

        if (output.T == MessageTypes.Snapshot && output.ConnId == connId)
        {
            gotSnapshot = true;
        }

        Send(output.T, output.StateVersion, output.Payload);
    }

    private void Send(string type, long? stateVersion, JsonElement? payload) =>
        outgoing.Writer.TryWrite(new Outgoing(type, stateVersion, payload, null, null));

    private void Close(WebSocketCloseStatus status, string description) =>
        outgoing.Writer.TryWrite(new Outgoing(null, null, null, status, description));

    /// <summary>The only writer to the socket (a WebSocket allows one send at a time).</summary>
    private async Task SendLoopAsync()
    {
        await foreach (var message in outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (message.CloseStatus is { } status)
                {
                    if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(status, message.CloseDescription, CancellationToken.None).ConfigureAwait(false);
                    }

                    return;
                }

                if (socket.State != WebSocketState.Open)
                {
                    return;
                }

                var envelope = new Envelope<JsonElement?>
                {
                    T = message.Type!,
                    Seq = ++serverSeq,
                    SessionId = sessionId.ToString(),
                    StateVersion = message.StateVersion,
                    Payload = message.Payload,
                };
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, Json), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    /// <summary>Returns why the connection ended.</summary>
    private async Task<string> ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[options.MaxMessageBytes];
        var budget = new MessageBudget(options.MessagesPerSecond, TimeProvider.System);
        var malformed = 0;
        var overBudget = 0;
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (length == buffer.Length)
                {
                    Close(WebSocketCloseStatus.MessageTooBig, "Message exceeds " + options.MaxMessageBytes + " bytes.");
                    return "message too big";
                }

                result = await socket.ReceiveAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
                length += result.Count;
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Close(WebSocketCloseStatus.NormalClosure, "Bye.");
                    return "client closed";
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    Close(WebSocketCloseStatus.InvalidMessageType, "Text messages only.");
                    return "binary message";
                }
            }
            while (!result.EndOfMessage);

            if (!budget.TrySpend())
            {
                // Echo the seq when there is one, so the client knows which action to resend later.
                Send(MessageTypes.Error, null, Error("rate_limited", "Too many messages; slow down.", PeekSeq(buffer.AsSpan(0, length))));
                if (++overBudget >= StrikeLimit)
                {
                    Close(WebSocketCloseStatus.PolicyViolation, "Too many messages.");
                    return "rate limited";
                }

                continue;
            }

            overBudget = 0;
            if (!await HandleAsync(buffer.AsMemory(0, length)).ConfigureAwait(false))
            {
                if (++malformed >= StrikeLimit)
                {
                    Close(WebSocketCloseStatus.PolicyViolation, "Too many malformed messages.");
                    return "malformed messages";
                }

                continue;
            }

            malformed = 0;
        }

        return "client closed";
    }

    /// <summary>False for a malformed envelope (already answered with an error).</summary>
    private async Task<bool> HandleAsync(ReadOnlyMemory<byte> text)
    {
        Envelope<JsonElement?>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope<JsonElement?>>(text.Span, Json);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        var problem = envelope is null ? "not a JSON envelope"
            : string.IsNullOrEmpty(envelope.T) ? "missing t"
            : envelope.SessionId.Length > 0 && envelope.SessionId != sessionId.ToString() ? "wrong sessionId"
            : envelope.T == MessageTypes.Action && envelope.Seq <= 0 ? "an action needs a positive seq"
            : null;
        if (problem is not null)
        {
            RealtimeLog.MalformedEnvelope(log, sessionId.ToString(), problem);
            Send(MessageTypes.Error, null, Error("malformed_envelope", problem, envelope?.Seq));
            return false;
        }

        switch (envelope!.T)
        {
            case MessageTypes.Action:
                await PushAsync(new HostInbound { Kind = InboundKinds.Action, ConnId = connId, Seat = seat, Seq = envelope.Seq, Payload = envelope.Payload }).ConfigureAwait(false);
                break;
            case MessageTypes.Resync:
                await PushAsync(new HostInbound { Kind = InboundKinds.Resync, ConnId = connId, Seat = seat }).ConfigureAwait(false);
                break;
            case MessageTypes.Pong:
                Interlocked.Exchange(ref lastPong, time.GetUtcNow().ToUnixTimeMilliseconds());
                break;
            default:
                Send(MessageTypes.Error, null, Error("unknown_type", "Clients send action, resync or pong.", envelope.Seq));
                break;
        }

        return true;
    }

    /// <summary>Pings every heartbeat, tells the host this connection is alive, and closes it after 45 seconds of silence.</summary>
    private async Task HeartbeatAsync(CancellationTokenSource connection)
    {
        var ct = connection.Token;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.HeartbeatSeconds), time, ct).ConfigureAwait(false);
            var silent = time.GetUtcNow().ToUnixTimeMilliseconds() - Interlocked.Read(ref lastPong);
            if (silent >= options.PongTimeoutSeconds * 1000L)
            {
                Close(WebSocketCloseStatus.PolicyViolation, "No pong for " + options.PongTimeoutSeconds + " seconds.");
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false); // let the close frame go out
                await connection.CancelAsync().ConfigureAwait(false);
                return;
            }

            Send(MessageTypes.Ping, null, null);
            await registry.EnsureHostAsync(sessionId).ConfigureAwait(false); // take the match over if its host died
            await PushAsync(new HostInbound { Kind = InboundKinds.Alive, ConnId = connId, Seat = seat }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Repeats the connect until the host answers with a snapshot: a host that is just starting elsewhere, or one that
    /// died, may miss the first one.
    /// </summary>
    private async Task RetryConnectAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 30 && !gotSnapshot && !ct.IsCancellationRequested; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false); // wall clock: this waits on Redis, not on the game
            if (!gotSnapshot)
            {
                await registry.EnsureHostAsync(sessionId).ConfigureAwait(false);
                await PushAsync(new HostInbound { Kind = InboundKinds.Connect, ConnId = connId, Seat = seat }).ConfigureAwait(false);
            }
        }
    }

    private async Task PushAsync(HostInbound message)
    {
        var db = redis.GetDatabase();
        var inbox = RealtimeKeys.Inbox(sessionId);
        await db.ListRightPushAsync(inbox, JsonSerializer.Serialize(message, Json)).ConfigureAwait(false);
        await db.KeyExpireAsync(inbox, TimeSpan.FromHours(options.SnapshotHours)).ConfigureAwait(false);
        await redis.GetSubscriber().PublishAsync(RealtimeKeys.InboxPoke(sessionId), "1").ConfigureAwait(false);
    }

    private static long? PeekSeq(ReadOnlySpan<byte> text)
    {
        try
        {
            return JsonSerializer.Deserialize<EnvelopeHeader>(text, Json)?.Seq;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement Error(string code, string message, long? seq) =>
        JsonSerializer.SerializeToElement(new ErrorPayload { Code = code, Message = message, Seq = seq }, Json);

    private static async Task Quietly(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or RedisException or ObjectDisposedException)
        {
            // Expected on close and on shutdown.
        }
    }

    private sealed record Outgoing(string? Type, long? StateVersion, JsonElement? Payload, WebSocketCloseStatus? CloseStatus, string? CloseDescription);
}
