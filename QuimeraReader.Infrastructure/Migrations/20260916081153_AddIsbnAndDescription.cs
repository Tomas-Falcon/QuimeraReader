using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsbnAndDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Isbn",
                table: "Books",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Isbn",
                table: "Books");
        }
    }
}
