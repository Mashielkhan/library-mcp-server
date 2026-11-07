using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryMCPServer.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAuthenticColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAuthentic",
                table: "Books",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAuthentic",
                table: "Books");
        }
    }
}
