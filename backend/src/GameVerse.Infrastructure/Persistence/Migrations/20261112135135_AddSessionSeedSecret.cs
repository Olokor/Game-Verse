using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVerse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionSeedSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "seed_secret",
                schema: "sessions",
                table: "game_sessions",
                type: "bytea",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "seed_secret",
                schema: "sessions",
                table: "game_sessions");
        }
    }
}
