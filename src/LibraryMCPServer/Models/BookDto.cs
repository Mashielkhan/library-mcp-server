namespace LibraryMCPServer.Models;

/// <summary>
/// Summary representation of a book returned by searches and listings, including cover URLs and ratings.
/// </summary>
public record BookSummaryDto(
    int Id,
    string Title,
    string Author,
    string Genre,
    int PublishedYear,
    int CopiesAvailable,
    double? Rating,
    int PageCount,
    string? CoverImageUrl,
    bool IsAuthentic
);

/// <summary>
/// Detailed representation of a book including ISBN, full description, rating, cover, and loan status.
/// </summary>
public record BookDetailDto(
    int Id,
    string Title,
    string Author,
    string Genre,
    int PublishedYear,
    string ISBN,
    int CopiesAvailable,
    double? Rating,
    int PageCount,
    string? CoverImageUrl,
    string Description,
    bool IsAuthentic,
    int ActiveLoansCount
);

/// <summary>
/// Generic paginated response metadata.
/// </summary>
public record PaginatedResultDto<T>(
    int Page,
    int PageSize,
    int TotalPages,
    int TotalCount,
    bool HasMore,
    IReadOnlyList<T> Items
);

/// <summary>
/// Result of querying books by genre with pagination.
/// </summary>
public record GenreBooksResultDto(
    string Genre,
    int Page,
    int PageSize,
    int TotalPages,
    int TotalMatching,
    bool HasMore,
    IReadOnlyList<BookSummaryDto> Books
)
{
    public int TotalCount => TotalMatching;
};

/// <summary>
/// Statistics for a specific genre.
/// </summary>
public record GenreStatDto(
    string Genre,
    int BookCount,
    int CopiesAvailable,
    double? AverageRating
);

/// <summary>
/// Overall library catalog statistics.
/// </summary>
public record LibraryStatsDto(
    int TotalBooks,
    int AuthenticBooksCount,
    long TotalAvailableCopies,
    int ActiveLoansCount,
    IReadOnlyList<GenreStatDto> GenreBreakdown
);

/// <summary>
/// Data transfer object for a book loan.
/// </summary>
public record LoanDto(
    int LoanId,
    int BookId,
    string BookTitle,
    string BorrowerName,
    DateTime BorrowedAt,
    DateTime DueDate,
    bool IsReturned
);

/// <summary>
/// Confirmation result of a borrowing or return transaction.
/// </summary>
public record LoanResultDto(
    bool Success,
    string Message,
    int? LoanId,
    string? BookTitle,
    string? BorrowerName,
    DateTime? DueDate,
    int RemainingCopies
);
