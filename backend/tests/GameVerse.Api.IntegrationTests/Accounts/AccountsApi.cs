using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameVerse.Contracts.Accounts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace GameVerse.Api.IntegrationTests.Accounts;

/// <summary>
/// A test host for the auth flows with a controllable clock, plus small typed helpers. JSON uses the API's own defaults
/// (camelCase), which System.Net.Http.Json reads case-insensitively.
/// </summary>
internal sealed class AccountsApi : IAsyncDisposable
{
    private AccountsApi(GameVerseApiFactory factory, FakeTimeProvider time)
    {
        Factory = factory;
        Time = time;
        Client = factory.CreateClient(new() { HandleCookies = false });
    }

    public GameVerseApiFactory Factory { get; }

    public FakeTimeProvider Time { get; }

    public HttpClient Client { get; }

    public static AccountsApi Start(StackFixture stack, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var factory = new GameVerseApiFactory(stack, overrides, builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(time)));
        return new AccountsApi(factory, time);
    }

    public async Task<GuestCreatedResponse> CreateGuestAsync(CancellationToken ct)
    {
        using var response = await Client.PostAsync(new Uri("/auth/guest", UriKind.Relative), null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<GuestCreatedResponse>(ct)).ShouldNotBeNull();
    }

    public Task<HttpResponseMessage> GuestLoginAsync(string guestId, string secret, CancellationToken ct) =>
        Client.PostAsJsonAsync(new Uri("/auth/guest/login", UriKind.Relative), new GuestLoginRequest { GuestId = guestId, DeviceSecret = secret }, ct);

    public Task<HttpResponseMessage> RefreshAsync(string? refreshToken, CancellationToken ct) =>
        Client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest { RefreshToken = refreshToken }, ct);

    public Task<HttpResponseMessage> LogoutAsync(string refreshToken, CancellationToken ct) =>
        Client.PostAsJsonAsync(new Uri("/auth/logout", UriKind.Relative), new RefreshRequest { RefreshToken = refreshToken }, ct);

    public async Task<HttpResponseMessage> MeAsync(string? accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/accounts/me", UriKind.Relative));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await Client.SendAsync(request, ct);
    }

    public static async Task<AuthTokensResponse> TokensAsync(HttpResponseMessage response, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<AuthTokensResponse>(ct)).ShouldNotBeNull();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}
