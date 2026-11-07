using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryMCPServer.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeOpenLibraryGenres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Subjects",
                table: "Books",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE Books SET Subjects = Genre");
            migrationBuilder.Sql(@"
UPDATE Books
SET Genre = CASE
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%science fiction%' THEN 'Science Fiction'
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%program%' THEN 'Programming'
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%technology%' OR LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%computer%' THEN 'Technology'
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%self-help%' OR LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%self help%' THEN 'Self-Help'
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%business%' THEN 'Business'
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%mystery%' OR LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%detective%' THEN 'Mystery'
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%romance%' THEN 'Romance'
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%biograph%' THEN 'Biography'
    WHEN LOWER(LTRIM(RTRIM(LEFT(Genre, CHARINDEX(',', Genre + ',') - 1)))) LIKE '%fantasy%' THEN 'Fantasy'
    ELSE 'History'
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Subjects",
                table: "Books");
        }
    }
}
