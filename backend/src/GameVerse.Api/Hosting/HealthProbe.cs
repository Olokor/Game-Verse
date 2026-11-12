namespace GameVerse.Api.Hosting;

/// <summary>
/// `dotnet GameVerse.Api.dll healthcheck`: calls this container's own /health and exits 0 or 1.
/// Lets the Docker HEALTHCHECK work without curl in the image.
/// </summary>
internal static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0];
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        try
        {
            using var response = await http.GetAsync(new Uri("http://127.0.0.1:" + port + "/health")).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
