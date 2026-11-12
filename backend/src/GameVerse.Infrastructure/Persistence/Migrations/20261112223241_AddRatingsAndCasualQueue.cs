using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVerse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRatingsAndCasualQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ratings",
                schema: "matchmaking",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rating = table.Column<double>(type: "double precision", nullable: false),
                    deviation = table.Column<double>(type: "double precision", nullable: false),
                    volatility = table.Column<double>(type: "double precision", nullable: false),
                    games_played = table.Column<int>(type: "integer", nullable: false),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ratings", x => new { x.user_id, x.game_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_ratings_game_id_rating",
                schema: "matchmaking",
                table: "ratings",
                columns: new[] { "game_id", "rating" });

            // ADR-0010 (owner decision 2026-11-12): Ludo's Quick match becomes casual and a Ranked mode joins it. The catalog
            // seeder only inserts missing rows (it inserts "ranked"), so existing databases change Quick match's kind here,
            // and Private room moves after Ranked in the list.
            migrationBuilder.Sql("UPDATE catalog.game_modes SET kind = 'online_casual' WHERE game_id = 'ludo' AND key = 'quick-match' AND kind = 'online_ranked';");
            migrationBuilder.Sql("UPDATE catalog.game_modes SET sort_order = 50 WHERE game_id = 'ludo' AND key = 'private-room' AND sort_order = 40;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE catalog.game_modes SET kind = 'online_ranked' WHERE game_id = 'ludo' AND key = 'quick-match' AND kind = 'online_casual';");
            migrationBuilder.Sql("DELETE FROM catalog.game_modes WHERE game_id = 'ludo' AND key = 'ranked';");
            migrationBuilder.Sql("UPDATE catalog.game_modes SET sort_order = 40 WHERE game_id = 'ludo' AND key = 'private-room' AND sort_order = 50;");

            migrationBuilder.DropTable(
                name: "ratings",
                schema: "matchmaking");
        }
    }
}
