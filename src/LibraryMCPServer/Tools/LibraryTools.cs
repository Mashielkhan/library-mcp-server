using LibraryMCPServer.Models;
using LibraryMCPServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace LibraryMCPServer.Tools;

/// <summary>
/// Exposes enterprise MCP tools for authentic catalog querying, typo-tolerant search,
/// live book checkout/returns, and real Open Library API importing.
/// </summary>
[McpServerToolType]
public sealed class LibraryTools
{
    private readonly ILibraryService _libraryService;
    private readonly OpenLibraryService _openLibraryService;
    private readonly ILogger<LibraryTools> _logger;

    public LibraryTools(
        ILibraryService libraryService,
        OpenLibraryService openLibraryService,
        ILogger<LibraryTools> logger)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _openLibraryService = openLibraryService ?? throw new ArgumentNullException(nameof(openLibraryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Searches books by title, author, or genre keywords with typo-tolerance and pagination.
    /// </summary>
    [McpServerTool(Name = "search_books", ReadOnly = true, Idempotent = true)]
    [Description("Search for authentic books in the library catalog by title, author, or genre (e.g., 'George Orwell', 'Python', 'Dystopian'). Supports typo tolerance, real ratings, and pagination.")]
    public async Task<object> SearchBooksAsync(
        [Description("The search keyword to look for in book titles, authors, or genres (e.g., 'George Orwell', 'Python').")] string searchTerm,
        [Description("The page number for paginated results (defaults to 1).")] int page = 1,
        [Description("The number of results per page (defaults to 10, maximum 50).")] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return new { error = "Search term cannot be empty. Please provide a search keyword." };
        }

        try
        {
            var result = await _libraryService.SearchBooksAsync(searchTerm, page, pageSize, cancellationToken);
            if (result.TotalCount == 0)
            {
                return new
                {
                    message = $"No books found matching '{searchTerm}'.",
                    totalCount = 0,
                    page = result.Page,
                    totalPages = 0,
                    hasMore = false,
                    books = Array.Empty<BookSummaryDto>()
                };
            }

            return new
            {
                message = $"Found {result.TotalCount} matching book(s) for '{searchTerm}'. Showing page {result.Page} of {result.TotalPages}.",
                totalCount = result.TotalCount,
                page = result.Page,
                pageSize = result.PageSize,
                totalPages = result.TotalPages,
                hasMore = result.HasMore,
                books = result.Items
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search books for term: '{SearchTerm}'", searchTerm);
            return new { error = "A database error occurred while searching for books. Please ensure the database is accessible." };
        }
    }

    /// <summary>
    /// Advanced multi-criteria search filtering by title, genre, author, publication years, and rating.
    /// </summary>
    [McpServerTool(Name = "search_books_advanced", ReadOnly = true, Idempotent = true)]
    [Description("Advanced search with multiple filters: title keywords, genre, author, publication year range, and minimum rating (e.g. 4.5+).")]
    public async Task<object> SearchBooksAdvancedAsync(
        [Description("Optional search keyword to find in book titles or descriptions.")] string? query = null,
        [Description("Optional genre category to filter by (e.g. 'Fantasy', 'Programming', 'Satire').")] string? genre = null,
        [Description("Optional author name to filter by.")] string? author = null,
        [Description("Optional minimum publication year (e.g. 2000).")] int? minYear = null,
        [Description("Optional maximum publication year (e.g. 2024).")] int? maxYear = null,
        [Description("Optional minimum book rating from 0.0 to 5.0 (e.g. 4.7).")] double? minRating = null,
        [Description("Page number (defaults to 1).")] int page = 1,
        [Description("Page size (defaults to 10).")] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _libraryService.SearchBooksAdvancedAsync(query, genre, author, minYear, maxYear, minRating, page, pageSize, cancellationToken);
            return new
            {
                message = $"Advanced search matched {result.TotalCount} books. Page {result.Page} of {result.TotalPages}.",
                totalCount = result.TotalCount,
                page = result.Page,
                pageSize = result.PageSize,
                totalPages = result.TotalPages,
                hasMore = result.HasMore,
                books = result.Items
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed advanced book search");
            return new { error = "A database error occurred during advanced book search." };
        }
    }

    /// <summary>
    /// Retrieves paginated books for a specific genre with total matching counts and ratings.
    /// </summary>
    [McpServerTool(Name = "get_books_by_genre", ReadOnly = true, Idempotent = true)]
    [Description("Retrieve authentic books belonging to a specified genre (e.g., 'Fantasy', 'Science Fiction', 'Programming', 'Satire'). Returns books with ratings and cover URLs.")]
    public async Task<object> GetBooksByGenreAsync(
        [Description("The genre category to filter by (e.g., 'Fantasy', 'Science Fiction', 'Programming', 'Classic Fiction').")] string genre,
        [Description("Page number for pagination (defaults to 1).")] int page = 1,
        [Description("Number of records per page (defaults to 10).")] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            return new { error = "Genre must not be empty. Please specify a valid genre category." };
        }

        try
        {
            var result = await _libraryService.GetBooksByGenreAsync(genre, page, pageSize, cancellationToken);
            if (result.TotalMatching == 0)
            {
                return new
                {
                    message = $"No books found for genre '{genre}'.",
                    genre = result.Genre,
                    totalCount = 0,
                    totalMatching = 0,
                    page = result.Page,
                    totalPages = 0,
                    hasMore = false,
                    books = Array.Empty<BookSummaryDto>()
                };
            }

            return new
            {
                message = $"Found {result.TotalMatching} books in genre '{result.Genre}'. Displaying page {result.Page} of {result.TotalPages}.",
                genre = result.Genre,
                 totalCount = result.TotalCount,
                 totalMatching = result.TotalMatching,
                page = result.Page,
                pageSize = result.PageSize,
                totalPages = result.TotalPages,
                hasMore = result.HasMore,
                books = result.Books
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query books for genre: '{Genre}'", genre);
            return new { error = "A database error occurred while querying books by genre." };
        }
    }

    /// <summary>
    /// Computes and returns library catalog statistics.
    /// </summary>
    [McpServerTool(Name = "get_library_stats", ReadOnly = true, Idempotent = true)]
    [Description("Calculate comprehensive statistics about the authentic library catalog, including total titles, physical copies, active loans, and genre breakdown.")]
    public async Task<object> GetLibraryStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _libraryService.GetLibraryStatsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve library statistics.");
            return new { error = "A database error occurred while calculating library statistics." };
        }
    }

