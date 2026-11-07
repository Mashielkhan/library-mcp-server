USE [LibraryDb];
GO

SELECT DB_NAME() AS DatabaseName;
SELECT COUNT(*) AS BookCount FROM dbo.Books;
SELECT COUNT(*) AS LoanCount FROM dbo.BookLoans;

SELECT TOP (25)
	Id,
	Title,
	Author,
	Genre,
	PublishedYear,
	ISBN,
	CopiesAvailable,
	Rating,
	IsSample
FROM dbo.Books
ORDER BY Title;

SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
ORDER BY MigrationId;