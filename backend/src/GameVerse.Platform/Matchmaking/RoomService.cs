using System.Globalization;
using System.Text.Json;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Contracts.Session;
using GameVerse.Platform.Profiles;
using GameVerse.Platform.Realtime;
using GameVerse.Platform.Sessions;
using Microsoft.EntityFrameworkCore;

namespace GameVerse.Platform.Matchmaking;

/// <summary>Why a room request failed.</summary>
internal enum RoomError
{
    None,
    NotFound,
    Gone,
    NotHost,
    NotSeated,
    Full,
    WrongStatus,
    NotReady,
    Invalid,
}

internal sealed record RoomResult(RoomDto? Room, RoomError Error, string? Reason = null)
{
    public static RoomResult Fail(RoomError error, string? reason = null) => new(null, error, reason);
}

/// <summary>Settings for private rooms (GameVerse:Rooms).</summary>
public sealed class RoomsOptions
{
    public const string Section = "GameVerse:Rooms";

    /// <summary>Base of share links: the room code is appended (CLAUDE.md 4.6: gameverse.app/r/ABC123).</summary>
    public string ShareBaseUrl { get; set; } = "https://gameverse.app/r/";

    /// <summary>A lobby closes after this long without a change.</summary>
    public int LobbyMinutes { get; set; } = 30;

    /// <summary>A room closes this long after a game ends if nobody starts the next one.</summary>
    public int AfterGameMinutes { get; set; } = 10;
}

