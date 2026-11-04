using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHashAndScannedFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScannedSourceFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannedSourceFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Books_ContentHash",
                table: "Books",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedSourceFiles_Path",
                table: "ScannedSourceFiles",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScannedSourceFiles");

            migrationBuilder.DropIndex(
                name: "IX_Books_ContentHash",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Books");
        }
    }
}
