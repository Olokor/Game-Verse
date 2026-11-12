using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameVerse.Contracts.Accounts;

namespace GameVerse.Api.IntegrationTests.Accounts;

/// <summary>Profile basics and in-app account deletion (Prompt 1.1, ADR-0006).</summary>
public class ProfileAndDeletionTests(StackFixture stack)
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task A_new_guest_has_a_default_profile_that_it_can_change()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        var profile = await GetProfileAsync(api, guest.AccessToken, ct);
        profile.UserId.ShouldBe(guest.UserId);
        profile.DisplayName.ShouldMatch("^Player[0-9]{4}$");
        profile.AvatarId.ShouldBe(ProfileAvatars.Default);

        using var updated = await SendAsync(api, HttpMethod.Patch, "/profiles/me", guest.AccessToken, new UpdateProfileRequest { DisplayName = "Ada Okafor", AvatarId = "avatar_07" }, ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await GetProfileAsync(api, guest.AccessToken, ct);
        after.DisplayName.ShouldBe("Ada Okafor");
        after.AvatarId.ShouldBe("avatar_07");

        using var nameOnly = await SendAsync(api, HttpMethod.Patch, "/profiles/me", guest.AccessToken, new UpdateProfileRequest { DisplayName = "Ada_99" }, ct);
        (await nameOnly.Content.ReadFromJsonAsync<ProfileResponse>(ct)).ShouldNotBeNull().AvatarId.ShouldBe("avatar_07", "fields left null stay");
    }

    [Theory]
    [InlineData("ab")] // too short
    [InlineData("a name that is far too long")]
    [InlineData(" leading")]
    [InlineData("double  space")]
    [InlineData("no<html>")]
    public async Task Invalid_display_names_are_rejected_with_a_field_error(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var response = await SendAsync(api, HttpMethod.Patch, "/profiles/me", guest.AccessToken, new UpdateProfileRequest { DisplayName = name }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        json.RootElement.GetProperty("errors").TryGetProperty("DisplayName", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task An_unknown_avatar_is_rejected_and_profiles_need_a_token()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var avatar = await SendAsync(api, HttpMethod.Patch, "/profiles/me", guest.AccessToken, new UpdateProfileRequest { AvatarId = "avatar_999" }, ct);
        avatar.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var anonymous = await SendAsync<object?>(api, HttpMethod.Get, "/profiles/me", null, null, ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upgrading_keeps_the_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);
        using var renamed = await SendAsync(api, HttpMethod.Patch, "/profiles/me", guest.AccessToken, new UpdateProfileRequest { DisplayName = "Kept Name" }, ct);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var upgraded = await SendAsync(api, HttpMethod.Post, "/auth/upgrade", guest.AccessToken, new UpgradeRequest { Email = Mailbox.NewAddress(), Password = Password, BirthYear = 1990 }, ct);
        var tokens = await AccountsApi.TokensAsync(upgraded, ct);

        (await GetProfileAsync(api, tokens.AccessToken, ct)).DisplayName.ShouldBe("Kept Name");
    }

    [Fact]
    public async Task A_guest_deletes_its_account_and_can_never_sign_in_again()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var deleted = await SendAsync(api, HttpMethod.Post, "/accounts/me/delete", guest.AccessToken, new DeleteAccountRequest(), ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var device = await api.GuestLoginAsync(guest.UserId, guest.DeviceSecret, ct);
        device.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var refresh = await api.RefreshAsync(guest.RefreshToken, ct);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var me = await api.MeAsync(guest.AccessToken, ct);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the access token outlives the account by at most 15 minutes, but the account is gone");
        (await GetProfileAsync(api, guest.AccessToken, ct)).DisplayName.ShouldBe("Deleted player");
    }

    [Fact]
    public async Task An_email_account_confirms_deletion_with_its_password_and_its_email_is_freed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack, new Dictionary<string, string?> { ["GameVerse:Auth:RateLimits:CreatePerMinute"] = "50" });
        var guest = await api.CreateGuestAsync(ct);
        var email = Mailbox.NewAddress();
        using var upgraded = await SendAsync(api, HttpMethod.Post, "/auth/upgrade", guest.AccessToken, new UpgradeRequest { Email = email, Password = Password, BirthYear = 1990 }, ct);
        var tokens = await AccountsApi.TokensAsync(upgraded, ct);

        using var wrong = await SendAsync(api, HttpMethod.Post, "/accounts/me/delete", tokens.AccessToken, new DeleteAccountRequest { Password = "not the password" }, ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var deleted = await SendAsync(api, HttpMethod.Post, "/accounts/me/delete", tokens.AccessToken, new DeleteAccountRequest { Password = Password }, ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var login = await SendAsync(api, HttpMethod.Post, "/auth/login", null, new LoginRequest { Email = email, Password = Password }, ct);
        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The address is free again: a new player can take it.
        var next = await api.CreateGuestAsync(ct);
        using var reuse = await SendAsync(api, HttpMethod.Post, "/auth/upgrade", next.AccessToken, new UpgradeRequest { Email = email, Password = Password, BirthYear = 1990 }, ct);
        (await AccountsApi.TokensAsync(reuse, ct)).UserId.ShouldNotBe(guest.UserId);
    }

    private static async Task<ProfileResponse> GetProfileAsync(AccountsApi api, string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync<object?>(api, HttpMethod.Get, "/profiles/me", accessToken, null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ProfileResponse>(ct)).ShouldNotBeNull();
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(AccountsApi api, HttpMethod method, string path, string? accessToken, T body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await api.Client.SendAsync(request, ct);
    }
}
