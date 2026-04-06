using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shared.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fixtures",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    MatchId = table.Column<string>(type: "text", nullable: false),
                    Minute = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    PlayerId = table.Column<string>(type: "text", nullable: false),
                    AssistPlayerId = table.Column<string>(type: "text", nullable: true),
                    TeamId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fixtures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    HomeTeamId = table.Column<string>(type: "text", nullable: false),
                    AwayTeamId = table.Column<string>(type: "text", nullable: false),
                    Gameweek = table.Column<int>(type: "integer", nullable: false),
                    Kickoff = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ScoreHome = table.Column<int>(type: "integer", nullable: false),
                    ScoreAway = table.Column<int>(type: "integer", nullable: false),
                    Minute = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<string>(type: "text", nullable: false),
                    TeamId = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPoints = table.Column<int>(type: "integer", nullable: false),
                    Stats_Goals = table.Column<int>(type: "integer", nullable: false),
                    Stats_Assists = table.Column<int>(type: "integer", nullable: false),
                    Stats_YellowCards = table.Column<int>(type: "integer", nullable: false),
                    Stats_RedCards = table.Column<int>(type: "integer", nullable: false),
                    Stats_MinutesPlayed = table.Column<int>(type: "integer", nullable: false),
                    Stats_CleanSheets = table.Column<int>(type: "integer", nullable: false),
                    Stats_OwnGoals = table.Column<int>(type: "integer", nullable: false),
                    Stats_PenaltiesMissed = table.Column<int>(type: "integer", nullable: false),
                    Stats_Saves = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Squads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Gameweek = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Squads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Short = table.Column<string>(type: "text", nullable: false),
                    Stadium = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    LeagueId = table.Column<string>(type: "text", nullable: true),
                    TotalPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SquadPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SquadId = table.Column<string>(type: "text", nullable: false),
                    PlayerId = table.Column<string>(type: "text", nullable: false),
                    PositionSlot = table.Column<string>(type: "text", nullable: false),
                    IsCaptain = table.Column<bool>(type: "boolean", nullable: false),
                    IsViceCaptain = table.Column<bool>(type: "boolean", nullable: false),
                    IsBench = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquadPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SquadPlayers_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SquadPlayers_SquadId",
                table: "SquadPlayers",
                column: "SquadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fixtures");

            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "SquadPlayers");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Squads");
        }
    }
}
