#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameVerse.Net.Http;
using GameVerse.Net.Realtime;
using GameVerse.Platform.Session;

namespace GameVerse.Tests
{
    /// <summary>Answers HTTP requests from a handler and records them. Completes synchronously.</summary>
    internal sealed class FakeHttp : IHttpTransport
    {
        private readonly Func<HttpRequest, HttpResponse> handler;

        public FakeHttp(Func<HttpRequest, HttpResponse> handler) => this.handler = handler;

        public List<HttpRequest> Requests { get; } = new List<HttpRequest>();

        public UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return UniTask.FromResult(handler(request));
        }
    }

    /// <summary>A socket the test drives by hand: it decides when the socket opens, what arrives and when it closes.</summary>
    internal sealed class FakeSocket : ISocket
    {
        private readonly Queue<SocketEvent> events = new Queue<SocketEvent>();

        public string? Url { get; private set; }

        public List<string> Sent { get; } = new List<string>();

        public bool Closed { get; private set; }

        public void Connect(string url) => Url = url;

        public void Send(string text) => Sent.Add(text);

        public void Close() => Closed = true;

        public void Pump()
        {
        }

        public bool TryDequeue(out SocketEvent socketEvent) => events.TryDequeue(out socketEvent);

        public void Dispose() => Closed = true;

        public void Open() => events.Enqueue(new SocketEvent(SocketEventKind.Opened));

        public void Receive(string text) => events.Enqueue(new SocketEvent(SocketEventKind.Text, text));

        public void Drop() => events.Enqueue(new SocketEvent(SocketEventKind.Closed, closeCode: 1006));
    }

    /// <summary>An in-memory secure store.</summary>
    internal sealed class MemoryStore : ISecureStore
    {
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>();

        public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;

        public void Set(string key, string value) => Values[key] = value;

        public void Delete(string key) => Values.Remove(key);
    }
}
