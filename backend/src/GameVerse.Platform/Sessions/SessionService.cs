using System.Globalization;
using System.Text.Json;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Session;
using GameVerse.Platform.Catalog;
using GameVerse.Platform.Profiles;
using Microsoft.EntityFrameworkCore;

namespace GameVerse.Platform.Sessions;

/// <summary>A seat to create. Humans pass their account id; AI seats pass a level; extra local players pass neither.</summary>
public sealed record NewSeat(Guid? UserId, bool IsAi, int? AiLevel);

public enum CreateSessionError
{
    None,
    UnknownGame,
    NotPlayable,
    UnknownMode,
    BadPlayerCount,
}

public enum AppendMoveResult
{
    Appended,
    UnknownSession,
    NotActive,
    OutOfOrder,
}

/// <summary>A finished session's outcome. Placements map seat to finishing place (1 = first).</summary>
public sealed record MatchOutcome(string Outcome, IReadOnlyList<int> WinningSeats, string Reason, IReadOnlyDictionary<int, int> Placements);

/// <summary>
/// Sessions as other modules use them (CLAUDE.md 4.1): Realtime appends moves, Matchmaking and private rooms create
/// online sessions, Progression reads results. Moves are opaque JSON here; each game's engine validates them before they
/// are appended (Ludo: Prompt 1.3).
/// </summary>
public interface ISessionService
{
    Task<(Guid? SessionId, CreateSessionError Error)> CreateAsync(
        string gameId, string modeKey, Guid createdBy, IReadOnlyList<NewSeat> seats, SessionConfig<Dictionary<string, string>> config, CancellationToken ct = default);

    Task<AppendMoveResult> AppendMoveAsync(Guid sessionId, int seq, int seat, JsonElement action, long stateVersion, CancellationToken ct = default);

    /// <summary>Records the result and finishes the session. False when there is already a result or no such session.</summary>
    Task<bool> RecordResultAsync(Guid sessionId, MatchOutcome outcome, CancellationToken ct = default);

    /// <summary>The manifest for a participant, or null when the session is unknown or the user is not seated in it.</summary>
    Task<SessionManifest<SessionConfig<Dictionary<string, string>>, JsonElement?>?> GetManifestAsync(Guid sessionId, Guid localUserId, CancellationToken ct = default);
}

