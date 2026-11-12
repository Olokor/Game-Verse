using Sentry;
using Sentry.Unity;
using UnityEngine;

namespace GameVerse.Platform.Diagnostics
{
    /// <summary>
    /// Runtime part of error reporting (ADR-0005). The Sentry Unity SDK loads Resources/Sentry/SentryOptions.asset at
    /// startup and calls this hook (referenced from that asset by ProjectSetup) before initialising.
    /// Environment: development for development builds, production otherwise.
    /// Privacy: the SDK attaches a persistent per-install user id by default; GameVerse sends no user block at all until
    /// auth exists, and then only the internal player UUID.
    /// </summary>
    [CreateAssetMenu(fileName = "GameVerseSentryConfiguration", menuName = "GameVerse/Sentry Configuration")]
    public sealed class GameVerseSentryConfiguration : SentryOptionsConfiguration
    {
        public override void Configure(SentryUnityOptions options)
        {
            options.Environment = Debug.isDebugBuild ? "development" : "production";
            options.SendDefaultPii = false;
            options.SetBeforeSend((sentryEvent, _) => Scrub(sentryEvent));
        }

        /// <summary>Removes the user block. Public so the EditMode test can check it.</summary>
        public static SentryEvent Scrub(SentryEvent sentryEvent)
        {
            sentryEvent.User = new SentryUser();
            return sentryEvent;
        }
    }
}
