using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuimeraReader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsMetadataComplete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMetadataComplete",
                table: "Books",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMetadataComplete",
                table: "Books");
        }
    }
}