internal sealed class SessionService(DbContext db, ICatalog catalog, IProfileService profiles, TimeProvider time) : ISessionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<(Guid? SessionId, CreateSessionError Error)> CreateAsync(
        string gameId, string modeKey, Guid createdBy, IReadOnlyList<NewSeat> seats, SessionConfig<Dictionary<string, string>> config, CancellationToken ct = default)
    {
        var game = await catalog.GetGameAsync(gameId, ct).ConfigureAwait(false);
        if (game is null)
        {
            return (null, CreateSessionError.UnknownGame);
        }

        if (game.Status != GameStatuses.Enabled)
        {
            return (null, CreateSessionError.NotPlayable);
        }

        var mode = game.Modes.SingleOrDefault(m => m.Key == modeKey);
        if (mode is null)
        {
            return (null, CreateSessionError.UnknownMode);
        }

        if (!mode.PlayerCounts.Contains(seats.Count))
        {
            return (null, CreateSessionError.BadPlayerCount);
        }

        var now = time.GetUtcNow();
        var online = mode.Kind is ModeKinds.OnlineRanked or ModeKinds.OnlinePrivate;
        var session = new GameSession
        {
            Id = Guid.CreateVersion7(now),
            GameId = game.Id,
            GameVersion = game.Version,
            ModeKey = mode.Key,
            ModeKind = mode.Kind,
            Status = online ? SessionStatuses.Created : SessionStatuses.Active,
            ConfigJson = JsonSerializer.Serialize(config, Json),
            CreatedBy = createdBy,
            CreatedAt = now,
            StartedAt = online ? null : now,
        };

        for (var seat = 0; seat < seats.Count; seat++)
        {
            var s = seats[seat];
            session.Players.Add(new SessionPlayer
            {
                SessionId = session.Id,
                Seat = seat,
                UserId = s.UserId,
                IsAi = s.IsAi,
                AiLevel = s.AiLevel,
                DisplayName = await SeatNameAsync(s, seat, ct).ConfigureAwait(false),
                JoinedAt = now,
            });
        }

        db.Set<GameSession>().Add(session);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (session.Id, CreateSessionError.None);
    }

    public async Task<AppendMoveResult> AppendMoveAsync(Guid sessionId, int seq, int seat, JsonElement action, long stateVersion, CancellationToken ct = default)
    {
        var status = await db.Set<GameSession>().Where(s => s.Id == sessionId).Select(s => s.Status).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (status is null)
        {
            return AppendMoveResult.UnknownSession;
        }

        if (status != SessionStatuses.Active)
        {
            return AppendMoveResult.NotActive;
        }

        var last = await db.Set<MoveLog>().Where(m => m.SessionId == sessionId).MaxAsync(m => (int?)m.Seq, ct).ConfigureAwait(false) ?? 0;
        if (seq != last + 1)
        {
            return AppendMoveResult.OutOfOrder;
        }

        db.Set<MoveLog>().Add(new MoveLog
        {
            SessionId = sessionId,
            Seq = seq,
            Seat = seat,
            ActionJson = action.GetRawText(),
            StateVersion = stateVersion,
            CreatedAt = time.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return AppendMoveResult.OutOfOrder; // another writer took this seq first (primary key)
        }

        return AppendMoveResult.Appended;
    }

    public async Task<bool> RecordResultAsync(Guid sessionId, MatchOutcome outcome, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var session = await db.Set<GameSession>().SingleOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
        if (session is null || session.Status is SessionStatuses.Finished or SessionStatuses.Abandoned)
        {
            return false;
        }

        var now = time.GetUtcNow();
        db.Set<MatchResult>().Add(new MatchResult
        {
            SessionId = sessionId,
            Outcome = outcome.Outcome,
            WinningSeats = [.. outcome.WinningSeats],
            Reason = outcome.Reason,
            PlacementsJson = JsonSerializer.Serialize(outcome.Placements.Select(p => new { seat = p.Key, place = p.Value }), Json),
            FinishedAt = now,
        });
        session.Status = outcome.Outcome == "abandoned" ? SessionStatuses.Abandoned : SessionStatuses.Finished;
        session.EndedAt = now;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return false; // a result already exists (primary key)
        }

        return true;
    }

    public async Task<SessionManifest<SessionConfig<Dictionary<string, string>>, JsonElement?>?> GetManifestAsync(
        Guid sessionId, Guid localUserId, CancellationToken ct = default)
    {
        var session = await db.Set<GameSession>().AsNoTracking().Include(s => s.Players)
            .SingleOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
        if (session is null || session.Players.All(p => p.UserId != localUserId))
        {
            return null;
        }

        return new SessionManifest<SessionConfig<Dictionary<string, string>>, JsonElement?>
        {
            GameId = session.GameId,
            GameVersion = session.GameVersion,
            SessionId = session.Id.ToString(),
            Mode = session.ModeKind,
            LocalPlayerId = localUserId.ToString(),
            Players = session.Players.OrderBy(p => p.Seat).Select(p => new PlayerSlot
            {
                Id = p.UserId?.ToString() ?? (p.IsAi ? "ai-" : "local-") + p.Seat.ToString(CultureInfo.InvariantCulture),
                DisplayName = p.DisplayName,
                Seat = p.Seat,
                IsAI = p.IsAi,
                // TODO(gameverse): equipped cosmetics from the Economy inventory (Prompt 2.2).
                Cosmetics = [],
            }).ToList(),
            Config = JsonSerializer.Deserialize<SessionConfig<Dictionary<string, string>>>(session.ConfigJson, Json)!,
            InitialState = null, // the game engine builds it (Ludo: Prompt 1.3)
            Realtime = null, // online sessions get the socket URL and ticket in Prompt 1.4
        };
    }

    public async Task<IReadOnlyList<SessionSummary>> ListForUserAsync(Guid userId, string? status, CancellationToken ct)
    {
        var query = db.Set<GameSession>().AsNoTracking().Where(s => s.Players.Any(p => p.UserId == userId));
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(s => s.Status == status);
        }

        var sessions = await query.OrderByDescending(s => s.CreatedAt).Take(50).ToListAsync(ct).ConfigureAwait(false);
        return sessions.Select(s => new SessionSummary
        {
            SessionId = s.Id.ToString(),
            GameId = s.GameId,
            Mode = s.ModeKey,
            Status = s.Status,
            CreatedAt = s.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        }).ToList();
    }

    private async Task<string> SeatNameAsync(NewSeat seat, int index, CancellationToken ct)
    {
        if (seat.UserId is { } userId)
        {
            var profile = await profiles.GetAsync(userId, ct).ConfigureAwait(false);
            return profile?.DisplayName ?? "Player";
        }

        return seat.IsAi
            ? "AI level " + (seat.AiLevel ?? 1).ToString(CultureInfo.InvariantCulture)
            : "Player " + (index + 1).ToString(CultureInfo.InvariantCulture);
    }
}
