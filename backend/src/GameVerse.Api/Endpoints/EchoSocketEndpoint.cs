using System.Net.WebSockets;

namespace GameVerse.Api.Endpoints;

/// <summary>
/// /ws/echo: echoes text messages back. Proves the raw WebSocket stack (the same one /ws/play will use)
/// works through Docker, Traefik and Cloudflare, and that message limits are enforced.
/// TODO(gameverse): remove or restrict to Development when /ws/play lands (Prompt 1.4).
/// </summary>
internal static class EchoSocketEndpoint
{
    /// <summary>Largest accepted message. Game envelopes are small; the security baseline caps message size.</summary>
    public const int MaxMessageBytes = 4096;

    public static IEndpointRouteBuilder MapEchoSocket(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/echo", HandleAsync).ExcludeFromDescription();
        return app;
    }

    private static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket connection expected.").ConfigureAwait(false);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await EchoLoopAsync(socket, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task EchoLoopAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var message = new byte[MaxMessageBytes];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (length == message.Length)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message exceeds " + MaxMessageBytes + " bytes.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                result = await socket.ReceiveAsync(message.AsMemory(length), cancellationToken).ConfigureAwait(false);
                length += result.Count;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Text messages only.", cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            while (!result.EndOfMessage);

            await socket.SendAsync(message.AsMemory(0, length), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
    }
}
