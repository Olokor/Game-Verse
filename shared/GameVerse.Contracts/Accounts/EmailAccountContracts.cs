namespace GameVerse.Contracts.Accounts
{
    /// <summary>
    /// Turns the signed-in guest into an email account. The account id stays the same, so everything the guest earned
    /// stays attached. Only the birth year is asked, to decide whether the player is a minor (ADR-0006).
    /// </summary>
    public class UpgradeRequest
    {
        /// <summary>Gets or sets the email address. A 6-digit verification code is sent to it.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Gets or sets the password: at least 10 characters, no other composition rules.</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Gets or sets the birth year (four digits).</summary>
        public int BirthYear { get; set; }
    }

    /// <summary>Signs in with email and password.</summary>
    public class LoginRequest
    {
        /// <summary>Gets or sets the email address.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Gets or sets the password.</summary>
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>Confirms the signed-in account's email with the 6-digit code that was mailed to it.</summary>
    public class VerifyEmailRequest
    {
        /// <summary>Gets or sets the 6-digit code.</summary>
        public string Code { get; set; } = string.Empty;
    }

    /// <summary>Asks for a password-reset code. Always accepted (202), whether or not the email has an account.</summary>
    public class ForgotPasswordRequest
    {
        /// <summary>Gets or sets the email address.</summary>
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>Sets a new password with the 6-digit code from the reset email. Signs out every session.</summary>
    public class ResetPasswordRequest
    {
        /// <summary>Gets or sets the email address.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Gets or sets the 6-digit code.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Gets or sets the new password (at least 10 characters).</summary>
        public string NewPassword { get; set; } = string.Empty;
    }
}
