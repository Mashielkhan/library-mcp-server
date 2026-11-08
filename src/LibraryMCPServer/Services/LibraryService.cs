using LibraryMCPServer.Data;
using LibraryMCPServer.Models;
using LibraryMCPServer.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Linq.Expressions;

namespace LibraryMCPServer.Services;

/// <summary>
/// Production implementation of ILibraryService providing multi-genre search,
/// typo tolerance, live borrowing transactions, and in-memory caching.
/// </summary>
public class LibraryService : ILibraryService
{
    private const string StatsCacheKey = "library_stats_cache";
    private const string GenresCacheKey = "library_genres_cache";

    private readonly LibraryDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LibraryService> _logger;
    private readonly bool _includeSampleData;
    private readonly int _minimumRatingCount;

    public LibraryService(
        LibraryDbContext context,
        IMemoryCache cache,
        ILogger<LibraryService> logger,
        IConfiguration? configuration = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _includeSampleData = configuration?.GetValue("INCLUDE_SAMPLE_DATA", configuration.GetValue("LibrarySettings:IncludeSampleData", false)) ?? false;
        _minimumRatingCount = Math.Max(0, configuration?.GetValue("LibrarySettings:MinimumRatingCount", 10) ?? 10);
    }

    public async Task<PaginatedResultDto<BookSummaryDto>> SearchBooksAsync(
        string searchTerm,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException("Search term must not be null or whitespace.", nameof(searchTerm));
        }

        var trimmed = searchTerm.Trim();
        var searchLower = trimmed.ToLowerInvariant();
        var validPage = Math.Max(1, page);
        var validPageSize = Math.Clamp(pageSize, 1, 50);

        _logger.LogInformation("Searching books for term '{Term}' (Page: {Page}, Size: {Size})", trimmed, validPage, validPageSize);

        // 1. Direct case-insensitive search across title, author, and description
        var query = CatalogBooks()
            .AsNoTracking()
            .Where(b => b.Title.ToLower().Contains(searchLower)
                     || b.Author.ToLower().Contains(searchLower)
                     || b.Genre.ToLower().Contains(searchLower));

        var totalCount = await query.CountAsync(cancellationToken);

        // 2. Fuzzy Matching Fallback: If 0 direct matches found, execute typo-tolerant matching
        if (totalCount == 0 && trimmed.Length >= 4)
        {
            _logger.LogInformation("Direct search yielded 0 results. Executing typo-tolerant fuzzy matching for '{Term}'", trimmed);

            var candidates = await CatalogBooks()
                .AsNoTracking()
                .OrderByDescending(b => b.RatingCount >= _minimumRatingCount ? b.Rating : null)
                .Take(250)
                .ToListAsync(cancellationToken);

            var fuzzyMatches = candidates
                .Select(b => new
                {
                    Book = b,
                    Score = Math.Max(
                        FuzzyMatcher.Similarity(trimmed, b.Title),
                        FuzzyMatcher.Similarity(trimmed, b.Author))
                })
                .Where(x => x.Score >= 0.65)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Book.Title)
                .Select(x => x.Book)
                .ToList();

