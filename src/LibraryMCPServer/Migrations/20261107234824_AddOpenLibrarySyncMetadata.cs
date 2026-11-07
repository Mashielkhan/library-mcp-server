using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryMCPServer.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenLibrarySyncMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "Books",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenLibraryKey",
                table: "Books",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_OpenLibraryKey",
                table: "Books",
                column: "OpenLibraryKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Books_OpenLibraryKey",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "OpenLibraryKey",
                table: "Books");
        }
    }
}
