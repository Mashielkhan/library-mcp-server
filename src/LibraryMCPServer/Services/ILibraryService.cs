using LibraryMCPServer.Models;

namespace LibraryMCPServer.Services;

/// <summary>
/// Service interface for querying, searching, and transacting library book records and loans.
/// </summary>
public interface ILibraryService
{
    /// <summary>
    /// Searches books matching the search term in title or author with fuzzy typo tolerance and pagination.
    /// </summary>
    Task<PaginatedResultDto<BookSummaryDto>> SearchBooksAsync(
        string searchTerm,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advanced multi-criteria filtering by title/keyword, genre, author, year range, and minimum rating.
    /// </summary>
    Task<PaginatedResultDto<BookSummaryDto>> SearchBooksAdvancedAsync(
        string? query = null,
        string? genre = null,
        string? author = null,
        int? minYear = null,
        int? maxYear = null,
        double? minRating = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves paginated books belonging to a specific genre.
    /// </summary>
    Task<GenreBooksResultDto> GetBooksByGenreAsync(
        string genre,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes aggregated catalog statistics with in-memory caching.
    /// </summary>
    Task<LibraryStatsDto> GetLibraryStatsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves comprehensive details for a specific book by its ID.
    /// </summary>
    Task<BookDetailDto?> GetBookDetailsAsync(
        int bookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all distinct genres currently present in the catalog.
    /// </summary>
    Task<PaginatedResultDto<string>> ListAvailableGenresAsync(
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves books with available inventory at or below the given threshold.
    /// </summary>
    Task<PaginatedResultDto<BookSummaryDto>> GetLowStockBooksAsync(
        int threshold = 5,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks out / borrows a copy of a book, decrementing inventory and recording an active loan.
    /// </summary>
    Task<LoanResultDto> BorrowBookAsync(
        int bookId,
        string borrowerName,
        int loanDays = 14,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a borrowed book, restoring inventory and closing the loan record.
    /// </summary>
    Task<LoanResultDto> ReturnBookAsync(
        int bookId,
        string borrowerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all currently active (unreturned) book loans.
    /// </summary>
    Task<PaginatedResultDto<LoanDto>> GetActiveLoansAsync(
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);
}
