using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerGameweekScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerGameweekScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<string>(type: "text", nullable: false),
                    MatchId = table.Column<string>(type: "text", nullable: false),
                    Gameweek = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerGameweekScores", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGameweekScores_IdempotencyKey",
                table: "PlayerGameweekScores",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGameweekScores_PlayerId_Gameweek",
                table: "PlayerGameweekScores",
                columns: new[] { "PlayerId", "Gameweek" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerGameweekScores");
        }
    }
}
