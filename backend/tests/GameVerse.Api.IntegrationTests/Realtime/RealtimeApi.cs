using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GameVerse.Api.IntegrationTests.Accounts;
using GameVerse.Contracts.Accounts;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Platform.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace GameVerse.Api.IntegrationTests.Realtime;

/// <summary>Helpers for realtime tests: API instances that share one signing key, online sessions and tickets.</summary>
internal static class RealtimeApi
{
    /// <summary>One key for every instance a test starts, so a token from instance A works on instance B.</summary>
    private static readonly string SigningKey = CreateKey();

    /// <summary>Starts an API instance tuned for tests: AI acts at once, no message budget in the way.</summary>
    public static AccountsApi Start(StackFixture stack, Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["GameVerse:Auth:SigningKey"] = SigningKey,
            ["GameVerse:Realtime:AiDelayMilliseconds"] = "0",
            ["GameVerse:Realtime:MessagesPerSecond"] = "100000",
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        return AccountsApi.Start(stack, settings);
    }

    /// <summary>An online private session seating the given players (what a private room's start does).</summary>
    public static async Task<Guid> CreateOnlineSessionAsync(AccountsApi api, IReadOnlyList<GuestCreatedResponse> players, CancellationToken ct, Dictionary<string, string>? houseRules = null)
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var seats = players.Select(p => new NewSeat(Guid.Parse(p.UserId), false, null)).ToList();
        var (id, error) = await sessions.CreateAsync("ludo", "private-room", seats[0].UserId!.Value, seats, new SessionConfig<Dictionary<string, string>> { HouseRules = houseRules ?? [] }, ct);
        error.ShouldBe(CreateSessionError.None);
        return id!.Value;
    }

    public static async Task<HttpResponseMessage> RequestTicketAsync(AccountsApi api, string accessToken, TicketRequest body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/realtime/tickets", UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await api.Client.SendAsync(request, ct);
    }

    public static async Task<RealtimeInfo> TicketAsync(AccountsApi api, string accessToken, Guid sessionId, CancellationToken ct)
    {
        using var response = await RequestTicketAsync(api, accessToken, new TicketRequest { SessionId = sessionId.ToString() }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<RealtimeInfo>(ct)).ShouldNotBeNull();
    }

    public static async Task<PlayClient> ConnectAsync(AccountsApi api, string accessToken, Guid sessionId, CancellationToken ct)
    {
        var info = await TicketAsync(api, accessToken, sessionId, ct);
        return await PlayClient.ConnectAsync(api.Factory, info.WsUrl, info.Ticket, ct);
    }

    private static string CreateKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }
}
