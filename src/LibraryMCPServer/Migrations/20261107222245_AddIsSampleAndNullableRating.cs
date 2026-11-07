using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryMCPServer.Migrations
{
    /// <inheritdoc />
    public partial class AddIsSampleAndNullableRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "Rating",
                table: "Books",
                type: "float",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "float",
                oldDefaultValue: 4.5);

            migrationBuilder.AddColumn<bool>(
                name: "IsSample",
                table: "Books",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE Books SET IsSample = 1 WHERE Title LIKE 'Awesome %' OR Description LIKE '%lorem ipsum%' OR Description LIKE '%Lorem ipsum%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSample",
                table: "Books");

            migrationBuilder.AlterColumn<double>(
                name: "Rating",
                table: "Books",
                type: "float",
                nullable: false,
                defaultValue: 4.5,
                oldClrType: typeof(double),
                oldType: "float",
                oldNullable: true);
        }
    }
}
