using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OurStory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDrawAndGuess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "draw_games",
                columns: table => new
                {
                    RelationshipId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerOneId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerTwoId = table.Column<int>(type: "INTEGER", nullable: false),
                    StateJson = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draw_games", x => x.RelationshipId);
                    table.ForeignKey(
                        name: "FK_draw_games_couple_relationships_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "couple_relationships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_draw_games_users_PlayerOneId",
                        column: x => x.PlayerOneId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_draw_games_users_PlayerTwoId",
                        column: x => x.PlayerTwoId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "draw_words",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RelationshipId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Answer = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Aliases = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draw_words", x => x.Id);
                    table.ForeignKey(
                        name: "FK_draw_words_couple_relationships_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "couple_relationships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_draw_games_PlayerOneId",
                table: "draw_games",
                column: "PlayerOneId");

            migrationBuilder.CreateIndex(
                name: "IX_draw_games_PlayerTwoId",
                table: "draw_games",
                column: "PlayerTwoId");

            migrationBuilder.CreateIndex(
                name: "IX_draw_words_RelationshipId_SourceKey",
                table: "draw_words",
                columns: new[] { "RelationshipId", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draw_games");

            migrationBuilder.DropTable(
                name: "draw_words");
        }
    }
}
