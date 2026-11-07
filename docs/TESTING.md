# Testing Guide — LibraryMCPServer

This document details the automated test suite, interactive testing with the official **MCP Inspector**, and direct command-line verification.

---

## 1. Automated Test Suite

The solution includes an extensive automated test suite (`LibraryMCPServer.Tests`) with **40 automated tests** covering all core layers:

```powershell
dotnet test "C:\Users\hp\Documents\Projects\MCP Server\tests\LibraryMCPServer.Tests\LibraryMCPServer.Tests.csproj"
```

### Test Coverage Breakdown

| Test Class | Focus Area | Key Verifications |
|---|---|---|
| `EntityValidationTests` | Data Annotations & Constraints | Title/Author/Genre required, string lengths, year bounds (1000–2100), non-negative copies. |
| `DbContextConfigurationTests` | EF Core Model Metadata | Table name `Books`, unique index on `ISBN`, search indexes on `Title`, `Author`, `Genre`, check constraint `CK_Books_CopiesAvailable`. |
| `SeederTests` | Bogus Generation & Idempotency | Exactly 5,000 records generated, 100% unique ISBNs, deterministic seeds, duplicate insertion prevention on non-empty databases. |
| `LibraryServiceTests` | LINQ Query Logic | Case-insensitive title and author searching, deterministic sorting, result limits (10), total match vs. displayed pagination, aggregate statistics, low-stock filtering. |
| `LibraryToolsTests` | MCP Tool Schemas & Handlers | Structured response payloads, safe user-facing error messages on null/empty inputs, exception containment without stack trace leaks. |
| `SqlServerIntegrationTests` | SQL Server Execution | Direct integration test against SQL Server / LocalDB instances when available, with explicit skip reporting when offline. |

---

## 2. Interactive Testing with the Official MCP Inspector

The official MCP Inspector provides an interactive developer UI to inspect tool schemas and execute tools against local servers.

### Prerequisites
- Node.js and npm (installed and accessible in PowerShell)
- Verified with Node v26.13.0 and npm 11.20.0

### Starting the MCP Inspector

Run the following command in PowerShell:

```powershell
npx @modelcontextprotocol/inspector "C:\Users\hp\Documents\Projects\MCP Server\publish\win-x64\LibraryMCPServer.exe"
```

### Using the Inspector Web UI

1. The inspector will launch a local server and provide a URL (typically `http://localhost:5173` or similar). Open this in your browser.
2. Select **Transport Type**: `stdio`.
3. Click **Connect**.
4. Navigate to the **Tools** tab:
   - Verify all 6 tools are discovered: `search_books`, `get_books_by_genre`, `get_library_stats`, `get_book_details`, `list_available_genres`, `get_low_stock_books`.
   - Inspect the input JSON schema for each tool.
5. **Execute `get_library_stats`**:
   - Click on `get_library_stats` -> Click **Run Tool**.
   - Inspect the returned JSON containing total books (5,000), total copies, and the genre breakdown.
6. **Execute `search_books`**:
   - Provide `searchTerm`: `"George Orwell"`.
   - Click **Run Tool**.
   - Verify matching records are returned with title, author, genre, published year, and copies available.
7. **Execute `get_books_by_genre`**:
   - Provide `genre`: `"Fantasy"`.
   - Click **Run Tool**.
   - Verify `totalMatching` matches the database count while exactly 10 records are displayed.

---

## 3. Direct Command-Line Protocol Testing (PowerShell)

You can verify the stdio protocol without third-party tools using the following PowerShell command:

```powershell
$init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0.0"}}}'
$list = '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'

$pinfo = New-Object System.Diagnostics.ProcessStartInfo
$pinfo.FileName = "C:\Users\hp\Documents\Projects\MCP Server\publish\win-x64\LibraryMCPServer.exe"
$pinfo.RedirectStandardInput = $true
$pinfo.RedirectStandardOutput = $true
$pinfo.RedirectStandardError = $true
$pinfo.UseShellExecute = $false
$pinfo.WorkingDirectory = "C:\Users\hp\Documents\Projects\MCP Server\publish\win-x64"

$p = New-Object System.Diagnostics.Process
$p.StartInfo = $pinfo
$p.Start() | Out-Null

# Send initialize
$p.StandardInput.WriteLine($init)
$res1 = $p.StandardOutput.ReadLine()
Write-Host "INITIALIZE RESPONSE: $res1" -ForegroundColor Green

# Send tools/list
$p.StandardInput.WriteLine($list)
$res2 = $p.StandardOutput.ReadLine()
Write-Host "TOOLS LIST: $res2" -ForegroundColor Cyan

$p.Kill()
```
