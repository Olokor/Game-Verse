using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVerse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "matchmaking");

            migrationBuilder.CreateTable(
                name: "rooms",
                schema: "matchmaking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    game_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    player_count = table.Column<int>(type: "integer", nullable: false),
                    house_rules_json = table.Column<string>(type: "jsonb", nullable: false),
                    best_of = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    host_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    game_number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rooms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "room_seats",
                schema: "matchmaking",
                columns: table => new
                {
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ready = table.Column<bool>(type: "boolean", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    leaving = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_seats", x => new { x.room_id, x.seat });
                    table.ForeignKey(
                        name: "fk_room_seats_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "matchmaking",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_room_seats_room_id_user_id",
                schema: "matchmaking",
                table: "room_seats",
                columns: new[] { "room_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_room_seats_user_id",
                schema: "matchmaking",
                table: "room_seats",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_code",
                schema: "matchmaking",
                table: "rooms",
                column: "code",
                unique: true,
                filter: "status <> 'closed'");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_current_session_id",
                schema: "matchmaking",
                table: "rooms",
                column: "current_session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_seats",
                schema: "matchmaking");

            migrationBuilder.DropTable(
                name: "rooms",
                schema: "matchmaking");
        }
    }
}
