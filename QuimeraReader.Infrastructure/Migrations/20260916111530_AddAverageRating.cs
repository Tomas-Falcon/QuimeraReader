using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAverageRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AverageRating",
                table: "Books",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "Books");
        }
    }
}
