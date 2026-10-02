using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OurStory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGomokuGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gomoku_games",
                columns: table => new
                {
                    RelationshipId = table.Column<int>(type: "INTEGER", nullable: false),
                    BlackUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    WhiteUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovesJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gomoku_games", x => x.RelationshipId);
                    table.ForeignKey(
                        name: "FK_gomoku_games_couple_relationships_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "couple_relationships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gomoku_games_users_BlackUserId",
                        column: x => x.BlackUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gomoku_games_users_WhiteUserId",
                        column: x => x.WhiteUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gomoku_games_BlackUserId",
                table: "gomoku_games",
                column: "BlackUserId");

            migrationBuilder.CreateIndex(
                name: "IX_gomoku_games_WhiteUserId",
                table: "gomoku_games",
                column: "WhiteUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gomoku_games");
        }
    }
}
