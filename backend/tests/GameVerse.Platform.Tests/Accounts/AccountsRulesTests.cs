using GameVerse.Platform.Accounts;

namespace GameVerse.Platform.Tests.Accounts;

public class RefreshTokenRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Future = Now.AddDays(10);

    [Fact]
    public void An_unused_unexpired_token_rotates() =>
        RefreshTokenRules.Evaluate(null, null, Future, Now, Grace).ShouldBe(RefreshVerdict.Rotate);

    [Fact]
    public void A_revoked_token_is_revoked_even_if_otherwise_valid() =>
        RefreshTokenRules.Evaluate(null, Now.AddMinutes(-1), Future, Now, Grace).ShouldBe(RefreshVerdict.Revoked);

    [Fact]
    public void Revocation_wins_over_reuse() =>
        RefreshTokenRules.Evaluate(Now.AddHours(-1), Now.AddMinutes(-1), Future, Now, Grace).ShouldBe(RefreshVerdict.Revoked);

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void A_token_used_within_the_grace_window_is_a_retry(int secondsAgo) =>
        RefreshTokenRules.Evaluate(Now.AddSeconds(-secondsAgo), null, Future, Now, Grace).ShouldBe(RefreshVerdict.RetryWithinGrace);

    [Fact]
    public void A_token_used_before_the_grace_window_is_reuse() =>
        RefreshTokenRules.Evaluate(Now.AddSeconds(-16), null, Future, Now, Grace).ShouldBe(RefreshVerdict.Reuse);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_token_at_or_past_its_expiry_is_expired(int secondsFromNow) =>
        RefreshTokenRules.Evaluate(null, null, Now.AddSeconds(secondsFromNow), Now, Grace).ShouldBe(RefreshVerdict.Expired);
}

public class AgeRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, true)] // no birth year (guest): treated as a minor
    [InlineData(2015, true)]
    [InlineData(2008, true)] // turns 18 during 2026: may still be 17, so the stricter setting applies
    [InlineData(2007, false)]
    [InlineData(1990, false)]
    public void Minors_are_decided_conservatively_from_the_birth_year(int? birthYear, bool minor) =>
        AgeRule.IsMinor(birthYear, Now).ShouldBe(minor);
}

public class AuthSecretsTests
{
    [Fact]
    public void Secrets_are_prefixed_unique_and_carry_256_bits()
    {
        var a = AuthSecrets.New(AuthSecrets.RefreshPrefix);
        var b = AuthSecrets.New(AuthSecrets.RefreshPrefix);

        a.ShouldStartWith("gvr_");
        a.ShouldNotBe(b);
        a.Length.ShouldBe("gvr_".Length + 43); // 32 bytes as unpadded base64url
    }

    [Fact]
    public void A_secret_matches_only_its_own_hash()
    {
        var secret = AuthSecrets.New(AuthSecrets.DevicePrefix);
        var hash = AuthSecrets.Hash(secret);

        hash.Length.ShouldBe(32);
        AuthSecrets.Matches(secret, hash).ShouldBeTrue();
        AuthSecrets.Matches(secret + "x", hash).ShouldBeFalse();
        AuthSecrets.Matches(AuthSecrets.New(AuthSecrets.DevicePrefix), hash).ShouldBeFalse();
    }
}
