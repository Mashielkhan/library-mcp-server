# Library MCP Server

A .NET 8 Model Context Protocol (MCP) server for a SQL Server-backed library catalog. It exposes read-only catalog search and inventory tools plus transactional borrowing and returns.

## Stack

- C# / .NET 8
- ModelContextProtocol 2.3.1 over stdio
- Entity Framework Core 8 with SQL Server
- xUnit and EF Core InMemory for tests

## Tools

| Tool | Parameters | Purpose |
| --- | --- | --- |
| `get_library_stats` | none | Counts visible books, available copies, loans, and genre aggregates. |
| `list_available_genres` | `page=1`, `pageSize=10` | Paged list of distinct genres. |
| `get_books_by_genre` | `genre`, `page=1`, `pageSize=10` | Paged book summaries with rating, pages, cover, and inventory. |
| `search_books` | `searchTerm`, `page=1`, `pageSize=10` | Case-insensitive title/author/genre search with fuzzy fallback. |
| `search_books_advanced` | `query`, `genre`, `author`, `minYear`, `maxYear`, `minRating`, `page`, `pageSize` | Multi-filter search; `minRating` is applied to both results and `totalCount`. |
| `get_book_details` | `bookId` | Full metadata and active-loan count, or a clear not-found message. |
| `get_low_stock_books` | `threshold=5`, `page=1`, `pageSize=10` | Paged inventory below the threshold. |
| `borrow_book` | `bookId`, `borrowerName`, `loanDays=14` | Decrements available copies and creates a loan. |
| `return_book` | `bookId`, `borrowerName` | Closes the matching active loan and restores a copy. |
| `get_active_loans` | `page=1`, `pageSize=10` | Paged active-loan list. |

List tools return `totalCount`, `page`, `pageSize`, `totalPages`, `hasMore`, and an item collection (`books`, `genres`, or `loans`). Page sizes are clamped to 1–50. Empty search terms, unknown IDs, zero inventory, and unmatched returns return structured errors/messages rather than throwing through MCP.

Example `search_books_advanced` response:

```json
{
  "message": "Advanced search matched 1 books. Page 1 of 1.",
  "totalCount": 1,
  "page": 1,
  "pageSize": 10,
  "totalPages": 1,
  "hasMore": false,
  "books": [{ "id": 1, "title": "Example", "rating": 4.8, "pageCount": 320 }]
}
```

## Setup

1. Install the .NET 8 SDK and SQL Server (LocalDB or SQL Server Express).
2. Copy `.env.example` to `.env` and set environment variables in the shell or your launch profile. The .NET host reads environment variables; it does not read `.env` automatically.
3. Set `ConnectionStrings__LibraryDb` to the database connection string.
4. Restore and test:

```powershell
dotnet restore LibraryMCPServer.sln
dotnet test LibraryMCPServer.sln
```

### Connect with SQL Server Management Studio

Use these SSMS connection settings on the development machine:

- **Server type:** Database Engine
- **Server name:** `.\SQLEXPRESS01`
- **Authentication:** Windows Authentication
- **Database:** `LibraryDb`

The application uses the same server in `appsettings.json`. EF Core applies pending migrations at startup. The verification queries in `scripts/InspectLibraryDb.sql` can be opened and executed in SSMS.

The first normal startup applies EF migrations and adds the curated authentic catalog when `LibrarySettings:EnableAutoMigrationAndSeed` is enabled. It does not delete existing rows.

## Real Open Library data

The seed command queries the Open Library Search API for these genres: Mystery, Romance, Biography, History, Fantasy, Business, Self-Help, Programming, Technology, and Science Fiction. It validates ISBN-13 check digits, skips missing title/author/ISBN/year records, stores API ratings only when present, retries transient failures, waits between requests, and aggregates duplicate ISBNs as additional copies.

Run approximately 1,000 imports:

```powershell
.\scripts\SeedOpenLibrary.ps1 -TargetCount 1000
```

Or invoke the command directly:

```powershell
$env:OPEN_LIBRARY_TARGET_COUNT = 1000
dotnet run --project src/LibraryMCPServer/LibraryMCPServer.csproj -- --seed-open-library
```

Sample/generated rows are marked `IsSample=true` by the migration and are hidden by default. Set `INCLUDE_SAMPLE_DATA=true` or `LibrarySettings__IncludeSampleData=true` to include them in queries.

## Run as an MCP server

```powershell
dotnet run --project src/LibraryMCPServer/LibraryMCPServer.csproj
```

The server uses stdio. Configure an MCP client such as Claude Desktop with an entry similar to:

```json
{
  "mcpServers": {
    "library": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/LibraryMCPServer/src/LibraryMCPServer/LibraryMCPServer.csproj"]
    }
  }
}
```

Keep protocol output on stdout; application logs are routed to stderr.

## Project layout

- `src/LibraryMCPServer/Tools` — MCP registration and tool methods
- `src/LibraryMCPServer/Services` — catalog, loan, and Open Library logic
- `src/LibraryMCPServer/Data` — EF Core context and seed data
- `src/LibraryMCPServer/Migrations` — database schema migrations
- `tests/LibraryMCPServer.Tests` — unit and tool contract tests
