using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameVerse.Contracts.Accounts;

namespace GameVerse.Api.IntegrationTests.Accounts;

/// <summary>Guest accounts, sessions and refresh-token rotation (Prompt 1.1, ADR-0006).</summary>
public class GuestAndSessionTests(StackFixture stack)
{
    [Fact]
    public async Task A_guest_is_created_and_signs_back_in_with_its_device_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);

        var guest = await api.CreateGuestAsync(ct);
        guest.IsGuest.ShouldBeTrue();
        guest.DeviceSecret.ShouldStartWith("gvd_");
        guest.RefreshToken.ShouldNotBeNull().ShouldStartWith("gvr_");

        using var login = await api.GuestLoginAsync(guest.UserId, guest.DeviceSecret, ct);
        var tokens = await AccountsApi.TokensAsync(login, ct);
        tokens.UserId.ShouldBe(guest.UserId);
        tokens.RefreshToken.ShouldNotBe(guest.RefreshToken); // a new session
    }

    [Fact]
    public async Task Guest_login_with_a_wrong_secret_or_unknown_id_is_rejected_the_same_way()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var wrongSecret = await api.GuestLoginAsync(guest.UserId, "gvd_not-the-secret", ct);
        using var unknownId = await api.GuestLoginAsync(Guid.NewGuid().ToString(), guest.DeviceSecret, ct);

        wrongSecret.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownId.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // Same title and message either way: the response must not reveal whether the account exists.
        (await ProblemTextAsync(wrongSecret, ct)).ShouldBe(await ProblemTextAsync(unknownId, ct));
    }

    [Fact]
    public async Task The_access_token_opens_accounts_me_and_a_guest_counts_as_a_minor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var anonymous = await api.MeAsync(null, ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var me = await api.MeAsync(guest.AccessToken, ct);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var account = (await me.Content.ReadFromJsonAsync<AccountResponse>(ct)).ShouldNotBeNull();
        account.UserId.ShouldBe(guest.UserId);
        account.IsGuest.ShouldBeTrue();
        account.IsMinor.ShouldBeTrue();
        account.Email.ShouldBeNull();
    }

    [Fact]
    public async Task A_token_signed_by_another_key_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var other = AccountsApi.Start(stack); // its own ephemeral Development key
        var foreign = await other.CreateGuestAsync(ct);
        await using var api = AccountsApi.Start(stack);

        using var me = await api.MeAsync(foreign.AccessToken, ct);

        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_an_old_token_revokes_the_whole_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var first = await api.RefreshAsync(guest.RefreshToken, ct);
        var rotated = await AccountsApi.TokensAsync(first, ct);
        rotated.RefreshToken.ShouldNotBe(guest.RefreshToken);

        api.Time.Advance(TimeSpan.FromMinutes(1)); // past the retry grace: presenting the old token now means it was copied
        using var replay = await api.RefreshAsync(guest.RefreshToken, ct);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var victim = await api.RefreshAsync(rotated.RefreshToken, ct);
        victim.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "reuse detection revokes every token in the family");
    }

    [Fact]
    public async Task A_quick_retry_after_a_lost_response_gets_new_tokens_and_retires_the_lost_ones()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var lost = await api.RefreshAsync(guest.RefreshToken, ct); // imagine this response never reached the phone
        var lostTokens = await AccountsApi.TokensAsync(lost, ct);
        api.Time.Advance(TimeSpan.FromSeconds(5));

        using var retry = await api.RefreshAsync(guest.RefreshToken, ct);
        var retried = await AccountsApi.TokensAsync(retry, ct);

        using var useLost = await api.RefreshAsync(lostTokens.RefreshToken, ct);
        useLost.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var useRetried = await api.RefreshAsync(retried.RefreshToken, ct);
        await AccountsApi.TokensAsync(useRetried, ct);
    }

    [Fact]
    public async Task Logout_ends_the_session_and_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var logout = await api.LogoutAsync(guest.RefreshToken!, ct);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var again = await api.LogoutAsync(guest.RefreshToken!, ct);
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var refresh = await api.RefreshAsync(guest.RefreshToken, ct);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_expired_refresh_token_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        api.Time.Advance(TimeSpan.FromDays(31));
        using var refresh = await api.RefreshAsync(guest.RefreshToken, ct);

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Web_clients_get_the_refresh_token_only_as_an_httpOnly_cookie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);

        using var create = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/guest", UriKind.Relative));
        create.Headers.Add(AuthHeaders.Client, AuthHeaders.WebClient);
        using var created = await api.Client.SendAsync(create, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await created.Content.ReadFromJsonAsync<GuestCreatedResponse>(ct)).ShouldNotBeNull();
        body.RefreshToken.ShouldBeNull();
        var cookie = created.Headers.GetValues("Set-Cookie").ShouldHaveSingleItem();
        cookie.ShouldStartWith(AuthHeaders.RefreshCookie + "=gvr_");
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/auth", Case.Insensitive);

        using var refresh = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/refresh", UriKind.Relative));
        refresh.Headers.Add(AuthHeaders.Client, AuthHeaders.WebClient);
        refresh.Headers.Add("Cookie", cookie.Split(';')[0]);
        using var refreshed = await api.Client.SendAsync(refresh, ct);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await refreshed.Content.ReadFromJsonAsync<AuthTokensResponse>(ct)).ShouldNotBeNull().RefreshToken.ShouldBeNull();
    }

    [Fact]
    public async Task A_malformed_guest_login_returns_field_errors()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);

        using var response = await api.GuestLoginAsync("not-a-guid", "", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var errors = json.RootElement.GetProperty("errors");
        errors.TryGetProperty("GuestId", out _).ShouldBeTrue();
        errors.TryGetProperty("DeviceSecret", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task The_eleventh_sign_in_attempt_in_a_minute_is_rate_limited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        for (var i = 0; i < 10; i++)
        {
            using var attempt = await api.GuestLoginAsync(guest.UserId, "gvd_wrong", ct);
            attempt.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var limited = await api.GuestLoginAsync(guest.UserId, guest.DeviceSecret, ct);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Only_configured_browser_origins_may_call_with_credentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack, new Dictionary<string, string?> { ["GameVerse:CorsOrigins:0"] = "https://gameverse.app" });

        async Task<HttpResponseMessage> PreflightAsync(string origin)
        {
            using var preflight = new HttpRequestMessage(HttpMethod.Options, new Uri("/auth/refresh", UriKind.Relative));
            preflight.Headers.Add("Origin", origin);
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-gameverse-client");
            return await api.Client.SendAsync(preflight, ct);
        }

        using var allowed = await PreflightAsync("https://gameverse.app");
        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldHaveSingleItem().ShouldBe("https://gameverse.app");
        allowed.Headers.GetValues("Access-Control-Allow-Credentials").ShouldHaveSingleItem().ShouldBe("true");

        using var other = await PreflightAsync("https://evil.example");
        other.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private static async Task<string> ProblemTextAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("title").GetString() + " | " + json.RootElement.GetProperty("detail").GetString();
    }
}