    /// <summary>
    /// Retrieves comprehensive details for a specific book by ID, including ratings, pages, covers, and loan status.
    /// </summary>
    [McpServerTool(Name = "get_book_details", ReadOnly = true, Idempotent = true)]
    [Description("Retrieve comprehensive details for a specific book by its unique ID, including ISBN, full description, rating, page count, cover URL, and active loan status.")]
    public async Task<object> GetBookDetailsAsync(
        [Description("The unique integer ID of the book.")] int bookId,
        CancellationToken cancellationToken = default)
    {
        if (bookId <= 0)
        {
            return new { error = "Invalid book ID. Book ID must be a positive integer greater than zero." };
        }

        try
        {
            var book = await _libraryService.GetBookDetailsAsync(bookId, cancellationToken);
            if (book == null)
            {
                return new { message = $"No book found with ID {bookId}." };
            }

            return book;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve details for book ID {BookId}", bookId);
            return new { error = "A database error occurred while retrieving book details." };
        }
    }

    /// <summary>
    /// Borrows / checks out a book for a user.
    /// </summary>
    [McpServerTool(Name = "borrow_book", ReadOnly = false, Idempotent = false)]
    [Description("Checks out / borrows a copy of a book from the library for a specified borrower. Decrements inventory and creates an active loan with a due date.")]
    public async Task<object> BorrowBookAsync(
        [Description("The unique integer ID of the book to borrow.")] int bookId,
        [Description("The full name or identifier of the borrower.")] string borrowerName,
        [Description("Number of days for the loan (default 14 days, max 60).")] int loanDays = 14,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _libraryService.BorrowBookAsync(bookId, borrowerName, loanDays, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to borrow book ID {BookId} for {Borrower}", bookId, borrowerName);
            return new { success = false, error = "A database error occurred while processing the checkout." };
        }
    }

    /// <summary>
    /// Returns a borrowed book, restoring available copies.
    /// </summary>
    [McpServerTool(Name = "return_book", ReadOnly = false, Idempotent = false)]
    [Description("Returns a borrowed book back to the library. Marks the loan as returned and increments available inventory.")]
    public async Task<object> ReturnBookAsync(
        [Description("The unique integer ID of the book being returned.")] int bookId,
        [Description("The full name or identifier of the borrower returning the book.")] string borrowerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _libraryService.ReturnBookAsync(bookId, borrowerName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to return book ID {BookId} for {Borrower}", bookId, borrowerName);
            return new { success = false, error = "A database error occurred while processing the return." };
        }
    }

