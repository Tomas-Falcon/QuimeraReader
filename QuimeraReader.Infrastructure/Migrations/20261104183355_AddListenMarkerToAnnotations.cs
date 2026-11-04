using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListenMarkerToAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AudioPosition",
                table: "BookAnnotations",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioTrackNumber",
                table: "BookAnnotations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "BookAnnotations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioPosition",
                table: "BookAnnotations");

            migrationBuilder.DropColumn(
                name: "AudioTrackNumber",
                table: "BookAnnotations");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BookAnnotations");
        }
    }
}
