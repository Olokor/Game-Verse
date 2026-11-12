using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameVerse.Platform.Sessions;

/// <summary>One play session of one game (ADR-0007). Offline sessions start active; online rooms start created.</summary>
internal sealed class GameSession
{
    public Guid Id { get; set; }

    public string GameId { get; set; } = string.Empty;

    public string GameVersion { get; set; } = string.Empty;

    /// <summary>Catalog mode key, e.g. "vs-ai".</summary>
    public string ModeKey { get; set; } = string.Empty;

    /// <summary>Mode kind (SessionManifest.mode vocabulary), e.g. "vs_ai".</summary>
    public string ModeKind { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    /// <summary>Variant, house rules and AI level as JSON (jsonb).</summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>SHA-256 (hex) of <see cref="SeedSecret"/>, published at creation for commit-reveal (ADR-0008).</summary>
    public string? SeedCommitment { get; set; }

    /// <summary>
    /// 32 random bytes the dice seed is derived from. Server-only: never sent to a client until the session has ended
    /// (GET /sessions/{id}/fairness). Null for games without server randomness.
    /// </summary>
    public byte[]? SeedSecret { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public List<SessionPlayer> Players { get; set; } = [];
}

/// <summary>A seat. Humans have a UserId; AI and extra local players do not. The name is a snapshot for history.</summary>
internal sealed class SessionPlayer
{
    public Guid SessionId { get; set; }

    public int Seat { get; set; }

    public Guid? UserId { get; set; }

    public bool IsAi { get; set; }

    public int? AiLevel { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset JoinedAt { get; set; }

    public DateTimeOffset? LeftAt { get; set; }
}

/// <summary>
/// One accepted action, in order. Append-only: the application never updates or deletes a move, and a database trigger
/// rejects UPDATE and DELETE outright (migration AddSessions), so the log stays trustworthy for replays and disputes.
/// </summary>
internal sealed class MoveLog
{
    public Guid SessionId { get; set; }

    /// <summary>1, 2, 3 ... with no gaps (checked on append; the primary key rejects duplicates).</summary>
    public int Seq { get; set; }

    public int Seat { get; set; }

    public string ActionJson { get; set; } = "{}";

    public long StateVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The final outcome of a session. At most one per session; writing it finishes the session.</summary>
internal sealed class MatchResult
{
    public Guid SessionId { get; set; }

    /// <summary>win, draw or abandoned.</summary>
    public string Outcome { get; set; } = string.Empty;

    public int[] WinningSeats { get; set; } = [];

    public string Reason { get; set; } = string.Empty;

    /// <summary>Finishing place per seat as JSON, e.g. [{"seat":2,"place":1}].</summary>
    public string PlacementsJson { get; set; } = "[]";

    public DateTimeOffset FinishedAt { get; set; }
}

internal static class SessionsSchema
{
    public const string Name = "sessions";
}

internal sealed class GameSessionConfiguration : IEntityTypeConfiguration<GameSession>
{
    public void Configure(EntityTypeBuilder<GameSession> builder)
    {
        builder.ToTable("game_sessions", SessionsSchema.Name);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.GameId).HasMaxLength(40).IsRequired();
        builder.Property(s => s.GameVersion).HasMaxLength(20).IsRequired();
        builder.Property(s => s.ModeKey).HasMaxLength(40).IsRequired();
        builder.Property(s => s.ModeKind).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();
        builder.Property(s => s.ConfigJson).HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.SeedCommitment).HasMaxLength(128);
        builder.Property(s => s.SeedSecret).HasMaxLength(32);
        builder.HasIndex(s => new { s.CreatedBy, s.Status });
        builder.HasMany(s => s.Players).WithOne().HasForeignKey(p => p.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SessionPlayerConfiguration : IEntityTypeConfiguration<SessionPlayer>
{
    // No foreign key to accounts.users: modules do not couple through each other's tables (CLAUDE.md 4.1).
    public void Configure(EntityTypeBuilder<SessionPlayer> builder)
    {
        builder.ToTable("session_players", SessionsSchema.Name);
        builder.HasKey(p => new { p.SessionId, p.Seat });
        builder.Property(p => p.DisplayName).HasMaxLength(40).IsRequired();
        builder.HasIndex(p => p.UserId);
    }
}

internal sealed class MoveLogConfiguration : IEntityTypeConfiguration<MoveLog>
{
    public void Configure(EntityTypeBuilder<MoveLog> builder)
    {
        builder.ToTable("move_logs", SessionsSchema.Name);
        builder.HasKey(m => new { m.SessionId, m.Seq });
        builder.Property(m => m.ActionJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<GameSession>().WithMany().HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MatchResultConfiguration : IEntityTypeConfiguration<MatchResult>
{
    public void Configure(EntityTypeBuilder<MatchResult> builder)
    {
        builder.ToTable("match_results", SessionsSchema.Name);
        builder.HasKey(r => r.SessionId);
        builder.Property(r => r.Outcome).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(60).IsRequired();
        builder.Property(r => r.PlacementsJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<GameSession>().WithOne().HasForeignKey<MatchResult>(r => r.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