    /// <summary>
    /// Lists all active (unreturned) loans.
    /// </summary>
    [McpServerTool(Name = "get_active_loans", ReadOnly = true, Idempotent = true)]
    [Description("Lists all books currently checked out / on loan with borrower names and due dates.")]
    public async Task<object> GetActiveLoansAsync(
        [Description("Page number (defaults to 1).")] int page = 1,
        [Description("Number of loan records per page (defaults to 10, maximum 50).")] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _libraryService.GetActiveLoansAsync(page, pageSize, cancellationToken);
            return new
            {
                totalCount = result.TotalCount,
                page = result.Page,
                pageSize = result.PageSize,
                totalPages = result.TotalPages,
                hasMore = result.HasMore,
                loans = result.Items
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve active loans");
            return new { error = "A database error occurred while retrieving active loans." };
        }
    }

    /// <summary>
    /// Lists all distinct genres currently in the library catalog.
    /// </summary>
    [McpServerTool(Name = "list_available_genres", ReadOnly = true, Idempotent = true)]
    [Description("Lists all distinct book genres currently present in the library catalog.")]
    public async Task<object> ListAvailableGenresAsync(
        [Description("Page number (defaults to 1).")] int page = 1,
        [Description("Number of genres per page (defaults to 10, maximum 50).")] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _libraryService.ListAvailableGenresAsync(page, pageSize, cancellationToken);
            return new { totalCount = result.TotalCount, page = result.Page, pageSize = result.PageSize, totalPages = result.TotalPages, hasMore = result.HasMore, genres = result.Items };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list available genres.");
            return new { error = "A database error occurred while listing library genres." };
        }
    }

    /// <summary>
    /// Retrieves books with available inventory at or below the given threshold.
    /// </summary>
    [McpServerTool(Name = "get_low_stock_books", ReadOnly = true, Idempotent = true)]
    [Description("Find books with low available copies (at or below the threshold, default 5) for library inventory restocking.")]
    public async Task<object> GetLowStockBooksAsync(
        [Description("Maximum number of available copies to consider as low stock. Defaults to 5.")] int threshold = 5,
        [Description("Page number (defaults to 1).") ] int page = 1,
        [Description("Number of records per page (defaults to 10, maximum 50).") ] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _libraryService.GetLowStockBooksAsync(threshold, page, pageSize, cancellationToken);
            return new
            {
                threshold,
                totalCount = result.TotalCount,
                page = result.Page,
                pageSize = result.PageSize,
                totalPages = result.TotalPages,
                hasMore = result.HasMore,
                books = result.Items
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve low stock books.");
            return new { error = "A database error occurred while checking low stock books." };
        }
    }

    /// <summary>
    /// Live import of real published books directly from Open Library REST API.
    /// </summary>
    [McpServerTool(Name = "import_books_from_open_library", ReadOnly = false, Idempotent = false)]
    [Description("Searches the public Open Library API live and imports real published books into your local SQL Server catalog with real covers, page counts, descriptions, and ISBNs.")]
    public async Task<object> ImportBooksFromOpenLibraryAsync(
        [Description("The search query or author name (e.g., 'Stephen King', 'Isaac Asimov', 'Quantum Computing').")] string query,
        [Description("Maximum number of books to import (default 10, max 30).")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { success = false, error = "Query keyword cannot be empty." };
        }

        try
        {
            var (importedCount, titles) = await _openLibraryService.ImportBooksAsync(query, limit, cancellationToken);
            return new
            {
                success = true,
                message = $"Successfully imported {importedCount} authentic published book(s) from Open Library API.",
                importedCount,
                titles
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import books from Open Library API for query: '{Query}'", query);
            return new { success = false, error = "Failed to import books from Open Library. Please verify internet connectivity." };
        }
    }

    [McpServerTool(Name = "search_open_library", ReadOnly = true, Idempotent = true)]
    [Description("Searches Open Library live without writing to the local database and marks each result with inLibrary.")]
    public async Task<object> SearchOpenLibraryAsync(string query, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new { error = "Query cannot be empty." };
        return new { query, page, pageSize, results = await _openLibraryService.SearchOpenLibraryAsync(query, page, pageSize, cancellationToken) };
    }

    [McpServerTool(Name = "add_book_by_isbn", ReadOnly = false, Idempotent = true)]
    [Description("Fetches and validates one Open Library edition by ISBN-13 and upserts it into the local catalog.")]
    public async Task<object> AddBookByIsbnAsync(string isbn, int copies = 1, CancellationToken cancellationToken = default)
    {
        try { return await _openLibraryService.AddBookByIsbnAsync(isbn, copies, cancellationToken); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to add Open Library ISBN {Isbn}", isbn); return new { error = ex.Message }; }
    }
}
