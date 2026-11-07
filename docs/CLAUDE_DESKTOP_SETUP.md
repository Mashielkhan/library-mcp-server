# Claude Desktop Setup & Integration Guide

This guide walks you through connecting **Claude Desktop** to the published local **LibraryMCPServer**.

---

## 1. Locate the Claude Desktop MCP Configuration

Claude Desktop stores its MCP configuration in your Windows user profile:

```text
%APPDATA%\Claude\claude_desktop_config.json
```
Full path example:
```text
C:\Users\<YourUsername>\AppData\Roaming\Claude\claude_desktop_config.json
```

> **Tip:** You can open this file directly from PowerShell:
> ```powershell
> notepad "$env:APPDATA\Claude\claude_desktop_config.json"
> ```
> If the `Claude` folder does not exist yet, create it:
> ```powershell
> New-Item -ItemType Directory -Path "$env:APPDATA\Claude" -Force
> ```

---

## 2. Edit `claude_desktop_config.json`

Add `library-mcp-server` to the `mcpServers` object.

### Complete Configuration Example

```json
{
  "mcpServers": {
    "library-mcp-server": {
      "command": "C:\\Users\\hp\\Documents\\Projects\\MCP Server\\publish\\win-x64\\LibraryMCPServer.exe",
      "args": []
    }
  }
}
```

### Preserving Existing MCP Servers
If you already have other MCP servers configured in `claude_desktop_config.json`, simply append `library-mcp-server` inside `mcpServers`:

```json
{
  "mcpServers": {
    "existing-server": {
      "command": "node",
      "args": ["C:\\path\\to\\server.js"]
    },
    "library-mcp-server": {
      "command": "C:\\Users\\hp\\Documents\\Projects\\MCP Server\\publish\\win-x64\\LibraryMCPServer.exe",
      "args": []
    }
  }
}
```

---

## 3. Verify and Restart Claude Desktop

1. Save `claude_desktop_config.json`.
2. **Quit Claude Desktop completely**:
   - Right-click the Claude icon in the Windows Notification Area / System Tray and select **Quit**.
   - (Simply closing the window does not terminate background MCP server host processes).
3. Relaunch Claude Desktop.
4. Look for the **plug / hammer / tools icon** in the bottom right corner of the chat input.
5. Click the tools icon. You should see `library-mcp-server` listed with the following registered tools:
   - `search_books`
   - `get_books_by_genre`
   - `get_library_stats`
   - `get_book_details`
   - `list_available_genres`
   - `get_low_stock_books`

---

## 4. Example Prompts to Test with Claude Desktop

### Test Prompt 1: Tool Discovery
> *"What library tools are available on my local server?"*
- **Expected Action**: Claude identifies and summarizes the tools exposed by `LibraryMCPServer`.

### Test Prompt 2: Catalog Statistics
> *"How many books are in the library, and what are the top genres?"*
- **Expected Action**: Claude executes `get_library_stats` and summarizes the total book count, available copies, and breakdown by genre.

### Test Prompt 3: Genre Query
> *"Show me 10 Fantasy books available in the catalog."*
- **Expected Action**: Claude executes `get_books_by_genre(genre: "Fantasy")` and presents the first 10 matching books with total count.

### Test Prompt 4: Title / Author Search
> *"Search for books by George Orwell or containing the word Python in their titles."*
- **Expected Action**: Claude executes `search_books(searchTerm: "George Orwell")` or `search_books(searchTerm: "Python")` and displays real records from the SQL database.

### Test Prompt 5: Low Inventory
> *"Are there any books in the library that have low stock?"*
- **Expected Action**: Claude invokes `get_low_stock_books(threshold: 3)` and lists books with depleted copies.

---

## 5. Troubleshooting Connection Issues

| Symptom | Cause | Solution |
|---|---|---|
| Claude Desktop shows an error loading tools | Executable path typo or file missing | Verify file exists at `C:\Users\hp\Documents\Projects\MCP Server\publish\win-x64\LibraryMCPServer.exe`. |
| Server fails to start | Missing runtime dependency | Ensure the published build was `--self-contained true`. Verify `appsettings.json` is located in the same directory. |
| Tool invocation returns database error | SQL Server is not running | Start LocalDB (`sqllocaldb start mssqllocaldb`) or SQL Server service. |
| Diagnostics check | Inspect Claude logs | Check log files in `%APPDATA%\Claude\logs\mcp*.log`. |
