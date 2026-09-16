using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CurrentAudioPosition",
                table: "Books",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentEpubCfi",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PercentageCompleted",
                table: "Books",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentAudioPosition",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CurrentEpubCfi",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "PercentageCompleted",
                table: "Books");
        }
    }
}
