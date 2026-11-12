using System.Collections.Generic;

namespace GameVerse.Contracts.Session
{
    /// <summary>
    /// What the platform hands a game when a session starts (CLAUDE.md 4.2). This base type has the
    /// fields the shell needs; <see cref="SessionManifest{TConfig, TState}"/> adds the game-specific parts.
    /// <para>Wire format is camelCase JSON. Plain properties so System.Text.Json and Newtonsoft both work without attributes.</para>
    /// </summary>
    public class SessionManifest
    {
        /// <summary>Gets or sets the catalog game id, e.g. "ludo".</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>Gets or sets the game build version the session was created for.</summary>
        public string GameVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the session id (UUID).</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Gets or sets the mode, one of <see cref="GameModes"/>.</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>Gets or sets the id of the player on this device.</summary>
        public string LocalPlayerId { get; set; } = string.Empty;

        /// <summary>Gets or sets the seated players.</summary>
        public List<PlayerSlot> Players { get; set; } = new List<PlayerSlot>();

        /// <summary>Gets or sets realtime connection details; null for local, vs AI and solo modes.</summary>
        public RealtimeInfo? Realtime { get; set; }
    }

    /// <summary>The full manifest a game module reads, with its own config and state types.</summary>
    /// <typeparam name="TConfig">Game config, usually <see cref="SessionConfig{THouseRules}"/>.</typeparam>
    /// <typeparam name="TState">Game state (or the seat's view of it).</typeparam>
    public class SessionManifest<TConfig, TState> : SessionManifest
    {
        /// <summary>Gets or sets the variant, house rules and AI level.</summary>
        public TConfig Config { get; set; } = default!;

        /// <summary>Gets or sets the initial state; may be absent when the game builds it from the config.</summary>
        public TState? InitialState { get; set; }
    }

    /// <summary>The common config shape from CLAUDE.md 4.2.</summary>
    /// <typeparam name="THouseRules">The game's house-rule toggles.</typeparam>
    public class SessionConfig<THouseRules>
    {
        /// <summary>Gets or sets the variant id, e.g. "international_10x10".</summary>
        public string? Variant { get; set; }

        /// <summary>Gets or sets the house rules.</summary>
        public THouseRules HouseRules { get; set; } = default!;

        /// <summary>Gets or sets the AI difficulty for vs AI modes.</summary>
        public int? AiLevel { get; set; }
    }

    /// <summary>A seated player as the platform sees them.</summary>
    public class PlayerSlot
    {
        /// <summary>Gets or sets the player id (UUID).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the display name.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Gets or sets the zero-based seat.</summary>
        public int Seat { get; set; }

        /// <summary>Gets or sets equipped cosmetics, slot to cosmetic id (e.g. "diceSkin": "dice_neon").</summary>
        public Dictionary<string, string> Cosmetics { get; set; } = new Dictionary<string, string>();

        /// <summary>Gets or sets a value indicating whether an AI controls this seat.</summary>
        public bool IsAI { get; set; }
    }

    /// <summary>Where and how to open the realtime socket.</summary>
    public class RealtimeInfo
    {
        /// <summary>Gets or sets the WebSocket URL, e.g. wss://api.gameverse.app/ws/play/{sessionId}.</summary>
        public string WsUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the single-use ticket, valid for 30 seconds. Never the JWT.</summary>
        public string Ticket { get; set; } = string.Empty;
    }

    /// <summary>Allowed values for <see cref="SessionManifest.Mode"/>. Strings, not an enum, so both serializers agree without converters.</summary>
    public static class GameModes
    {
        /// <summary>Online, private room.</summary>
        public const string OnlinePrivate = "online_private";

        /// <summary>Online, ranked matchmaking.</summary>
        public const string OnlineRanked = "online_ranked";

        /// <summary>Pass and play on one device.</summary>
        public const string Local = "local";

        /// <summary>Against AI.</summary>
        public const string VsAI = "vs_ai";

        /// <summary>Single player.</summary>
        public const string Solo = "solo";
    }
}
