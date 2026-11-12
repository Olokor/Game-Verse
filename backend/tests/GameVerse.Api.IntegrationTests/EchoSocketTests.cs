using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace GameVerse.Api.IntegrationTests;

public class EchoSocketTests(StackFixture stack)
{
    private static Uri EchoUri(GameVerseApiFactory factory) => new(factory.Server.BaseAddress, "ws/echo");

    [Fact]
    public async Task Echoes_text_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);
        using var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(EchoUri(factory), ct);

        foreach (var text in new[] { "hello", "{\"t\":\"ping\",\"seq\":1}", new string('x', 4096) })
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
            (await ReceiveTextAsync(socket, ct)).ShouldBe(text);
        }

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
        socket.State.ShouldBe(WebSocketState.Closed);
    }

    [Fact]
    public async Task Closes_with_MessageTooBig_over_4KB()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);
        using var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(EchoUri(factory), ct);

        await socket.SendAsync(new byte[4097], WebSocketMessageType.Text, true, ct);
        var result = await socket.ReceiveAsync(new byte[256], ct);

        result.MessageType.ShouldBe(WebSocketMessageType.Close);
        socket.CloseStatus.ShouldBe(WebSocketCloseStatus.MessageTooBig);
    }

    [Fact]
    public async Task Closes_with_InvalidMessageType_for_binary()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GameVerseApiFactory(stack);
        using var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(EchoUri(factory), ct);

        await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, ct);
        var result = await socket.ReceiveAsync(new byte[256], ct);

        result.MessageType.ShouldBe(WebSocketMessageType.Close);
        socket.CloseStatus.ShouldBe(WebSocketCloseStatus.InvalidMessageType);
    }

    [Fact]
    public async Task Plain_http_request_gets_400()
    {
        await using var factory = new GameVerseApiFactory(stack);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/ws/echo", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var length = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), ct);
            length += result.Count;
        }
        while (!result.EndOfMessage);

        result.MessageType.ShouldBe(WebSocketMessageType.Text);
        return Encoding.UTF8.GetString(buffer, 0, length);
    }
}