            if (fuzzyMatches.Count > 0)
            {
                var fuzzyTotal = fuzzyMatches.Count;
                var fuzzyPaged = fuzzyMatches
                    .Skip((validPage - 1) * validPageSize)
                    .Take(validPageSize)
                    .Select(MapToSummary)
                    .ToList();

                var fuzzyPages = (int)Math.Ceiling((double)fuzzyTotal / validPageSize);

                return new PaginatedResultDto<BookSummaryDto>(
                    Page: validPage,
                    PageSize: validPageSize,
                    TotalPages: fuzzyPages,
                    TotalCount: fuzzyTotal,
                    HasMore: validPage < fuzzyPages,
                    Items: fuzzyPaged
                );
            }
        }

        var totalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / validPageSize) : 0;

        var items = await query
            .OrderBy(b => b.Title)
            .ThenBy(b => b.Id)
            .Skip((validPage - 1) * validPageSize)
            .Take(validPageSize)
            .Select(b => MapToSummary(b))
            .ToListAsync(cancellationToken);

        return new PaginatedResultDto<BookSummaryDto>(
            Page: validPage,
            PageSize: validPageSize,
            TotalPages: totalPages,
            TotalCount: totalCount,
            HasMore: validPage < totalPages,
            Items: items
        );
    }

    public async Task<PaginatedResultDto<BookSummaryDto>> SearchBooksAdvancedAsync(
        string? query = null,
        string? genre = null,
        string? author = null,
        int? minYear = null,
        int? maxYear = null,
        double? minRating = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var validPage = Math.Max(1, page);
        var validPageSize = Math.Clamp(pageSize, 1, 50);

        var q = CatalogBooks();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var queryLower = query.Trim().ToLowerInvariant();
            q = q.Where(b => b.Title.ToLower().Contains(queryLower) || b.Description.ToLower().Contains(queryLower));
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            var genreLower = genre.Trim().ToLowerInvariant();
            q = q.Where(b => b.Genre.ToLower().Contains(genreLower));
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            var authorLower = author.Trim().ToLowerInvariant();
            q = q.Where(b => b.Author.ToLower().Contains(authorLower));
        }

        if (minYear.HasValue) q = q.Where(b => b.PublishedYear >= minYear.Value);
        if (maxYear.HasValue) q = q.Where(b => b.PublishedYear <= maxYear.Value);
        if (minRating.HasValue) q = q.Where(b => b.RatingCount >= _minimumRatingCount && b.Rating >= minRating.Value);

        var totalCount = await q.CountAsync(cancellationToken);
        var totalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / validPageSize) : 0;

        var items = await q
            .OrderByDescending(b => b.RatingCount >= _minimumRatingCount ? b.Rating : null)
            .ThenBy(b => b.Title)
            .Skip((validPage - 1) * validPageSize)
            .Take(validPageSize)
            .Select(b => MapToSummary(b))
            .ToListAsync(cancellationToken);

        return new PaginatedResultDto<BookSummaryDto>(
            Page: validPage,
            PageSize: validPageSize,
            TotalPages: totalPages,
            TotalCount: totalCount,
            HasMore: validPage < totalPages,
            Items: items
        );
    }

    public async Task<GenreBooksResultDto> GetBooksByGenreAsync(
        string genre,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            throw new ArgumentException("Genre must not be null or whitespace.", nameof(genre));
        }

        var trimmedGenre = genre.Trim();
        var genreLower = trimmedGenre.ToLowerInvariant();
        var validPage = Math.Max(1, page);
        var validPageSize = Math.Clamp(pageSize, 1, 50);

        // Supports multi-genre matching (e.g. searching "Satire" matches "Classic Fiction, Political Satire")
        var query = CatalogBooks()
            .AsNoTracking()
            .Where(b => b.Genre.ToLower().Contains(genreLower));

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / validPageSize) : 0;

        var books = await query
            .OrderByDescending(b => b.Rating)
            .ThenBy(b => b.Title)
            .Skip((validPage - 1) * validPageSize)
            .Take(validPageSize)
            .Select(b => MapToSummary(b))
            .ToListAsync(cancellationToken);

        return new GenreBooksResultDto(
            Genre: trimmedGenre,
            Page: validPage,
            PageSize: validPageSize,
            TotalPages: totalPages,
            TotalMatching: totalCount,
            HasMore: validPage < totalPages,
            Books: books
        );
    }

    public async Task<LibraryStatsDto> GetLibraryStatsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(StatsCacheKey, out LibraryStatsDto? cachedStats) && cachedStats != null)
        {
            return cachedStats;
        }

        var catalog = CatalogBooks();
        var totalBooks = await catalog.CountAsync(cancellationToken);
        if (totalBooks == 0)
        {
            return new LibraryStatsDto(0, 0, 0, 0, Array.Empty<GenreStatDto>());
        }

        var authenticCount = await catalog.CountAsync(b => b.IsAuthentic, cancellationToken);
        var totalCopies = await catalog.SumAsync(b => (long)b.CopiesAvailable, cancellationToken);
        var catalogBookIds = catalog.Select(b => b.Id);
        var activeLoans = await _context.BookLoans
            .AsNoTracking()
            .CountAsync(l => !l.IsReturned && (_includeSampleData || catalogBookIds.Contains(l.BookId)), cancellationToken);

        // Group across multi-genre tags cleanly
        var allBooks = await catalog
            .Select(b => new { b.Genre, b.CopiesAvailable, b.Rating, b.RatingCount })
            .ToListAsync(cancellationToken);

        var genreMap = new Dictionary<string, (int count, int copies, List<double> ratings)>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in allBooks)
        {
            var parts = b.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (!genreMap.ContainsKey(part))
                {
                    genreMap[part] = (0, 0, new List<double>());
                }
                var cur = genreMap[part];
            if (b.RatingCount >= _minimumRatingCount && b.Rating.HasValue) cur.ratings.Add(b.Rating.Value);
                genreMap[part] = (cur.count + 1, cur.copies + b.CopiesAvailable, cur.ratings);
            }
        }

        var breakdown = genreMap
            .Select(kv => new GenreStatDto(
                Genre: kv.Key,
                BookCount: kv.Value.count,
                CopiesAvailable: kv.Value.copies,
                AverageRating: kv.Value.ratings.Count == 0 ? null : Math.Round(kv.Value.ratings.Average(), 2)
            ))
            .OrderByDescending(s => s.BookCount)
            .ThenBy(s => s.Genre)
            .ToList();

        var stats = new LibraryStatsDto(
            TotalBooks: totalBooks,
            AuthenticBooksCount: authenticCount,
            TotalAvailableCopies: totalCopies,
            ActiveLoansCount: activeLoans,
            GenreBreakdown: breakdown
        );

        _cache.Set(StatsCacheKey, stats, TimeSpan.FromMinutes(5));
        return stats;
    }

    public async Task<BookDetailDto?> GetBookDetailsAsync(
        int bookId,
        CancellationToken cancellationToken = default)
    {
        if (bookId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bookId), "Book ID must be greater than zero.");
        }

        return await CatalogBooks()
            .AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => new BookDetailDto(
                b.Id,
                b.Title,
                b.Author,
                b.Genre,
                b.PublishedYear,
                b.ISBN,
                b.CopiesAvailable,
                b.RatingCount >= _minimumRatingCount ? b.Rating : null,
                b.RatingCount,
                b.PageCount,
                b.CoverImageUrl,
                b.Description,
                b.IsAuthentic,
                b.Loans.Count(l => !l.IsReturned)
            ))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PaginatedResultDto<string>> ListAvailableGenresAsync(
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(GenresCacheKey, out IReadOnlyList<string>? cachedGenres) && cachedGenres != null)
        {
            return Paginate(cachedGenres, page, pageSize);
        }

        var allGenres = await CatalogBooks()
            .Select(b => b.Genre)
            .ToListAsync(cancellationToken);

        var distinctGenres = allGenres
            .SelectMany(g => g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToList();

        _cache.Set(GenresCacheKey, distinctGenres, TimeSpan.FromMinutes(15));
        return Paginate(distinctGenres, page, pageSize);
    }

    public async Task<PaginatedResultDto<BookSummaryDto>> GetLowStockBooksAsync(
        int threshold = 5,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var clampedThreshold = Math.Max(0, threshold);
        var validPage = Math.Max(1, page);
        var validPageSize = Math.Clamp(pageSize, 1, 50);

        var query = CatalogBooks()
            .Where(b => b.CopiesAvailable <= clampedThreshold)
            .OrderBy(b => b.CopiesAvailable)
            .ThenBy(b => b.Title);

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / validPageSize);
        var items = await query.Skip((validPage - 1) * validPageSize).Take(validPageSize)
            .Select(b => new BookSummaryDto(b.Id, b.Title, b.Author, b.Genre, b.PublishedYear, b.CopiesAvailable, b.RatingCount >= _minimumRatingCount ? b.Rating : null, b.RatingCount, b.PageCount, b.CoverImageUrl, b.IsAuthentic))
            .ToListAsync(cancellationToken);
        return new PaginatedResultDto<BookSummaryDto>(validPage, validPageSize, totalPages, totalCount, validPage < totalPages, items);
    }

    public async Task<LoanResultDto> BorrowBookAsync(
        int bookId,
        string borrowerName,
        int loanDays = 14,
        CancellationToken cancellationToken = default)
    {
        if (bookId <= 0)
        {
            return new LoanResultDto(false, "Invalid book ID.", null, null, null, null, 0);
        }

        if (string.IsNullOrWhiteSpace(borrowerName))
        {
            return new LoanResultDto(false, "Borrower name is required.", null, null, null, null, 0);
        }

        var book = await _context.Books.Where(b => _includeSampleData || !b.IsSample)
            .FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);
        if (book == null)
        {
            return new LoanResultDto(false, $"Book with ID {bookId} was not found.", null, null, null, null, 0);
        }

        if (book.CopiesAvailable <= 0)
        {
            return new LoanResultDto(false, $"All copies of '{book.Title}' are currently borrowed. No inventory available.", null, book.Title, borrowerName, null, 0);
        }

        book.CopiesAvailable--;

        var loan = new BookLoan
        {
            BookId = book.Id,
            BorrowerName = borrowerName.Trim(),
            BorrowedAt = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(Math.Clamp(loanDays, 1, 60)),
            IsReturned = false
        };

        _context.BookLoans.Add(loan);
        await _context.SaveChangesAsync(cancellationToken);

        _cache.Remove(StatsCacheKey);

        _logger.LogInformation("Book ID {BookId} successfully checked out by {Borrower}. Remaining copies: {Remaining}",
            bookId, loan.BorrowerName, book.CopiesAvailable);

        return new LoanResultDto(
            Success: true,
            Message: $"Successfully borrowed '{book.Title}'. Due date: {loan.DueDate:yyyy-MM-dd}.",
            LoanId: loan.Id,
            BookTitle: book.Title,
            BorrowerName: loan.BorrowerName,
            DueDate: loan.DueDate,
            RemainingCopies: book.CopiesAvailable
        );
    }

    public async Task<LoanResultDto> ReturnBookAsync(
        int bookId,
        string borrowerName,
        CancellationToken cancellationToken = default)
    {
        if (bookId <= 0 || string.IsNullOrWhiteSpace(borrowerName))
        {
            return new LoanResultDto(false, "Valid Book ID and Borrower Name are required.", null, null, null, null, 0);
        }

        var trimmedBorrower = borrowerName.Trim();
        var loan = await _context.BookLoans
            .Include(l => l.Book)
            .FirstOrDefaultAsync(l => l.BookId == bookId && !l.IsReturned && (_includeSampleData || !l.Book.IsSample) && l.BorrowerName.ToLower() == trimmedBorrower.ToLower(), cancellationToken);

        if (loan == null)
        {
            return new LoanResultDto(false, $"No active loan found for Book ID {bookId} under borrower '{trimmedBorrower}'.", null, null, trimmedBorrower, null, 0);
        }

        loan.IsReturned = true;
        loan.ReturnedAt = DateTime.UtcNow;
        loan.Book.CopiesAvailable++;

        await _context.SaveChangesAsync(cancellationToken);

        _cache.Remove(StatsCacheKey);

        _logger.LogInformation("Book ID {BookId} returned by {Borrower}. New available copies: {Copies}",
            bookId, trimmedBorrower, loan.Book.CopiesAvailable);

        return new LoanResultDto(
            Success: true,
            Message: $"Successfully returned '{loan.Book.Title}'. Thank you!",
            LoanId: loan.Id,
            BookTitle: loan.Book.Title,
            BorrowerName: loan.BorrowerName,
            DueDate: null,
            RemainingCopies: loan.Book.CopiesAvailable
        );
    }

    public async Task<PaginatedResultDto<LoanDto>> GetActiveLoansAsync(
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var validPage = Math.Max(1, page);
        var validPageSize = Math.Clamp(pageSize, 1, 50);
        var query = _context.BookLoans
            .AsNoTracking()
            .Include(l => l.Book)
            .Where(l => !l.IsReturned && (_includeSampleData || !l.Book.IsSample));
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / validPageSize);

        var loans = await query
            .OrderBy(l => l.DueDate)
            .Skip((validPage - 1) * validPageSize)
            .Take(validPageSize)
            .Select(l => new LoanDto(
                l.Id,
                l.BookId,
                l.Book.Title,
                l.BorrowerName,
                l.BorrowedAt,
                l.DueDate,
                l.IsReturned
            ))
            .ToListAsync(cancellationToken);

        return new PaginatedResultDto<LoanDto>(validPage, validPageSize, totalPages, totalCount, validPage < totalPages, loans);
    }

    private BookSummaryDto MapToSummary(Book b) =>
        new(b.Id, b.Title, b.Author, b.Genre, b.PublishedYear, b.CopiesAvailable, b.RatingCount >= _minimumRatingCount ? b.Rating : null, b.RatingCount, b.PageCount, b.CoverImageUrl, b.IsAuthentic);

    private IQueryable<Book> CatalogBooks() =>
        _context.Books.AsNoTracking().Where(b => _includeSampleData || !b.IsSample);

    private static PaginatedResultDto<string> Paginate(IReadOnlyList<string> items, int page, int pageSize)
    {
        var validPage = Math.Max(1, page);
        var validPageSize = Math.Clamp(pageSize, 1, 50);
        var totalPages = items.Count == 0 ? 0 : (int)Math.Ceiling((double)items.Count / validPageSize);
        return new PaginatedResultDto<string>(validPage, validPageSize, totalPages, items.Count, validPage < totalPages, items.Skip((validPage - 1) * validPageSize).Take(validPageSize).ToList());
    }
}
