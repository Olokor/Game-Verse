using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameVerse.Api.IntegrationTests.Accounts;

/// <summary>Reads mail the API sent, from the Mailpit test container's HTTP API.</summary>
internal sealed partial class Mailbox(StackFixture stack) : IDisposable
{
    private readonly HttpClient http = new() { BaseAddress = stack.MailpitApi };

    /// <summary>A fresh address per test, so tests running in parallel never read each other's mail.</summary>
    public static string NewAddress() => $"player-{Guid.NewGuid():N}@example.test";

    /// <summary>Waits for mail to <paramref name="to"/> whose subject contains <paramref name="subjectPart"/>; returns its text.</summary>
    public async Task<(string Subject, string Text)> WaitForAsync(string to, string subjectPart, CancellationToken ct, int expectedCount = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var messages = await SearchAsync(to, ct);
            var matching = messages.Where(m => m.Subject.Contains(subjectPart, StringComparison.Ordinal)).ToList();
            if (matching.Count >= expectedCount)
            {
                var latest = matching[0]; // Mailpit lists newest first
                using var message = JsonDocument.Parse(await http.GetStringAsync(new Uri($"api/v1/message/{latest.Id}", UriKind.Relative), ct));
                return (latest.Subject, message.RootElement.GetProperty("Text").GetString() ?? string.Empty);
            }

            await Task.Delay(200, ct);
        }

        throw new TimeoutException($"No mail to {to} with \"{subjectPart}\" within 15 s");
    }

    public async Task<int> CountAsync(string to, CancellationToken ct) => (await SearchAsync(to, ct)).Count;

    public static string CodeIn(string text)
    {
        var code = SixDigits().Match(text).Value;
        code.ShouldNotBeNullOrEmpty("the email should contain a 6-digit code");
        return code;
    }

    public void Dispose() => http.Dispose();

    private async Task<List<(string Id, string Subject)>> SearchAsync(string to, CancellationToken ct)
    {
        var query = Uri.EscapeDataString($"to:\"{to}\"");
        using var json = JsonDocument.Parse(await http.GetStringAsync(new Uri($"api/v1/search?query={query}", UriKind.Relative), ct));
        return json.RootElement.GetProperty("messages").EnumerateArray()
            .Select(m => (m.GetProperty("ID").GetString()!, m.GetProperty("Subject").GetString() ?? string.Empty))
            .ToList();
    }

    [GeneratedRegex(@"\b[0-9]{6}\b")]
    private static partial Regex SixDigits();
}