/// <summary>
/// Private rooms (CLAUDE.md 4.6, ADR-0009). Every change publishes the room to its lobby sockets. Changes to one room are
/// serialised by an optimistic concurrency token (Postgres xmin): a conflicting change reloads and retries.
/// </summary>
internal sealed class RoomService(
    DbContext db, ISessionService sessions, IProfileService profiles, IRoomBroadcaster broadcaster, RoomsOptions options, TimeProvider time)
{
    /// <summary>The catalog mode private rooms play (every game with rooms has one).</summary>
    public const string ModeKey = "private-room";

    private const int Attempts = 5;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RoomResult> CreateAsync(Guid userId, CreateRoomRequest request, CancellationToken ct)
    {
        var (error, reason) = await sessions.CheckSetupAsync(request.GameId, ModeKey, request.PlayerCount, request.HouseRules, ct).ConfigureAwait(false);
        if (error != CreateSessionError.None)
        {
            return RoomResult.Fail(error == CreateSessionError.UnknownGame ? RoomError.NotFound : RoomError.Invalid, reason);
        }

        var now = time.GetUtcNow();
        var name = await NameAsync(userId, ct).ConfigureAwait(false);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var code = RoomCodes.Generate();
            var holder = await db.Set<Room>().SingleOrDefaultAsync(r => r.Code == code && r.Status != RoomStatuses.Closed, ct).ConfigureAwait(false);
            if (holder is not null)
            {
                if (!IsExpired(holder, now))
                {
                    continue; // in use: draw another
                }

                holder.Status = RoomStatuses.Closed; // an expired room gives its code up
            }

            var room = new Room
            {
                Id = Guid.CreateVersion7(now),
                Code = code,
                GameId = request.GameId,
                PlayerCount = request.PlayerCount,
                HouseRulesJson = JsonSerializer.Serialize(request.HouseRules, Json),
                BestOf = request.BestOf,
                Status = RoomStatuses.Lobby,
                HostUserId = userId,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now.AddMinutes(options.LobbyMinutes),
                Seats = Enumerable.Range(0, request.PlayerCount).Select(seat => new RoomSeat { Seat = seat }).ToList(),
            };
            room.Seats[0].UserId = userId;
            room.Seats[0].DisplayName = name;
            db.Set<Room>().Add(room);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear(); // the code was taken a moment ago
                continue;
            }

            return new RoomResult(ToDto(room), RoomError.None);
        }

        throw new InvalidOperationException("Could not find a free room code.");
    }

    public async Task<RoomResult> GetAsync(string code, CancellationToken ct)
    {
        var room = await LoadAsync(code, tracked: false, ct).ConfigureAwait(false);
        return room is null ? RoomResult.Fail(RoomError.NotFound)
            : IsExpired(room, time.GetUtcNow()) ? RoomResult.Fail(RoomError.Gone)
            : new RoomResult(ToDto(room), RoomError.None);
    }

    /// <summary>The room for a seated member, else null (lobby socket and its tickets).</summary>
    public async Task<RoomDto?> GetForMemberAsync(string code, Guid userId, CancellationToken ct)
    {
        var room = await LoadAsync(code, tracked: false, ct).ConfigureAwait(false);
        return room is not null && !IsExpired(room, time.GetUtcNow()) && SeatOf(room, userId) is not null ? ToDto(room) : null;
    }

    public Task<RoomResult> JoinAsync(string code, Guid userId, CancellationToken ct) =>
        ChangeAsync(code, async room =>
        {
            if (SeatOf(room, userId) is not null)
            {
                return RoomError.None; // already in: joining again is harmless
            }

            if (room.Status != RoomStatuses.Lobby)
            {
                return RoomError.WrongStatus;
            }

            if (room.Seats.FirstOrDefault(s => s.UserId is null) is not { } free)
            {
                return RoomError.Full;
            }

            free.UserId = userId;
            free.DisplayName = await NameAsync(userId, ct).ConfigureAwait(false);
            free.Ready = false;
            return RoomError.None;
        }, ct);

    public Task<RoomResult> SetReadyAsync(string code, Guid userId, bool ready, CancellationToken ct) =>
        ChangeAsync(code, async room =>
        {
            if (SeatOf(room, userId) is not { } seat)
            {
                return RoomError.NotSeated;
            }

            if (room.Status == RoomStatuses.Playing)
            {
                return RoomError.WrongStatus;
            }

            seat.Ready = ready;

            // After a game, everyone tapping ready is the rematch: the next game (or a new series) starts by itself.
            if (room.Status is RoomStatuses.BetweenGames or RoomStatuses.SeriesOver && AllSeatedAndReady(room))
            {
                if (room.Status == RoomStatuses.SeriesOver)
                {
                    ResetSeries(room);
                }

                return await StartGameAsync(room, ct).ConfigureAwait(false);
            }

            return RoomError.None;
        }, ct);

    public Task<RoomResult> SetRulesAsync(string code, Guid userId, Dictionary<string, string> rules, CancellationToken ct)
    {
        string? reason = null;
        return ChangeAsync(code, async room =>
        {
            if (room.HostUserId != userId)
            {
                return RoomError.NotHost;
            }

            if (room.Status != RoomStatuses.Lobby)
            {
                return RoomError.WrongStatus;
            }

            var (error, why) = await sessions.CheckSetupAsync(room.GameId, ModeKey, room.PlayerCount, rules, ct).ConfigureAwait(false);
            if (error != CreateSessionError.None)
            {
                reason = why;
                return RoomError.Invalid;
            }

            room.HouseRulesJson = JsonSerializer.Serialize(rules, Json);
            room.Seats.ForEach(s => s.Ready = false); // everyone agrees to the new rules again
            return RoomError.None;
        }, ct, () => reason);
    }

    public Task<RoomResult> LeaveAsync(string code, Guid userId, CancellationToken ct) =>
        ChangeAsync(code, room =>
        {
            if (SeatOf(room, userId) is not { } seat)
            {
                return Task.FromResult(RoomError.NotSeated);
            }

            if (room.Status == RoomStatuses.Playing)
            {
                // The game goes on: the socket's disconnect grace (or a resign) forfeits the seat. It frees when the game ends.
                seat.Leaving = true;
                seat.Ready = false;
                return Task.FromResult(RoomError.None);
            }

            Free(seat);
            AfterSeatsFreed(room);
            return Task.FromResult(RoomError.None);
        }, ct);

    public Task<RoomResult> StartAsync(string code, Guid userId, CancellationToken ct) =>
        ChangeAsync(code, async room =>
        {
            if (room.HostUserId != userId)
            {
                return RoomError.NotHost;
            }

            if (room.Status != RoomStatuses.Lobby)
            {
                return RoomError.WrongStatus;
            }

            return AllSeatedAndReady(room) ? await StartGameAsync(room, ct).ConfigureAwait(false) : RoomError.NotReady;
        }, ct);

    /// <summary>A game in a room ended: score it, then wait for the next ready-up (IMatchFinishedHandler).</summary>
    public async Task OnMatchFinishedAsync(Guid sessionId, MatchOutcome outcome, CancellationToken ct)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var room = await db.Set<Room>().Include(r => r.Seats).SingleOrDefaultAsync(r => r.CurrentSessionId == sessionId, ct).ConfigureAwait(false);
            if (room is null || room.Status != RoomStatuses.Playing)
            {
                return;
            }

            var seats = room.Seats.OrderBy(s => s.Seat).ToList();
            foreach (var winner in outcome.WinningSeats.Where(w => w >= 0 && w < seats.Count))
            {
                seats[winner].Wins++;
            }

            seats.ForEach(s => s.Ready = false);
            var leavers = seats.Where(s => s.Leaving).ToList();
            if (leavers.Count > 0)
            {
                leavers.ForEach(Free);
                AfterSeatsFreed(room);
            }
            else
            {
                room.Status = seats.Any(s => s.Wins >= WinsNeeded(room)) ? RoomStatuses.SeriesOver : RoomStatuses.BetweenGames;
            }

            Touch(room, options.AfterGameMinutes);
            if (await TrySaveAsync(ct).ConfigureAwait(false))
            {
                await PublishAsync(room).ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>Loads, changes, saves and publishes one room, retrying when another change got there first.</summary>
    private async Task<RoomResult> ChangeAsync(string code, Func<Room, Task<RoomError>> change, CancellationToken ct, Func<string?>? reason = null)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var room = await LoadAsync(code, tracked: true, ct).ConfigureAwait(false);
            if (room is null)
            {
                return RoomResult.Fail(RoomError.NotFound);
            }

            if (IsExpired(room, time.GetUtcNow()))
            {
                return RoomResult.Fail(RoomError.Gone);
            }

            var error = await change(room).ConfigureAwait(false);
            if (error != RoomError.None)
            {
                db.ChangeTracker.Clear();
                return RoomResult.Fail(error, reason?.Invoke());
            }

            Touch(room, room.Status == RoomStatuses.Lobby ? options.LobbyMinutes : options.AfterGameMinutes);
            if (await TrySaveAsync(ct).ConfigureAwait(false))
            {
                await PublishAsync(room).ConfigureAwait(false);
                return new RoomResult(ToDto(room), RoomError.None);
            }
        }

        throw new InvalidOperationException("The room kept changing; try again.");
    }

    private async Task<RoomError> StartGameAsync(Room room, CancellationToken ct)
    {
        var rules = JsonSerializer.Deserialize<Dictionary<string, string>>(room.HouseRulesJson, Json) ?? [];
        var seats = room.Seats.OrderBy(s => s.Seat).Select(s => new NewSeat(s.UserId, false, null)).ToList();
        var (sessionId, error) = await sessions.CreateAsync(
            room.GameId, ModeKey, room.HostUserId, seats, new SessionConfig<Dictionary<string, string>> { HouseRules = rules }, ct).ConfigureAwait(false);
        if (error != CreateSessionError.None)
        {
            return RoomError.Invalid; // e.g. the game was switched off since the room opened
        }

        room.CurrentSessionId = sessionId;
        room.GameNumber++;
        room.Status = RoomStatuses.Playing;
        room.Seats.ForEach(s => s.Ready = false);
        return RoomError.None;
    }

    private void Touch(Room room, int minutes)
    {
        var now = time.GetUtcNow();
        room.UpdatedAt = now;
        room.ExpiresAt = room.Status switch
        {
            RoomStatuses.Playing => null,
            RoomStatuses.Closed => now,
            _ => now.AddMinutes(minutes),
        };
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private Task PublishAsync(Room room) => broadcaster.PublishRoomAsync(room.Code, ToDto(room));

    private Task<Room?> LoadAsync(string code, bool tracked, CancellationToken ct)
    {
        var rooms = tracked ? db.Set<Room>() : db.Set<Room>().AsNoTracking();
        return rooms.Include(r => r.Seats)
            .Where(r => r.Code == code)
            .OrderBy(r => r.Status == RoomStatuses.Closed) // the open room holding this code, if any, else the latest closed one
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<string> NameAsync(Guid userId, CancellationToken ct) =>
        (await profiles.GetAsync(userId, ct).ConfigureAwait(false))?.DisplayName ?? "Player";

    /// <summary>After seats free up between games: the series resets, the host moves on, an empty room closes.</summary>
    private static void AfterSeatsFreed(Room room)
    {
        if (room.Seats.All(s => s.UserId is null))
        {
            room.Status = RoomStatuses.Closed;
            return;
        }

        if (room.Seats.All(s => s.UserId != room.HostUserId))
        {
            room.HostUserId = room.Seats.OrderBy(s => s.Seat).First(s => s.UserId is not null).UserId!.Value;
        }

        if (room.Status != RoomStatuses.Lobby)
        {
            ResetSeries(room);
            room.Status = RoomStatuses.Lobby;
        }
    }

    private static void ResetSeries(Room room)
    {
        room.GameNumber = 0;
        room.Seats.ForEach(s => s.Wins = 0);
    }

    private static void Free(RoomSeat seat)
    {
        seat.UserId = null;
        seat.DisplayName = null;
        seat.Ready = false;
        seat.Leaving = false;
    }

    private static bool AllSeatedAndReady(Room room) => room.Seats.All(s => s.UserId is not null && s.Ready && !s.Leaving);

    private static RoomSeat? SeatOf(Room room, Guid userId) => room.Seats.FirstOrDefault(s => s.UserId == userId);

    private static int WinsNeeded(Room room) => (room.BestOf / 2) + 1;

    private static bool IsExpired(Room room, DateTimeOffset now) => room.Status == RoomStatuses.Closed || room.ExpiresAt <= now;

    private RoomDto ToDto(Room room)
    {
        var seats = room.Seats.OrderBy(s => s.Seat).ToList();
        return new RoomDto
        {
            Code = room.Code,
            ShareUrl = options.ShareBaseUrl.TrimEnd('/') + "/" + room.Code,
            GameId = room.GameId,
            PlayerCount = room.PlayerCount,
            HouseRules = JsonSerializer.Deserialize<Dictionary<string, string>>(room.HouseRulesJson, Json) ?? [],
            BestOf = room.BestOf,
            Status = room.Status,
            HostSeat = seats.FirstOrDefault(s => s.UserId == room.HostUserId)?.Seat ?? 0,
            Seats = seats.Select(s => new RoomSeatDto
            {
                Seat = s.Seat,
                PlayerId = s.UserId?.ToString(),
                DisplayName = s.DisplayName,
                Ready = s.Ready,
                Wins = s.Wins,
            }).ToList(),
            SessionId = room.CurrentSessionId?.ToString(),
            GameNumber = room.GameNumber,
            SeriesWinners = room.Status == RoomStatuses.SeriesOver ? seats.Where(s => s.Wins >= WinsNeeded(room)).Select(s => s.Seat).ToList() : [],
            ExpiresAt = room.ExpiresAt?.ToString("O", CultureInfo.InvariantCulture),
        };
    }
}
