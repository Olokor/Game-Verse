#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameVerse.Net.Realtime
{
    /// <summary>Where the realtime link is. The HUD shows a banner for anything but <see cref="Open"/>.</summary>
    public enum ConnectionStatus
    {
        Connecting,
        Open,
        Reconnecting,

        /// <summary>Closed for good: disposed, or the server no longer knows the session.</summary>
        Closed,
    }

    /// <summary>A <c>state</c> or <c>snapshot</c> from the server, already checked for staleness.</summary>
    public sealed class RealtimeState
    {
        public RealtimeState(string kind, long stateVersion, long? appliedSeq, int? turnSeat, long? turnDeadline, JToken state)
        {
            Kind = kind;
            StateVersion = stateVersion;
            AppliedSeq = appliedSeq;
            TurnSeat = turnSeat;
            TurnDeadline = turnDeadline;
            State = state;
        }

        /// <summary><see cref="MessageTypes.State"/> or <see cref="MessageTypes.Snapshot"/>.</summary>
        public string Kind { get; }

        public long StateVersion { get; }

        /// <summary>The seq of this client's action that produced the state, if it was this client's.</summary>
        public long? AppliedSeq { get; }

        public int? TurnSeat { get; }

        /// <summary>Unix milliseconds when the server plays for <see cref="TurnSeat"/>; null without a turn clock.</summary>
        public long? TurnDeadline { get; }

        /// <summary>The seat's view of the game, in the game's wire format.</summary>
        public JToken State { get; }
    }

    /// <summary>
    /// One player's realtime link to a session or a room lobby (CLAUDE.md 4.5, ADR-0009). Main-thread only: call
    /// <see cref="Tick"/> every frame. It answers pings, drops stale state versions, and on a lost connection fetches a fresh
    /// ticket and reconnects with backoff; actions not yet acknowledged are resent with their original seq, which the server
    /// never applies twice.
    /// </summary>
    public sealed class RealtimeConnection : IDisposable
    {
        /// <summary>The server pings every 15 seconds; this much silence means the link is dead.</summary>
        public const double SilenceLimitSeconds = 45;

        private static readonly double[] Backoff = { 0.5, 1, 2, 4, 8 };

        private readonly Func<ISocket> socketFactory;
        private readonly Func<CancellationToken, UniTask<RealtimeInfo>> ticketSource;
        private readonly Func<double> clock;
        private readonly List<(long Seq, string Text)> pending = new List<(long, string)>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private ISocket? socket;
        private string sessionId = string.Empty;
        private long seq;
        private long lastVersion = -1;
        private double lastHeard;
        private int attempt;
        private double nextAttemptAt = double.MaxValue;
        private bool fetchingTicket;
        private int snapshotsSinceOpen;
        private bool disposed;

        /// <param name="socketFactory">Makes a new socket for each (re)connect.</param>
        /// <param name="ticketSource">Fetches a fresh single-use ticket (POST /realtime/tickets).</param>
        /// <param name="clock">Seconds, monotonic (Time.realtimeSinceStartupAsDouble in the player).</param>
        /// <param name="seqBase">
        /// Where action seqs start. The server remembers the highest seq per seat across reconnects, so a restarted app
        /// must not start again at 1: callers pass the current Unix time in milliseconds.
        /// </param>
        public RealtimeConnection(Func<ISocket> socketFactory, Func<CancellationToken, UniTask<RealtimeInfo>> ticketSource, Func<double> clock, long seqBase)
        {
            this.socketFactory = socketFactory;
            this.ticketSource = ticketSource;
            this.clock = clock;
            seq = seqBase;
        }

        public event Action<ConnectionStatus>? StatusChanged;

        public event Action<RealtimeState>? StateReceived;

        public event Action<PresencePayload>? PresenceReceived;

        public event Action<MatchEndedEvent>? Ended;

        public event Action<ErrorPayload>? ErrorReceived;

        /// <summary>A <c>room</c> envelope's payload (lobby sockets), as JSON.</summary>
        public event Action<JToken>? RoomReceived;

        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Connecting;

        /// <summary>The newest state version applied, or -1 before the first.</summary>
        public long StateVersion => lastVersion;

        /// <summary>Actions sent but not yet acknowledged by a state, a snapshot or an error.</summary>
        public int PendingActions => pending.Count;

        /// <summary>Opens the first connection with the ticket the manifest (or a ticket request) carried.</summary>
        public void Start(RealtimeInfo first)
        {
            sessionId = SessionOf(first.WsUrl);
            Open(first);
        }

        /// <summary>Sends a player intent. Returns its seq, which comes back as <c>appliedSeq</c> or as an error's seq.</summary>
        public long SendAction(object payload)
        {
            var mine = ++seq;
            var text = GameVerseJson.Serialize(new Envelope<object> { T = MessageTypes.Action, Seq = mine, SessionId = sessionId, Payload = payload });
            pending.Add((mine, text));
            if (Status == ConnectionStatus.Open)
            {
                socket!.Send(text);
            }

            return mine;
        }

        /// <summary>Asks for a full snapshot (e.g. after the app was paused).</summary>
        public void Resync() => SendControl(MessageTypes.Resync, new ResyncRequest { LastStateVersion = lastVersion });

        /// <summary>Processes everything the socket delivered and runs the reconnect clock. Call every frame.</summary>
        public void Tick()
        {
            if (disposed)
            {
                return;
            }

            socket?.Pump();
            while (socket != null && socket.TryDequeue(out var socketEvent))
            {
                switch (socketEvent.Kind)
                {
                    case SocketEventKind.Opened:
                        attempt = 0;
                        snapshotsSinceOpen = 0;
                        lastHeard = clock();
                        SetStatus(ConnectionStatus.Open);
                        foreach (var (_, text) in pending)
                        {
                            socket.Send(text); // same seq: applied once, or answered with a snapshot if it already was
                        }

                        break;
                    case SocketEventKind.Text:
                        lastHeard = clock();
                        Handle(socketEvent.Text!);
                        break;
                    case SocketEventKind.Closed:
                        Lost();
                        break;
                }
            }

            if (Status == ConnectionStatus.Open && clock() - lastHeard > SilenceLimitSeconds)
            {
                socket?.Close(); // no ping for 45 seconds: the link is dead even if the OS has not noticed
                Lost();
            }

            if (Status == ConnectionStatus.Reconnecting && !fetchingTicket && clock() >= nextAttemptAt)
            {
                ReconnectAsync().Forget();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            socket?.Close();
            socket?.Dispose();
            socket = null;
            SetStatus(ConnectionStatus.Closed);
        }

        private void Open(RealtimeInfo info)
        {
            socket?.Dispose();
            socket = socketFactory();
            socket.Connect(info.WsUrl + (info.WsUrl.Contains("?") ? "&" : "?") + "ticket=" + Uri.EscapeDataString(info.Ticket));
            SetStatus(attempt == 0 && lastVersion < 0 ? ConnectionStatus.Connecting : ConnectionStatus.Reconnecting);
        }

        private void Lost()
        {
            if (disposed || Status == ConnectionStatus.Closed)
            {
                return;
            }

            socket?.Dispose();
            socket = null;
            nextAttemptAt = clock() + Backoff[Math.Min(attempt, Backoff.Length - 1)];
            attempt++;
            SetStatus(ConnectionStatus.Reconnecting);
        }

        private async UniTaskVoid ReconnectAsync()
        {
            fetchingTicket = true;
            try
            {
                var info = await ticketSource(lifetime.Token);
                if (!disposed)
                {
                    Open(info);
                }
            }
            catch (OperationCanceledException)
            {
                // Disposed while waiting.
            }
            catch (Http.ApiException ex) when (ex.Status == 404)
            {
                SetStatus(ConnectionStatus.Closed); // the session is gone (or we are not seated): stop trying
            }
            catch (Exception)
            {
                // Offline or the server is restarting: try again after the next backoff step.
                nextAttemptAt = clock() + Backoff[Math.Min(attempt, Backoff.Length - 1)];
                attempt++;
            }
            finally
            {
                fetchingTicket = false;
            }
        }

        private void Handle(string text)
        {
            Envelope<JToken>? envelope;
            try
            {
                envelope = GameVerseJson.Deserialize<Envelope<JToken>>(text);
            }
            catch (JsonException)
            {
                return; // the server never sends this; ignore rather than crash the game
            }

            switch (envelope?.T)
            {
                case MessageTypes.Ping:
                    SendControl(MessageTypes.Pong, null);
                    break;
                case MessageTypes.State:
                case MessageTypes.Snapshot:
                    HandleState(envelope);
                    break;
                case MessageTypes.Presence:
                    PresenceReceived?.Invoke(envelope.Payload.ToObject<PresencePayload>(GameVerseJson.Serializer)!);
                    break;
                case MessageTypes.Ended:
                    Ended?.Invoke(envelope.Payload.ToObject<MatchEndedEvent>(GameVerseJson.Serializer)!);
                    break;
                case MessageTypes.Error:
                    var error = envelope.Payload.ToObject<ErrorPayload>(GameVerseJson.Serializer)!;
                    if (error.Seq is { } failed)
                    {
                        pending.RemoveAll(p => p.Seq == failed);
                    }

                    ErrorReceived?.Invoke(error);
                    break;
                case MessageTypes.Room:
                    RoomReceived?.Invoke(envelope.Payload);
                    break;
            }
        }

        private void HandleState(Envelope<JToken> envelope)
        {
            var version = envelope.StateVersion ?? -1;
            var isSnapshot = envelope.T == MessageTypes.Snapshot;
            if (version < lastVersion || (!isSnapshot && version == lastVersion))
            {
                return; // stale (CLAUDE.md 4.5): a newer state is already on screen
            }

            var payload = envelope.Payload;
            var applied = payload.Value<long?>("appliedSeq");
            if (applied is { } a)
            {
                pending.RemoveAll(p => p.Seq <= a);
            }

            if (isSnapshot && ++snapshotsSinceOpen > 1)
            {
                // The first snapshot after opening answers the connect; a later one answers a resent duplicate.
                pending.Clear();
            }

            lastVersion = version;
            StateReceived?.Invoke(new RealtimeState(
                envelope.T, version, applied, payload.Value<int?>("turnSeat"), payload.Value<long?>("turnDeadline"), payload["state"] ?? JValue.CreateNull()));
        }

        private void SendControl(string type, object? payload)
        {
            if (Status == ConnectionStatus.Open)
            {
                socket!.Send(GameVerseJson.Serialize(new Envelope<object?> { T = type, Seq = 0, SessionId = sessionId, Payload = payload }));
            }
        }

        private void SetStatus(ConnectionStatus status)
        {
            if (Status != status)
            {
                Status = status;
                StatusChanged?.Invoke(status);
            }
        }

        private static string SessionOf(string wsUrl)
        {
            var path = new Uri(wsUrl).AbsolutePath;
            return path.Substring(path.LastIndexOf('/') + 1);
        }
    }
}
