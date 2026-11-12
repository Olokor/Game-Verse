using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameVerse.Contracts.Accounts;

namespace GameVerse.Api.IntegrationTests.Accounts;

/// <summary>Upgrade to an email account, verification, email sign-in and password reset (Prompt 1.1, ADR-0006).</summary>
public class EmailAccountTests(StackFixture stack)
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task Upgrading_keeps_the_account_id_mails_a_code_and_verifying_confirms_the_email()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        using var mail = new Mailbox(stack);
        var guest = await api.CreateGuestAsync(ct);
        var email = Mailbox.NewAddress();

        using var upgraded = await PostAsync(api, "/auth/upgrade", guest.AccessToken, new UpgradeRequest { Email = email, Password = Password, BirthYear = 1990 }, ct);
        var tokens = await AccountsApi.TokensAsync(upgraded, ct);
        tokens.UserId.ShouldBe(guest.UserId, "same account: progress stays attached");
        tokens.IsGuest.ShouldBeFalse();

        var account = await MeAsync(api, tokens.AccessToken, ct);
        account.Email.ShouldBe(email);
        account.EmailVerified.ShouldBeFalse();
        account.IsMinor.ShouldBeFalse();

        var (subject, text) = await mail.WaitForAsync(email, "verification code", ct);
        var code = Mailbox.CodeIn(text);
        subject.ShouldContain(code);
        using var verified = await PostAsync(api, "/auth/email/verify", tokens.AccessToken, new VerifyEmailRequest { Code = code }, ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MeAsync(api, tokens.AccessToken, ct)).EmailVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task After_upgrading_the_guest_device_secret_and_old_sessions_stop_working()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var upgraded = await PostAsync(api, "/auth/upgrade", guest.AccessToken, new UpgradeRequest { Email = Mailbox.NewAddress(), Password = Password, BirthYear = 1990 }, ct);
        await AccountsApi.TokensAsync(upgraded, ct);

        using var deviceLogin = await api.GuestLoginAsync(guest.UserId, guest.DeviceSecret, ct);
        deviceLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var oldSession = await api.RefreshAsync(guest.RefreshToken, ct);
        oldSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_young_birth_year_makes_the_account_a_minor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        var guest = await api.CreateGuestAsync(ct);

        using var upgraded = await PostAsync(api, "/auth/upgrade", guest.AccessToken, new UpgradeRequest { Email = Mailbox.NewAddress(), Password = Password, BirthYear = DateTime.UtcNow.Year - 12 }, ct);
        var tokens = await AccountsApi.TokensAsync(upgraded, ct);

        (await MeAsync(api, tokens.AccessToken, ct)).IsMinor.ShouldBeTrue();
    }

    [Fact]
    public async Task Upgrade_is_refused_for_a_taken_email_a_non_guest_bad_input_or_no_token()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack, new Dictionary<string, string?> { ["GameVerse:Auth:RateLimits:CreatePerMinute"] = "50" });
        var email = Mailbox.NewAddress();
        var first = await api.CreateGuestAsync(ct);
        using var taken = await PostAsync(api, "/auth/upgrade", first.AccessToken, new UpgradeRequest { Email = email, Password = Password, BirthYear = 1990 }, ct);
        var upgradedTokens = await AccountsApi.TokensAsync(taken, ct);

        var second = await api.CreateGuestAsync(ct);
        using var duplicate = await PostAsync(api, "/auth/upgrade", second.AccessToken, new UpgradeRequest { Email = email.ToUpperInvariant(), Password = Password, BirthYear = 1990 }, ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict, "emails compare case-insensitively");

        using var again = await PostAsync(api, "/auth/upgrade", upgradedTokens.AccessToken, new UpgradeRequest { Email = Mailbox.NewAddress(), Password = Password, BirthYear = 1990 }, ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var invalid = await PostAsync(api, "/auth/upgrade", second.AccessToken, new UpgradeRequest { Email = "not-an-email", Password = "short", BirthYear = 1800 }, ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var errors = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync(ct));
        foreach (var field in new[] { "Email", "Password", "BirthYear" })
        {
            errors.RootElement.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }

        using var anonymous = await PostAsync(api, "/auth/upgrade", null, new UpgradeRequest { Email = Mailbox.NewAddress(), Password = Password, BirthYear = 1990 }, ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_codes_are_rejected_and_five_misses_burn_the_code()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack, new Dictionary<string, string?> { ["GameVerse:Auth:RateLimits:SignInPerMinute"] = "50" });
        using var mail = new Mailbox(stack);
        var (tokens, email) = await UpgradedAsync(api, ct);
        var code = Mailbox.CodeIn((await mail.WaitForAsync(email, "verification code", ct)).Text);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            using var miss = await PostAsync(api, "/auth/email/verify", tokens.AccessToken, new VerifyEmailRequest { Code = wrong }, ct);
            miss.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var tooLate = await PostAsync(api, "/auth/email/verify", tokens.AccessToken, new VerifyEmailRequest { Code = code }, ct);
        tooLate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLate.Content.ReadAsStringAsync(ct)).ShouldContain("Ask for a new code");
        (await MeAsync(api, tokens.AccessToken, ct)).EmailVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task A_code_expires_after_15_minutes_and_resend_issues_a_new_one()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        using var mail = new Mailbox(stack);
        var (tokens, email) = await UpgradedAsync(api, ct);
        var code = Mailbox.CodeIn((await mail.WaitForAsync(email, "verification code", ct)).Text);

        // Only the server's clock moves (codes, sessions); the JWT handler checks access tokens against the real clock,
        // so the access token stays usable while the 15-minute code expires.
        api.Time.Advance(TimeSpan.FromMinutes(16));
        using var expired = await PostAsync(api, "/auth/email/verify", tokens.AccessToken, new VerifyEmailRequest { Code = code }, ct);
        expired.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await expired.Content.ReadAsStringAsync(ct)).ShouldContain("expired");

        using var resend = await PostAsync(api, "/auth/email/resend", tokens.AccessToken, new { }, ct);
        resend.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var (_, text) = await mail.WaitForAsync(email, "verification code", ct, expectedCount: 2);
        using var verified = await PostAsync(api, "/auth/email/verify", tokens.AccessToken, new VerifyEmailRequest { Code = Mailbox.CodeIn(text) }, ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Email_sign_in_works_and_five_wrong_passwords_lock_the_account()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack, new Dictionary<string, string?> { ["GameVerse:Auth:RateLimits:SignInPerMinute"] = "50" });
        var (_, email) = await UpgradedAsync(api, ct);

        using var ok = await LoginAsync(api, email, Password, ct);
        await AccountsApi.TokensAsync(ok, ct);
        using var unknown = await LoginAsync(api, Mailbox.NewAddress(), Password, ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        for (var i = 0; i < 5; i++)
        {
            using var wrong = await LoginAsync(api, email, "wrong password!", ct);
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var locked = await LoginAsync(api, email, Password, ct);
        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "locked out even with the right password");
    }

    [Fact]
    public async Task Forgot_password_is_silent_for_unknown_emails_and_reset_signs_out_everywhere()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AccountsApi.Start(stack);
        using var mail = new Mailbox(stack);
        var (tokens, email) = await UpgradedAsync(api, ct);

        var stranger = Mailbox.NewAddress();
        using var unknown = await PostAsync(api, "/auth/password/forgot", null, new ForgotPasswordRequest { Email = stranger }, ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var forgot = await PostAsync(api, "/auth/password/forgot", null, new ForgotPasswordRequest { Email = email }, ct);
        forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var code = Mailbox.CodeIn((await mail.WaitForAsync(email, "password reset code", ct)).Text);
        (await mail.CountAsync(stranger, ct)).ShouldBe(0);

        const string newPassword = "a brand new passphrase";
        using var reset = await PostAsync(api, "/auth/password/reset", null, new ResetPasswordRequest { Email = email, Code = code, NewPassword = newPassword }, ct);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var oldSession = await api.RefreshAsync(tokens.RefreshToken, ct);
        oldSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "a reset revokes every session");
        using var oldPassword = await LoginAsync(api, email, Password, ct);
        oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var newLogin = await LoginAsync(api, email, newPassword, ct);
        await AccountsApi.TokensAsync(newLogin, ct);
    }

    private static async Task<(AuthTokensResponse Tokens, string Email)> UpgradedAsync(AccountsApi api, CancellationToken ct)
    {
        var guest = await api.CreateGuestAsync(ct);
        var email = Mailbox.NewAddress();
        using var upgraded = await PostAsync(api, "/auth/upgrade", guest.AccessToken, new UpgradeRequest { Email = email, Password = Password, BirthYear = 1990 }, ct);
        return (await AccountsApi.TokensAsync(upgraded, ct), email);
    }

    private static Task<HttpResponseMessage> LoginAsync(AccountsApi api, string email, string password, CancellationToken ct) =>
        PostAsync(api, "/auth/login", null, new LoginRequest { Email = email, Password = password }, ct);

    private static async Task<AccountResponse> MeAsync(AccountsApi api, string accessToken, CancellationToken ct)
    {
        using var me = await api.MeAsync(accessToken, ct);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await me.Content.ReadFromJsonAsync<AccountResponse>(ct)).ShouldNotBeNull();
    }

    private static async Task<HttpResponseMessage> PostAsync<T>(AccountsApi api, string path, string? accessToken, T body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await api.Client.SendAsync(request, ct);
    }
}
