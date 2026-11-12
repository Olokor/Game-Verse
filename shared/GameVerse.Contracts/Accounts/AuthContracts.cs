namespace GameVerse.Contracts.Accounts
{
    /// <summary>
    /// Tokens returned by every sign-in style call (ADR-0006). The access token is a short-lived JWT for the
    /// Authorization header. The refresh token is in this body for Unity and other clients; web clients
    /// (header <c>X-GameVerse-Client: web</c>) get it as an httpOnly cookie instead and see <c>null</c> here.
    /// </summary>
    public class AuthTokensResponse
    {
        /// <summary>Gets or sets the account id (UUID).</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the account is a guest (device-bound, no email).</summary>
        public bool IsGuest { get; set; }

        /// <summary>Gets or sets the access token (JWT, about 15 minutes).</summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>Gets or sets when the access token expires (ISO 8601, UTC).</summary>
        public string AccessTokenExpiresAt { get; set; } = string.Empty;

        /// <summary>Gets or sets the refresh token, or null when it was set as a cookie (web).</summary>
        public string? RefreshToken { get; set; }

        /// <summary>Gets or sets when the refresh token expires (ISO 8601, UTC).</summary>
        public string RefreshTokenExpiresAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response to creating a guest. <see cref="DeviceSecret"/> is shown exactly once; the client stores it in secure
    /// storage and signs back in with it (<see cref="GuestLoginRequest"/>). The server keeps only a hash.
    /// </summary>
    public class GuestCreatedResponse : AuthTokensResponse
    {
        /// <summary>Gets or sets the device secret. Never sent again.</summary>
        public string DeviceSecret { get; set; } = string.Empty;
    }

    /// <summary>Signs a guest back in on the device that created it.</summary>
    public class GuestLoginRequest
    {
        /// <summary>Gets or sets the guest's account id.</summary>
        public string GuestId { get; set; } = string.Empty;

        /// <summary>Gets or sets the device secret returned when the guest was created.</summary>
        public string DeviceSecret { get; set; } = string.Empty;
    }

    /// <summary>
    /// Exchanges a refresh token for new tokens (the old one stops working). Web clients leave
    /// <see cref="RefreshToken"/> empty; the cookie is used instead. Also the body of a logout.
    /// </summary>
    public class RefreshRequest
    {
        /// <summary>Gets or sets the refresh token, or null for web clients.</summary>
        public string? RefreshToken { get; set; }
    }

    /// <summary>The signed-in account.</summary>
    public class AccountResponse
    {
        /// <summary>Gets or sets the account id (UUID).</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the account is a guest.</summary>
        public bool IsGuest { get; set; }

        /// <summary>Gets or sets the email, or null for guests.</summary>
        public string? Email { get; set; }

        /// <summary>Gets or sets a value indicating whether the email has been verified.</summary>
        public bool EmailVerified { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the player counts as under 18 (guests always do, CLAUDE.md
        /// section 7: chat is off for minors).
        /// </summary>
        public bool IsMinor { get; set; }

        /// <summary>Gets or sets when the account was created (ISO 8601, UTC).</summary>
        public string CreatedAt { get; set; } = string.Empty;
    }

    /// <summary>Header values and names shared by clients and the API.</summary>
    public static class AuthHeaders
    {
        /// <summary>Request header naming the client kind.</summary>
        public const string Client = "X-GameVerse-Client";

        /// <summary><see cref="Client"/> value for the browser app: refresh token travels as a cookie.</summary>
        public const string WebClient = "web";

        /// <summary>Name of the refresh-token cookie for web clients.</summary>
        public const string RefreshCookie = "gv_refresh";
    }
}
