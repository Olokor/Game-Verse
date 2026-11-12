using System.Collections.Generic;

namespace GameVerse.Contracts.Accounts
{
    /// <summary>A player's public profile basics (Prompt 1.1). Frames, titles and stats come with Progression.</summary>
    public class ProfileResponse
    {
        /// <summary>Gets or sets the account id (UUID).</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Gets or sets the display name shown to other players.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Gets or sets the avatar id, one of <see cref="ProfileAvatars.All"/>.</summary>
        public string AvatarId { get; set; } = string.Empty;
    }

    /// <summary>Changes the signed-in player's profile. Fields left null stay as they are.</summary>
    public class UpdateProfileRequest
    {
        /// <summary>Gets or sets the new display name: 3 to 20 letters, digits, spaces or underscores.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Gets or sets the new avatar id, one of <see cref="ProfileAvatars.All"/>.</summary>
        public string? AvatarId { get; set; }
    }

    /// <summary>Deletes the signed-in account. Email accounts confirm with their password; guests leave it empty.</summary>
    public class DeleteAccountRequest
    {
        /// <summary>Gets or sets the current password (email accounts only).</summary>
        public string? Password { get; set; }
    }

    /// <summary>
    /// The avatars a player can pick (artwork per id lives in the clients). TODO(gameverse): more avatars, and
    /// unlockable ones, arrive with the cosmetics inventory (Economy, Prompt 2.2).
    /// </summary>
    public static class ProfileAvatars
    {
        /// <summary>The avatar every new account starts with.</summary>
        public const string Default = "avatar_01";

        /// <summary>Gets every selectable avatar id.</summary>
        public static IReadOnlyList<string> All { get; } = new[]
        {
            "avatar_01", "avatar_02", "avatar_03", "avatar_04", "avatar_05", "avatar_06",
            "avatar_07", "avatar_08", "avatar_09", "avatar_10", "avatar_11", "avatar_12",
        };
    }
}
