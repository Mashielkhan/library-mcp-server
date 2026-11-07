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

The first normal startup applies pending EF migrations and adds the curated catalog when `LibrarySettings:EnableAutoMigrationAndSeed` is enabled. It does not delete existing rows.

### Connect with SQL Server Management Studio

Use these SSMS connection settings on the development machine:

- **Server type:** Database Engine
- **Server name:** `./SQLEXPRESS01`
- **Authentication:** Windows Authentication
- **Database:** `LibraryDb`

The application uses the same server in `appsettings.json`. Open `scripts/InspectLibraryDb.sql` in SSMS to inspect counts, books, and applied migrations.

## Open Library import

For bulk catalog expansion, download the free monthly Open Library editions and authors dumps from https://openlibrary.org/developers/dumps. Build the author lookup first, then load the editions dump. The loader reads gzip files line by line and never uses the Open Library API for bulk ingestion.

Goodbooks-10k ratings can be loaded from its free `books.csv` dataset (commonly distributed through the Goodreads/Goodbooks-10k dataset repository). Check the dataset's current license and terms yourself before using or redistributing it.

The seed command imports books across these ten normalized genres:

- Mystery
- Romance
- Biography
- History
- Fantasy
- Business
- Self-Help
- Programming
- Technology
- Science Fiction

`Books.Genre` always contains exactly one of those values. Raw Open Library subject tags are stored separately in `Books.Subjects` (`nvarchar(max)`). The `NormalizeOpenLibraryGenres` migration populated `Subjects` for existing rows and normalized their previous genre values.

The importer:

- Validates and normalizes ISBN-13 values.
- Skips records missing a title, author, ISBN-13, or publication year.
- Uses specific queries and paged result offsets for Business, Self-Help, Programming, Technology, and Science Fiction.
- Rejects metadata subjects beginning with `nyt:`, `collectionID:`, or `series:`.
- Rejects juvenile and adult-explicit subjects by default (`Juvenile`, `erotica`, `BDSM`).
- Rejects configured nonfiction markers for Fantasy and Romance.
- Uses the API rating when available and otherwise stores `null`.
- Updates an existing ISBN in place on re-import instead of inserting a duplicate or adding copies.
- Preserves `CopiesAvailable` on existing records and uses the fixed default only for new records.
- Retries transient API failures and rate-limits requests.

Run approximately 1,000 target records:

```powershell
.\scripts\SeedOpenLibrary.ps1 -TargetCount 1000
```

Or invoke the command directly:

```powershell
$env:OPEN_LIBRARY_TARGET_COUNT = 1000
dotnet run --project src/LibraryMCPServer/LibraryMCPServer.csproj -- --seed-open-library
```

Configuration options:

```text
FilterJuvenileAndAdultContent=true
DefaultCopiesAvailable=5
INCLUDE_SAMPLE_DATA=false
```

Use `LibrarySettings__FilterJuvenileAndAdultContent=false` to turn off juvenile/adult filtering. Set `INCLUDE_SAMPLE_DATA=true` or `LibrarySettings__IncludeSampleData=true` to include generated sample rows in catalog queries.

### Free-source bulk commands

Download the authors and editions dump files yourself; they are large (the editions dump is several GB compressed) and are intentionally ignored by Git. Then run:

```powershell
dotnet run --project src/LibraryMCPServer/LibraryMCPServer.csproj -- --load-open-library-authors C:\data\ol_dump_authors.txt.gz C:\data\authors.lookup.jsonl
dotnet run --project src/LibraryMCPServer/LibraryMCPServer.csproj -- --load-open-library-dump C:\data\ol_dump_editions.txt.gz --authors-lookup C:\data\authors.lookup.jsonl --max-rows 200000
dotnet run --project src/LibraryMCPServer/LibraryMCPServer.csproj -- --load-ratings C:\data\books.csv
```

The editions loader reports rows read, kept, skip reasons, inserts, updates, and per-genre counts. It is safe to rerun by ISBN, does not touch loans, and stops before the database reaches 8 GB. SQL Server Express has a 10 GB database-file limit, so leave headroom for indexes and normal operation.

### Verified import results

The 1,000-target import was run against `./SQLEXPRESS01/LibraryDb` and produced 1,433 distinct ISBN rows. All ten genres met the 80-book minimum:

| Genre | Books |
| --- | ---: |
| Biography | 188 |
| Business | 99 |
| Fantasy | 163 |
| History | 168 |
| Mystery | 171 |
| Programming | 104 |
| Romance | 198 |
| Science Fiction | 133 |
| Self-Help | 109 |
| Technology | 100 |

Validation confirmed zero invalid Genre values, zero missing Subjects, and no duplicate ISBN rows. Rows imported before the filtering fix were preserved; newly imported rows use the default filters.

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
- `scripts/SeedOpenLibrary.ps1` — repeatable Open Library seed command
- `scripts/InspectLibraryDb.sql` — SSMS inspection queries
- `tests/LibraryMCPServer.Tests` — unit and tool contract tests
