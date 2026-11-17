#nullable enable
using System;
using System.Collections.Concurrent;
using System.Text;

namespace GameVerse.Net.Realtime
{
    /// <summary>What happened on a socket.</summary>
    public enum SocketEventKind
    {
        Opened,
        Text,
        Closed,
    }

    public readonly struct SocketEvent
    {
        public SocketEvent(SocketEventKind kind, string? text = null, int closeCode = 0)
        {
            Kind = kind;
            Text = text;
            CloseCode = closeCode;
        }

        public SocketEventKind Kind { get; }

        public string? Text { get; }

        public int CloseCode { get; }
    }

    /// <summary>
    /// A WebSocket seen from the main thread only: events are queued and handed out by <see cref="TryDequeue"/>, so the
    /// connection logic never runs on a socket thread. Tests use a fake; devices use <see cref="NativeSocket"/>.
    /// </summary>
    public interface ISocket : IDisposable
    {
        void Connect(string url);

        void Send(string text);

        void Close();

        /// <summary>Moves whatever the socket received onto the queue (NativeWebSocket needs this every frame).</summary>
        void Pump();

        bool TryDequeue(out SocketEvent socketEvent);
    }

    /// <summary>NativeWebSocket (Android, iOS, desktop and Web builds alike), wrapped as an <see cref="ISocket"/>.</summary>
    public sealed class NativeSocket : ISocket
    {
        private readonly ConcurrentQueue<SocketEvent> events = new ConcurrentQueue<SocketEvent>();
        private NativeWebSocket.WebSocket? socket;
        private bool closedReported;

        public void Connect(string url)
        {
            socket = new NativeWebSocket.WebSocket(url);
            socket.OnOpen += () => events.Enqueue(new SocketEvent(SocketEventKind.Opened));
            socket.OnMessage += bytes => events.Enqueue(new SocketEvent(SocketEventKind.Text, Encoding.UTF8.GetString(bytes)));
            socket.OnError += _ => ReportClosed(1006);
            socket.OnClose += code => ReportClosed((int)code);

            // Connect() completes when the socket closes; its failures are reported through OnError/OnClose.
            _ = socket.Connect().ContinueWith(task => ReportClosed(1006), System.Threading.Tasks.TaskScheduler.Default);
        }

        public void Send(string text)
        {
            if (socket?.State == NativeWebSocket.WebSocketState.Open)
            {
                _ = socket.SendText(text);
            }
        }

        public void Close()
        {
            if (socket != null && socket.State is NativeWebSocket.WebSocketState.Open or NativeWebSocket.WebSocketState.Connecting)
            {
                _ = socket.Close();
            }
        }

        public void Pump()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            socket?.DispatchMessageQueue();
#endif
        }

        public bool TryDequeue(out SocketEvent socketEvent) => events.TryDequeue(out socketEvent);

        public void Dispose() => Close();

        private void ReportClosed(int code)
        {
            lock (events)
            {
                if (closedReported)
                {
                    return; // error, close and the end of Connect() can all report the same close
                }

                closedReported = true;
            }

            events.Enqueue(new SocketEvent(SocketEventKind.Closed, closeCode: code));
        }
    }
}
