using GameVerse.Platform.Diagnostics;
using NUnit.Framework;
using Sentry;
using Sentry.Unity;
using UnityEditor;

namespace GameVerse.Tests
{
    /// <summary>
    /// Error reporting settings (ADR-0005): the committed Sentry options asset never carries a DSN, collects no personal
    /// data and routes through the GameVerse configuration hook, which strips the user block.
    /// </summary>
    public class ErrorReportingTests
    {
        private const string OptionsPath = "Assets/Resources/Sentry/SentryOptions.asset";

        private static ScriptableSentryUnityOptions LoadOptions()
        {
            var options = AssetDatabase.LoadAssetAtPath<ScriptableSentryUnityOptions>(OptionsPath);
            Assert.That(options, Is.Not.Null, "Sentry options missing. Run `make unity-setup`.");
            return options;
        }

        [Test]
        public void Committed_options_carry_no_dsn()
        {
            Assert.That(LoadOptions().Dsn, Is.Null.Or.Empty, "BuildScript injects UNITY_ERROR_DSN only during a build");
        }

        [Test]
        public void Options_collect_no_personal_data()
        {
            var options = LoadOptions();
            Assert.That(options.SendDefaultPii, Is.False);
            Assert.That(options.AttachScreenshot, Is.False, "screenshots can show player names and chat");
            Assert.That(options.AttachViewHierarchy, Is.False);
            Assert.That(options.CaptureInEditor, Is.False);
            Assert.That(options.TracesSampleRate, Is.EqualTo(0));
        }

        [Test]
        public void Options_use_the_gameverse_configuration_hook()
        {
            Assert.That(LoadOptions().OptionsConfiguration, Is.InstanceOf<GameVerseSentryConfiguration>());
        }

        [Test]
        public void Scrub_removes_the_user_block()
        {
            var sentryEvent = new SentryEvent { User = new SentryUser { Id = "install-guid-123", Username = "Wisdom", IpAddress = "1.2.3.4" } };
            var scrubbed = GameVerseSentryConfiguration.Scrub(sentryEvent);
            Assert.That(scrubbed.User.Id, Is.Null);
            Assert.That(scrubbed.User.Username, Is.Null);
            Assert.That(scrubbed.User.IpAddress, Is.Null);
        }

        [Test]
        public void Self_check_reports_disabled_in_the_editor()
        {
            // CaptureInEditor is off, so the SDK is not running in EditMode.
            Assert.That(ErrorReportingSelfCheck.Run(), Is.EqualTo(ErrorReportingSelfCheck.LogPrefix + " disabled"));
        }
    }
}
