using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVerse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sessions");

            migrationBuilder.CreateTable(
                name: "game_sessions",
                schema: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    game_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    mode_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    config_json = table.Column<string>(type: "jsonb", nullable: false),
                    seed_commitment = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "match_results",
                schema: "sessions",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    winning_seats = table.Column<int[]>(type: "integer[]", nullable: false),
                    reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    placements_json = table.Column<string>(type: "jsonb", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_results", x => x.session_id);
                    table.ForeignKey(
                        name: "fk_match_results_game_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "sessions",
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "move_logs",
                schema: "sessions",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    seat = table.Column<int>(type: "integer", nullable: false),
                    action_json = table.Column<string>(type: "jsonb", nullable: false),
                    state_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_move_logs", x => new { x.session_id, x.seq });
                    table.ForeignKey(
                        name: "fk_move_logs_game_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "sessions",
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_players",
                schema: "sessions",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_ai = table.Column<bool>(type: "boolean", nullable: false),
                    ai_level = table.Column<int>(type: "integer", nullable: true),
                    display_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_players", x => new { x.session_id, x.seat });
                    table.ForeignKey(
                        name: "fk_session_players_game_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "sessions",
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_game_sessions_created_by_status",
                schema: "sessions",
                table: "game_sessions",
                columns: new[] { "created_by", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_session_players_user_id",
                schema: "sessions",
                table: "session_players",
                column: "user_id");

            // MoveLog is append-only (ADR-0007): the database itself refuses to change or remove a recorded move.
            migrationBuilder.Sql("""
                CREATE FUNCTION sessions.reject_move_log_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'sessions.move_logs is append-only (ADR-0007): % is not allowed', TG_OP;
                END
                $$;
                CREATE TRIGGER move_logs_append_only
                    BEFORE UPDATE OR DELETE ON sessions.move_logs
                    FOR EACH ROW EXECUTE FUNCTION sessions.reject_move_log_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS move_logs_append_only ON sessions.move_logs;
                DROP FUNCTION IF EXISTS sessions.reject_move_log_change();
                """);

            migrationBuilder.DropTable(
                name: "match_results",
                schema: "sessions");

            migrationBuilder.DropTable(
                name: "move_logs",
                schema: "sessions");

            migrationBuilder.DropTable(
                name: "session_players",
                schema: "sessions");

            migrationBuilder.DropTable(
                name: "game_sessions",
                schema: "sessions");
        }
    }
}
