using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryMCPServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRichBookDetailsAndLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Books",
                type: "nvarchar(1500)",
                maxLength: 1500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Books",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "Books",
                type: "int",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.AddColumn<double>(
                name: "Rating",
                table: "Books",
                type: "float",
                nullable: false,
                defaultValue: 4.5);

            migrationBuilder.CreateTable(
                name: "BookLoans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    BorrowerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BorrowedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReturnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsReturned = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookLoans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookLoans_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Books_Rating",
                table: "Books",
                column: "Rating");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Books_PageCount",
                table: "Books",
                sql: "[PageCount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Books_Rating",
                table: "Books",
                sql: "[Rating] >= 0.0 AND [Rating] <= 5.0");

            migrationBuilder.CreateIndex(
                name: "IX_BookLoans_BookId",
                table: "BookLoans",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_BookLoans_BorrowerName",
                table: "BookLoans",
                column: "BorrowerName");

            migrationBuilder.CreateIndex(
                name: "IX_BookLoans_IsReturned",
                table: "BookLoans",
                column: "IsReturned");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookLoans");

            migrationBuilder.DropIndex(
                name: "IX_Books_Rating",
                table: "Books");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Books_PageCount",
                table: "Books");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Books_Rating",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Books");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Books",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1500)",
                oldMaxLength: 1500);
        }
    }
}
