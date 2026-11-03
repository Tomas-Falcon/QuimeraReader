using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversesToBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Series_Universes_UniverseId",
                table: "Series");

            migrationBuilder.AddColumn<int>(
                name: "UniverseId",
                table: "Books",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Series_Name",
                table: "Series",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Books_UniverseId",
                table: "Books",
                column: "UniverseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Universes_UniverseId",
                table: "Books",
                column: "UniverseId",
                principalTable: "Universes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_Universes_UniverseId",
                table: "Series",
                column: "UniverseId",
                principalTable: "Universes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Universes_UniverseId",
                table: "Books");

            migrationBuilder.DropForeignKey(
                name: "FK_Series_Universes_UniverseId",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_Name",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Books_UniverseId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "UniverseId",
                table: "Books");

            migrationBuilder.AddForeignKey(
                name: "FK_Series_Universes_UniverseId",
                table: "Series",
                column: "UniverseId",
                principalTable: "Universes",
                principalColumn: "Id");
        }
    }
}
