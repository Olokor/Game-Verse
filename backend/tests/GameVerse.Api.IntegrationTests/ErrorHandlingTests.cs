using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;

namespace GameVerse.Api.IntegrationTests;

/// <summary>
/// Global error handling and error reporting (ADR-0005): an unhandled exception becomes problem+json without any
/// exception detail, and, when a DSN is configured, exactly one event reaches the Sentry transport.
/// </summary>
public class ErrorHandlingTests(StackFixture stack)
{
    private const string ThrowPath = "/__test/throw";
    private const string SecretDetail = "boom-secret-detail-7f3a";

    /// <summary>
    /// A transport that records the JSON of each event exactly as it would be sent to GlitchTip, instead of sending it.
    /// </summary>
    private sealed class RecordingTransport : ITransport
    {
        public ConcurrentQueue<JsonElement> Events { get; } = new();

        public async Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            foreach (var item in envelope.Items)
            {
                if (item.TryGetType() != "event")
                {
                    continue;
                }

                using var stream = new MemoryStream();
                await item.Payload.SerializeAsync(stream, null, cancellationToken);
                Events.Enqueue(JsonDocument.Parse(stream.ToArray()).RootElement.Clone());
            }
        }
    }

    /// <summary>
    /// Adds a test-only throwing endpoint after the app's own pipeline, so the exception travels through the real
    /// exception handler. Production has no such route.
    /// </summary>
    private sealed class ThrowingEndpointFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ThrowPath, branch => branch.Run(http => throw new InvalidOperationException(
                http.Request.Path.HasValue && http.Request.Path.Value!.Length > 1 ? "e2e" + http.Request.Path.Value.Replace('/', '-') : SecretDetail)));
        };
    }

    private GameVerseApiFactory Factory(string? dsn = null, RecordingTransport? transport = null) => new(
        stack,
        new Dictionary<string, string?> { ["GameVerse:ErrorReporting:Dsn"] = dsn ?? "" },
        builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, ThrowingEndpointFilter>();
            if (transport is not null)
            {
                services.PostConfigure<SentryAspNetCoreOptions>(options => options.Transport = transport);
            }
        }));

    [Fact]
    public async Task Unhandled_exception_returns_problem_details_without_exception_details()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(ThrowPath, UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(500);
        json.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        body.ShouldNotContain(SecretDetail);
        body.ShouldNotContain(nameof(InvalidOperationException));
        body.ShouldNotContain(" at ");
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/no-such-route", UriKind.Relative), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task With_a_dsn_an_unhandled_exception_is_reported_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new RecordingTransport();
        // Nothing listens on port 9; the recording transport replaces the network anyway.
        await using var factory = Factory("http://0123456789abcdef0123456789abcdef@127.0.0.1:9/1", transport);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ThrowPath + "?ticket=tkt-secret-91", UriKind.Relative));
        request.Headers.Add("X-Player-Secret", "hdr-secret-42");
        request.Headers.Add("Cookie", "session=cookie-secret-17");
        using var response = await client.SendAsync(request, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (transport.Events.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, ct);
        }

        await Task.Delay(1000, ct); // give any duplicate time to arrive
        // The Sentry SDK keeps process-wide state, so while this host has it on it also sees failures other test hosts
        // trigger in parallel (startup errors by design). Count only events about this request: the exception itself, or
        // anything mentioning its route, such as a 500 access-log line, which must not become a second event.
        var reported = transport.Events
            .Where(e => e.GetRawText().Contains(SecretDetail, StringComparison.Ordinal) || e.GetRawText().Contains(ThrowPath, StringComparison.Ordinal))
            .ToArray();
        reported.Length.ShouldBe(1);
        var exception = reported[0].GetProperty("exception").GetProperty("values")[0];
        exception.GetProperty("type").GetString().ShouldBe(typeof(InvalidOperationException).FullName);
        exception.GetProperty("value").GetString().ShouldBe(SecretDetail);

        // Privacy (ADR-0005): no client IP, headers, cookies or query string leave the server; the route does.
        var raw = reported[0].GetRawText();
        raw.ShouldNotContain("ip_address");
        raw.ShouldNotContain("tkt-secret-91");
        raw.ShouldNotContain("hdr-secret-42");
        raw.ShouldNotContain("cookie-secret-17");
        reported[0].GetProperty("request").GetProperty("url").GetString().ShouldEndWith(ThrowPath);
    }

    [Fact]
    public async Task Without_a_dsn_no_sentry_hub_is_registered()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        factory.Services.GetService<IHub>().ShouldBeNull();
    }

    [Fact]
    public void Malformed_dsn_fails_startup_with_a_clear_message()
    {
        using var factory = Factory("not-a-dsn");

        Should.Throw<InvalidOperationException>(() => factory.CreateClient()).Message.ShouldContain("GameVerse:ErrorReporting:Dsn");
    }

    /// <summary>
    /// Opt-in end-to-end check against a running GlitchTip (`make errors-up`, `make errors-setup`). Skipped unless
    /// GAMEVERSE_E2E_GLITCHTIP_DSN (api project DSN with host 127.0.0.1:8000) and GLITCHTIP_API_TOKEN are set.
    /// </summary>
    [Fact]
    public async Task Reaches_a_real_glitchtip_when_configured()
    {
        var dsn = Environment.GetEnvironmentVariable("GAMEVERSE_E2E_GLITCHTIP_DSN");
        var token = Environment.GetEnvironmentVariable("GLITCHTIP_API_TOKEN");
        Assert.SkipWhen(string.IsNullOrEmpty(dsn) || string.IsNullOrEmpty(token), "Set GAMEVERSE_E2E_GLITCHTIP_DSN and GLITCHTIP_API_TOKEN to run.");

        var ct = TestContext.Current.CancellationToken;
        var marker = Guid.NewGuid().ToString("N")[..12];
        await using (var factory = Factory(dsn))
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync(new Uri($"{ThrowPath}/{marker}", UriKind.Relative), ct);
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        } // disposing the host flushes the Sentry queue

        var glitchtip = new Uri(new Uri(dsn!).GetLeftPart(UriPartial.Authority));
        using var api = new HttpClient { BaseAddress = glitchtip };
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var found = false;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!found && DateTime.UtcNow < deadline)
        {
            var issues = await api.GetStringAsync(new Uri("/api/0/projects/gameverse/api/issues/?query=" + marker, UriKind.Relative), ct);
            found = issues.Contains(marker, StringComparison.Ordinal);
            if (!found)
            {
                await Task.Delay(1000, ct);
            }
        }

        found.ShouldBeTrue($"issue e2e-{marker} did not appear in GlitchTip within 60 s");
    }
}
