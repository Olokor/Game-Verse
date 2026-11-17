using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using GameVerse.Contracts.Realtime;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameVerse.Platform.Realtime;

/// <summary>
/// The rooms a lobby socket can follow. Implemented by the module that owns rooms (Matchmaking), so Realtime never
/// depends on it: Realtime only carries the room's JSON.
/// </summary>
public interface IRoomFeed
{
    /// <summary>The room's current wire form for a seated member, or null when unknown, closed, expired or not theirs.</summary>
    Task<JsonElement?> GetForMemberAsync(string code, Guid userId, CancellationToken ct = default);
}

/// <summary>Pushes a room's new state to every lobby socket following it, on every instance.</summary>
public interface IRoomBroadcaster
{
    Task PublishRoomAsync(string code, object room);
}

internal sealed class RoomBroadcaster(IConnectionMultiplexer redis) : IRoomBroadcaster
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishRoomAsync(string code, object room) =>
        await redis.GetSubscriber().PublishAsync(Channel(code), JsonSerializer.Serialize(room, room.GetType(), Json)).ConfigureAwait(false);

    public static RedisChannel Channel(string code) => RedisChannel.Literal("rt:room:" + code);
}

/// <summary>
/// One member's lobby socket, <c>/ws/room/{code}</c>: server to client only. It sends the room on connect and after every
/// change (<c>room</c> envelopes), with the same heartbeat and limits as the game socket. Clients send only pongs.
/// </summary>
internal sealed class RoomConnection(
    WebSocket socket, string code, JsonElement initial, IConnectionMultiplexer redis, RealtimeOptions options, TimeProvider time, ILogger log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Channel<(string? Text, WebSocketCloseStatus? Close, string? Reason)> outgoing =
        Channel.CreateUnbounded<(string?, WebSocketCloseStatus?, string?)>(new UnboundedChannelOptions { SingleReader = true });

    private long seq;
    private long lastPong;

    public async Task RunAsync(CancellationToken aborted)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        lastPong = time.GetUtcNow().ToUnixTimeMilliseconds();
        var feed = await redis.GetSubscriber().SubscribeAsync(RoomBroadcaster.Channel(code)).ConfigureAwait(false);
        feed.OnMessage(message =>
        {
            try
            {
                Send(MessageTypes.Room, JsonSerializer.Deserialize<JsonElement>(message.Message.ToString()));
            }
            catch (JsonException)
            {
                // Not ours to fix: drop it.
            }
        });

        var sender = SendLoopAsync();
        Send(MessageTypes.Room, initial);
        var heartbeat = HeartbeatAsync(cts);
        try
        {
            await ReceiveLoopAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
            // Closed or lost.
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            outgoing.Writer.TryComplete();
            try
            {
                await feed.UnsubscribeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
            {
                // Shutting down.
            }

            await Task.WhenAny(sender, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)).ConfigureAwait(false);
            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }

            RealtimeLog.LobbyClosed(log, code);
        }
    }

    private void Send(string type, JsonElement? payload) =>
        outgoing.Writer.TryWrite((JsonSerializer.Serialize(new Envelope<JsonElement?> { T = type, Seq = Interlocked.Increment(ref seq), Payload = payload }, Json), null, null));

    private void Close(WebSocketCloseStatus status, string reason) => outgoing.Writer.TryWrite((null, status, reason));

    private async Task SendLoopAsync()
    {
        await foreach (var (text, close, reason) in outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (close is { } status)
                {
                    if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(status, reason, CancellationToken.None).ConfigureAwait(false);
                    }

                    return;
                }

                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(text!), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[options.MaxMessageBytes];
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (length == buffer.Length)
                {
                    Close(WebSocketCloseStatus.MessageTooBig, "Message exceeds " + options.MaxMessageBytes + " bytes.");
                    return;
                }

                result = await socket.ReceiveAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
                length += result.Count;
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Close(WebSocketCloseStatus.NormalClosure, "Bye.");
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    Close(WebSocketCloseStatus.InvalidMessageType, "Text messages only.");
                    return;
                }
            }
            while (!result.EndOfMessage);

            EnvelopeHeader? header;
            try
            {
                header = JsonSerializer.Deserialize<EnvelopeHeader>(buffer.AsSpan(0, length), Json);
            }
            catch (JsonException)
            {
                header = null;
            }

            if (header?.T == MessageTypes.Pong)
            {
                Interlocked.Exchange(ref lastPong, time.GetUtcNow().ToUnixTimeMilliseconds());
            }
            else
            {
                Send(MessageTypes.Error, JsonSerializer.SerializeToElement(
                    new ErrorPayload { Code = "unknown_type", Message = "The lobby socket only listens for pong; change the room through /rooms.", Seq = header?.Seq }, Json));
            }
        }
    }

    private async Task HeartbeatAsync(CancellationTokenSource connection)
    {
        var ct = connection.Token;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.HeartbeatSeconds), time, ct).ConfigureAwait(false);
            if (time.GetUtcNow().ToUnixTimeMilliseconds() - Interlocked.Read(ref lastPong) >= options.PongTimeoutSeconds * 1000L)
            {
                Close(WebSocketCloseStatus.PolicyViolation, "No pong for " + options.PongTimeoutSeconds + " seconds.");
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
                await connection.CancelAsync().ConfigureAwait(false);
                return;
            }

            Send(MessageTypes.Ping, null);
        }
    }
}
