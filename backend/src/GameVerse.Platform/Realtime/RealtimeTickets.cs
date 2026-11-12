using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameVerse.Contracts.Session;
using StackExchange.Redis;

namespace GameVerse.Platform.Realtime;

/// <summary>What a socket ticket opens.</summary>
public enum TicketKind
{
    /// <summary>/ws/play/{sessionId}: play in a session.</summary>
    Play,

    /// <summary>/ws/room/{code}: follow a private room's lobby.</summary>
    Room,
}

/// <summary>
/// Single-use WebSocket tickets (CLAUDE.md section 7): short-lived, never the JWT. Other modules call this after their
/// own access check (the session's seats, the room's members); the ticket only proves that check passed.
/// </summary>
public interface IRealtimeTickets
{
    /// <summary>A fresh ticket for <paramref name="userId"/> and the socket URL to use it on.</summary>
    Task<RealtimeInfo> IssueAsync(TicketKind kind, string target, Guid userId, Uri requestBase, CancellationToken ct = default);
}

/// <summary>A redeemed ticket.</summary>
internal sealed record TicketClaim(Guid UserId, TicketKind Kind, string Target);

internal sealed class RealtimeTickets(IConnectionMultiplexer redis, RealtimeOptions options) : IRealtimeTickets
{
    public async Task<RealtimeInfo> IssueAsync(TicketKind kind, string target, Guid userId, Uri requestBase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requestBase);
        var ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var claim = JsonSerializer.Serialize(new StoredClaim { UserId = userId, Kind = kind, Target = target });

        // Stored under a hash of the ticket, so a Redis dump holds nothing a client could connect with.
        await redis.GetDatabase().StringSetAsync(RealtimeKeys.Ticket(Hash(ticket)), claim, TimeSpan.FromSeconds(options.TicketSeconds)).ConfigureAwait(false);
        return new RealtimeInfo { WsUrl = SocketUrl(kind, target, requestBase), Ticket = ticket };
    }

    /// <summary>Redeems a ticket once: GETDEL, so a second use (or a use after 30 seconds) finds nothing.</summary>
    public async Task<TicketClaim?> RedeemAsync(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket) || ticket.Length > 64)
        {
            return null;
        }

        var stored = await redis.GetDatabase().StringGetDeleteAsync(RealtimeKeys.Ticket(Hash(ticket))).ConfigureAwait(false);
        if (stored.IsNullOrEmpty)
        {
            return null;
        }

        var claim = JsonSerializer.Deserialize<StoredClaim>(stored.ToString())!;
        return new TicketClaim(claim.UserId, claim.Kind, claim.Target);
    }

    private string SocketUrl(TicketKind kind, string target, Uri requestBase)
    {
        var baseUri = string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? requestBase : new Uri(options.PublicBaseUrl);
        var scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        var path = kind == TicketKind.Play ? "/ws/play/" : "/ws/room/";
        return new UriBuilder(baseUri) { Scheme = scheme, Port = baseUri.IsDefaultPort ? -1 : baseUri.Port, Path = path + Uri.EscapeDataString(target), Query = string.Empty }
            .Uri.ToString();
    }

    private static string Hash(string ticket) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)));

    private sealed class StoredClaim
    {
        public Guid UserId { get; set; }

        public TicketKind Kind { get; set; }

        public string Target { get; set; } = string.Empty;
    }
}
