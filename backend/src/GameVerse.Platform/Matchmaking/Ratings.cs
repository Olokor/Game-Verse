using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Platform.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace GameVerse.Platform.Matchmaking;

/// <summary>A player's Glicko-2 rating in one game (CLAUDE.md 4.6). No row means a new player: 1500 / 350 / 0.06.</summary>
internal sealed class PlayerRating
{
    public Guid UserId { get; set; }

    public string GameId { get; set; } = string.Empty;

    public double Rating { get; set; }

    public double Deviation { get; set; }

    public double Volatility { get; set; }

    public int GamesPlayed { get; set; }

    public DateTimeOffset LastPlayedAt { get; set; }

    /// <summary>Postgres xmin: two results for one player at once retry instead of losing an update.</summary>
    public uint Version { get; set; }
}

internal sealed class PlayerRatingConfiguration : IEntityTypeConfiguration<PlayerRating>
{
    public void Configure(EntityTypeBuilder<PlayerRating> builder)
    {
        builder.ToTable("ratings", MatchmakingSchema.Name);
        builder.HasKey(r => new { r.UserId, r.GameId });
        builder.Property(r => r.GameId).HasMaxLength(40).IsRequired();
        builder.Property(r => r.Version).IsRowVersion();
        builder.HasIndex(r => new { r.GameId, r.Rating }); // leaderboards by rating, later
    }
}

/// <summary>Reads and updates ratings. A rating period is one game; idle days widen the deviation.</summary>
internal sealed class RatingService(DbContext db, TimeProvider time)
{
    /// <summary>Above this deviation a rating is shown as provisional.</summary>
    public const double ProvisionalAbove = 110;

    private const int Attempts = 5;

    /// <summary>The current rating, with the deviation widened for the days since the last rated game.</summary>
    public async Task<GlickoRating> GetAsync(Guid userId, string gameId, CancellationToken ct)
    {
        var row = await db.Set<PlayerRating>().AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId && r.GameId == gameId, ct).ConfigureAwait(false);
        return row is null ? GlickoRating.Default : Current(row);
    }

    public async Task<RatingDto> GetDtoAsync(Guid userId, string gameId, CancellationToken ct)
    {
        var row = await db.Set<PlayerRating>().AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId && r.GameId == gameId, ct).ConfigureAwait(false);
        var rating = row is null ? GlickoRating.Default : Current(row);
        return new RatingDto
        {
            GameId = gameId,
            Rating = (int)Math.Round(rating.Rating),
            Deviation = (int)Math.Round(rating.Deviation),
            GamesPlayed = row?.GamesPlayed ?? 0,
            Provisional = rating.Deviation > ProvisionalAbove,
        };
    }

    /// <summary>Rates one finished game: <paramref name="places"/>[i] is the finishing place of <paramref name="players"/>[i].</summary>
    public async Task ApplyAsync(string gameId, IReadOnlyList<Guid> players, IReadOnlyList<int> places, CancellationToken ct)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var rows = await db.Set<PlayerRating>().Where(r => r.GameId == gameId && players.Contains(r.UserId)).ToListAsync(ct).ConfigureAwait(false);
            var now = time.GetUtcNow();
            var ordered = players.Select(p => rows.SingleOrDefault(r => r.UserId == p) ?? Add(p, gameId, now)).ToList();
            var after = Glicko2.RateFinishingOrder(ordered.Select(Current).ToList(), places);
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Rating = after[i].Rating;
                ordered[i].Deviation = after[i].Deviation;
                ordered[i].Volatility = after[i].Volatility;
                ordered[i].GamesPlayed++;
                ordered[i].LastPlayedAt = now;
            }

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear(); // another result for one of these players landed first, or a first row raced: redo
            }
        }

        throw new InvalidOperationException("Could not save the ratings for this game.");
    }

    private PlayerRating Add(Guid userId, string gameId, DateTimeOffset now)
    {
        var row = new PlayerRating
        {
            UserId = userId,
            GameId = gameId,
            Rating = Glicko2.DefaultRating,
            Deviation = Glicko2.DefaultDeviation,
            Volatility = Glicko2.DefaultVolatility,
            LastPlayedAt = now,
        };
        db.Set<PlayerRating>().Add(row);
        return row;
    }

    private GlickoRating Current(PlayerRating row)
    {
        var idleDays = (int)Math.Floor((time.GetUtcNow() - row.LastPlayedAt).TotalDays);
        return Glicko2.Idle(new GlickoRating(row.Rating, row.Deviation, row.Volatility), Math.Max(0, idleDays));
    }
}

/// <summary>
/// Rates ranked games when their result is recorded. Resolves its services on use: the session service depends on this
/// handler, and the session lookup goes back through the session service.
/// </summary>
internal sealed class RatingsOnFinish(IServiceProvider scope) : IMatchFinishedHandler
{
    public async Task OnMatchFinishedAsync(Guid sessionId, MatchOutcome outcome, CancellationToken ct)
    {
        var live = await scope.GetRequiredService<ISessionService>().GetLiveAsync(sessionId, ct).ConfigureAwait(false);
        if (live is null || live.ModeKind != ModeKinds.OnlineRanked || live.Seats.Any(s => s.UserId is null))
        {
            return; // only ranked games between people change ratings
        }

        var seats = live.Seats.OrderBy(s => s.Seat).ToList();
        var places = seats.Select(s => outcome.Placements.TryGetValue(s.Seat, out var place) ? place : seats.Count).ToList();
        await scope.GetRequiredService<RatingService>().ApplyAsync(live.GameId, seats.Select(s => s.UserId!.Value).ToList(), places, ct).ConfigureAwait(false);
    }
}
