using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameVerse.Platform.Matchmaking;

/// <summary>A private room (CLAUDE.md 4.6, ADR-0009): seats, rules, and a series of games played in it.</summary>
internal sealed class Room
{
    public Guid Id { get; set; }

    /// <summary>6 characters from <see cref="RoomCodes.Alphabet"/>; unique among rooms that are not closed.</summary>
    public string Code { get; set; } = string.Empty;

    public string GameId { get; set; } = string.Empty;

    public int PlayerCount { get; set; }

    public string HouseRulesJson { get; set; } = "{}";

    /// <summary>1 or 3.</summary>
    public int BestOf { get; set; } = 1;

    /// <summary>GameVerse.Contracts.Matchmaking.RoomStatuses.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid HostUserId { get; set; }

    /// <summary>The game being played, or the last one played.</summary>
    public Guid? CurrentSessionId { get; set; }

    /// <summary>Games started in the current series (1-based once the first starts).</summary>
    public int GameNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Touched by every change, so concurrent changes to a room conflict on <see cref="Version"/> and retry.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the room closes if nothing happens; null while a game is being played.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Postgres xmin, as an optimistic concurrency token.</summary>
    public uint Version { get; set; }

    public List<RoomSeat> Seats { get; set; } = [];
}

/// <summary>A seat, empty when <see cref="UserId"/> is null.</summary>
internal sealed class RoomSeat
{
    public Guid RoomId { get; set; }

    public int Seat { get; set; }

    public Guid? UserId { get; set; }

    public string? DisplayName { get; set; }

    public bool Ready { get; set; }

    /// <summary>Games won in the current series.</summary>
    public int Wins { get; set; }

    /// <summary>The player left during a game; the seat frees up when that game ends.</summary>
    public bool Leaving { get; set; }
}

/// <summary>Room codes: 6 characters, without the look-alikes O/0 and I/1/L (CLAUDE.md 4.6).</summary>
internal static class RoomCodes
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>The canonical (upper-case) code, or null when it cannot be a room code.</summary>
    public static string? Normalize(string? code)
    {
        if (code is null || code.Length != Length)
        {
            return null;
        }

        var upper = code.ToUpperInvariant();
        return upper.All(Alphabet.Contains) ? upper : null;
    }
}

internal static class MatchmakingSchema
{
    public const string Name = "matchmaking";
}

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms", MatchmakingSchema.Name);
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Code).HasMaxLength(RoomCodes.Length).IsFixedLength().IsRequired();
        builder.Property(r => r.GameId).HasMaxLength(40).IsRequired();
        builder.Property(r => r.HouseRulesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Version).IsRowVersion();

        // A code is unique among rooms that are not closed; closed rooms free their code.
        builder.HasIndex(r => r.Code).IsUnique().HasFilter("status <> 'closed'");
        builder.HasIndex(r => r.CurrentSessionId);
        builder.HasMany(r => r.Seats).WithOne().HasForeignKey(s => s.RoomId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoomSeatConfiguration : IEntityTypeConfiguration<RoomSeat>
{
    // No foreign key to accounts.users: modules do not couple through each other's tables (CLAUDE.md 4.1).
    public void Configure(EntityTypeBuilder<RoomSeat> builder)
    {
        builder.ToTable("room_seats", MatchmakingSchema.Name);
        builder.HasKey(s => new { s.RoomId, s.Seat });
        builder.Property(s => s.DisplayName).HasMaxLength(40);
        builder.HasIndex(s => new { s.RoomId, s.UserId }).IsUnique().HasFilter("user_id IS NOT NULL"); // one seat per player per room
        builder.HasIndex(s => s.UserId);
    }
}
