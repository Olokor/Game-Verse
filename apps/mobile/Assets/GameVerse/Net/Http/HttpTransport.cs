#nullable enable
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace GameVerse.Net.Http
{
    /// <summary>One HTTP request, transport-neutral so tests can answer it without a server.</summary>
    public sealed class HttpRequest
    {
        public HttpRequest(string method, string url, string? body, IReadOnlyDictionary<string, string> headers)
        {
            Method = method;
            Url = url;
            Body = body;
            Headers = headers;
        }

        public string Method { get; }

        public string Url { get; }

        public string? Body { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }
    }

    /// <summary>The answer: status 0 when there was none (network failure).</summary>
    public sealed class HttpResponse
    {
        public HttpResponse(int status, string body)
        {
            Status = status;
            Body = body;
        }

        public int Status { get; }

        public string Body { get; }
    }

    public interface IHttpTransport
    {
        UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken ct);
    }

    /// <summary>UnityWebRequest, which works the same on Android, iOS and Web builds.</summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        /// <summary>Seconds before a request gives up.</summary>
        public const int TimeoutSeconds = 15;

        public async UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken ct)
        {
            using var web = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            if (request.Body != null)
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body)) { contentType = "application/json" };
            }

            foreach (var header in request.Headers)
            {
                web.SetRequestHeader(header.Key, header.Value);
            }

            try
            {
                await web.SendWebRequest().WithCancellation(ct);
            }
            catch (UnityWebRequestException)
            {
                // Non-2xx statuses also land here; the result below tells them apart from a network failure.
            }

            return web.result == UnityWebRequest.Result.ConnectionError
                ? new HttpResponse(0, web.error ?? "connection error")
                : new HttpResponse((int)web.responseCode, web.downloadHandler?.text ?? string.Empty);
        }
    }
}
