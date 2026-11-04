using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookMetadataCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookMetadataCache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Authors = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Isbn = table.Column<string>(type: "TEXT", nullable: true),
                    AverageRating = table.Column<double>(type: "REAL", nullable: true),
                    TotalPages = table.Column<int>(type: "INTEGER", nullable: true),
                    Categories = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesName = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesVolume = table.Column<double>(type: "REAL", nullable: true),
                    CoverImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    IsMetadataComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    CachedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookMetadataCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookMetadataCache_ContentHash",
                table: "BookMetadataCache",
                column: "ContentHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookMetadataCache");
        }
    }
}
