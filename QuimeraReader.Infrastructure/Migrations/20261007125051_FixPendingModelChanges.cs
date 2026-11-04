using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookAudioChapters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookAudioTrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    StartTimeSeconds = table.Column<double>(type: "REAL", nullable: false),
                    EndTimeSeconds = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAudioChapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookAudioChapters_BookAudioTracks_BookAudioTrackId",
                        column: x => x.BookAudioTrackId,
                        principalTable: "BookAudioTracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookAudioChapters_BookAudioTrackId",
                table: "BookAudioChapters",
                column: "BookAudioTrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookAudioChapters");
        }
    }
}
