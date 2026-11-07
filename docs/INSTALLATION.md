# Installation & Setup Guide — LibraryMCPServer

## 1. Prerequisites

Before running the server, ensure the following are installed on your Windows system:

| Dependency | Minimum Version | Verified Version | Purpose |
|---|---|---|---|
| **.NET SDK** | 8.0 LTS | 8.0.427 | Compiles and executes C# MCP server |
| **Node.js & npm** | 18.0+ | Node v26.13.0, npm 11.20.0 | Runs `@modelcontextprotocol/inspector` |
| **Microsoft SQL Server** | 2017+ or LocalDB 2019+ | LocalDB / Express / Docker | Relational database storage |

---

## 2. Setting Up SQL Server on Windows

If you do not have SQL Server or LocalDB running yet, choose one of the following three options:

### Option A: Microsoft SQL Server LocalDB (Recommended for Windows Development)

LocalDB is a lightweight execution mode of SQL Server Express designed for developers.

1. **Install LocalDB via winget (PowerShell as Administrator)**:
   ```powershell
   winget install Microsoft.SQLServer.2022.LocalDB
   ```
2. **Create and start the default instance**:
   ```powershell
   sqllocaldb create mssqllocaldb
   sqllocaldb start mssqllocaldb
   sqllocaldb info mssqllocaldb
   ```
3. **Verify the connection string**:
   The default `appsettings.json` is already configured for LocalDB:
   ```text
   Server=(localdb)\mssqllocaldb;Database=LibraryDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
   ```

### Option B: SQL Server Express

1. **Install SQL Server Express via winget**:
   ```powershell
   winget install Microsoft.SQLServer.2022.Express
   ```
2. **Update connection string in `appsettings.json`**:
   ```json
   "ConnectionStrings": {
     "LibraryDb": "Server=.\\SQLEXPRESS;Database=LibraryDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
   }
   ```

### Option C: Docker Container (Cross-Platform)

If Docker Desktop is running:
```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=LibrarySecret123!" -p 1433:1433 --name library-sql -d mcr.microsoft.com/mssql/server:2022-latest
```
Set the environment variable or `appsettings.json`:
```json
"ConnectionStrings": {
  "LibraryDb": "Server=localhost,1433;Database=LibraryDb;User Id=sa;Password=LibrarySecret123!;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

---

## 3. Building the Solution

From the solution root `C:\Users\hp\Documents\Projects\MCP Server`:

```powershell
# Restore all dependencies
dotnet restore

# Build solution in Release mode
dotnet build LibraryMCPServer.sln -c Release

# Run automated test suite
dotnet test tests\LibraryMCPServer.Tests\LibraryMCPServer.Tests.csproj
```

---

## 4. Applying Database Migrations

Entity Framework Core migrations manage the `Books` table schema.

### Automatic Migration on Startup
By default, `EnableAutoMigrationAndSeed` is set to `true` in `appsettings.json`. When the server boots and detects an active database connection, it automatically applies migrations and seeds 5,000 fictional book records!

### Manual Migration Commands (PowerShell)
To manually check or apply migrations using the local EF Core tool:

```powershell
# Check pending migrations
dotnet ef migrations list --project src\LibraryMCPServer --startup-project src\LibraryMCPServer

# Apply migrations to target database
dotnet ef database update --project src\LibraryMCPServer --startup-project src\LibraryMCPServer

# Revert database if needed
dotnet ef database update 0 --project src\LibraryMCPServer --startup-project src\LibraryMCPServer
```

---

## 5. Publishing the Standalone Windows Executable

To generate a self-contained, single-file Windows x64 binary:

```powershell
dotnet publish .\src\LibraryMCPServer\LibraryMCPServer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o ".\publish\win-x64"
```

The output binary will be located at:
`C:\Users\hp\Documents\Projects\MCP Server\publish\win-x64\LibraryMCPServer.exe`
alongside `appsettings.json` and `Microsoft.Data.SqlClient.SNI.dll`.
