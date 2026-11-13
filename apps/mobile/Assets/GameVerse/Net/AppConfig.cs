#nullable enable
using UnityEngine;

namespace GameVerse.Net
{
    /// <summary>
    /// Where the API lives, baked into the build. The committed asset (Resources/GameVerse/AppConfig) keeps the field
    /// empty; <c>BuildScript</c> writes GAMEVERSE_API_URL into it for the duration of a build and clears it again, the same
    /// way it handles the error-reporting DSN (ADR-0011). Empty means <see cref="DefaultApiBaseUrl"/>.
    /// </summary>
    public sealed class AppConfig : ScriptableObject
    {
        /// <summary>Resources path of the asset (without extension).</summary>
        public const string ResourcePath = "GameVerse/AppConfig";

        /// <summary>
        /// Development default. The editor reaches `make up` here directly; a USB phone reaches it through
        /// `adb reverse tcp:8080 tcp:8080` (make device-check sets that up).
        /// </summary>
        public const string DefaultApiBaseUrl = "http://127.0.0.1:8080";

        [SerializeField]
        [Tooltip("Base URL of the GameVerse API, e.g. https://api.gameverse.app. Leave empty in the repo: builds fill it.")]
        private string apiBaseUrl = string.Empty;

        /// <summary>Gets or sets the configured base URL (empty means the default). Set only by the build script.</summary>
        public string ApiBaseUrl
        {
            get => apiBaseUrl;
            set => apiBaseUrl = value ?? string.Empty;
        }

        /// <summary>The base URL to call, without a trailing slash.</summary>
        public static string ResolveApiBaseUrl() => Resolve(Resources.Load<AppConfig>(ResourcePath));

        /// <summary>The base URL for a given config asset (null or empty means the default).</summary>
        public static string Resolve(AppConfig? config)
        {
            var url = config != null && !string.IsNullOrWhiteSpace(config.apiBaseUrl) ? config.apiBaseUrl.Trim() : DefaultApiBaseUrl;
            return url.TrimEnd('/');
        }
    }
}
