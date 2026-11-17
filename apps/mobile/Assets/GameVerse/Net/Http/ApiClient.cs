#nullable enable
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameVerse.Net.Http
{
    /// <summary>Supplies the bearer token and renews it when the server says it expired.</summary>
    public interface IAccessTokenSource
    {
        /// <summary>The current access token, or null when signed out.</summary>
        string? AccessToken { get; }

        /// <summary>Gets a new access token (refresh token, or the guest device secret). False when that failed.</summary>
        UniTask<bool> RenewAsync(CancellationToken ct);
    }

    /// <summary>
    /// JSON over HTTP to the GameVerse API with the Contracts DTOs and <see cref="GameVerseJson"/> (the server's exact
    /// wire format). Errors become <see cref="ApiException"/>; a 401 renews the token once and retries.
    /// </summary>
    public sealed class ApiClient
    {
        private readonly string baseUrl;
        private readonly IHttpTransport transport;
        private readonly IAccessTokenSource? tokens;

        public ApiClient(string baseUrl, IHttpTransport transport, IAccessTokenSource? tokens)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            this.transport = transport;
            this.tokens = tokens;
        }

        public string BaseUrl => baseUrl;

        public UniTask<T> GetAsync<T>(string path, CancellationToken ct = default) => SendAsync<T>("GET", path, null, ct);

        public UniTask<T> PostAsync<T>(string path, object? body, CancellationToken ct = default) => SendAsync<T>("POST", path, body, ct);

        public UniTask<T> PatchAsync<T>(string path, object? body, CancellationToken ct = default) => SendAsync<T>("PATCH", path, body, ct);

        public async UniTask PostAsync(string path, object? body, CancellationToken ct = default) => await SendRawAsync("POST", path, body, ct);

        public async UniTask DeleteAsync(string path, CancellationToken ct = default) => await SendRawAsync("DELETE", path, null, ct);

        public async UniTask<T> SendAsync<T>(string method, string path, object? body, CancellationToken ct)
        {
            var text = await SendRawAsync(method, path, body, ct);
            return GameVerseJson.Deserialize<T>(text);
        }

        private async UniTask<string> SendRawAsync(string method, string path, object? body, CancellationToken ct)
        {
            var json = body is null ? null : GameVerseJson.Serialize(body);
            var response = await transport.SendAsync(Build(method, path, json), ct);
            if (response.Status == 401 && tokens != null && await tokens.RenewAsync(ct))
            {
                response = await transport.SendAsync(Build(method, path, json), ct); // once, with the new token
            }

            if (response.Status == 0)
            {
                throw ApiException.Network(response.Body);
            }

            if (response.Status < 200 || response.Status >= 300)
            {
                throw ApiException.FromResponse(response.Status, response.Body);
            }

            return response.Body;
        }

        private HttpRequest Build(string method, string path, string? json)
        {
            var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
            if (tokens?.AccessToken is { } token)
            {
                headers["Authorization"] = "Bearer " + token;
            }

            return new HttpRequest(method, baseUrl + path, json, headers);
        }
    }
}
