using System.ComponentModel.DataAnnotations;

namespace LibraryMCPServer.Models;

/// <summary>
/// Represents an authentic book record in the library catalog.
/// </summary>
public class Book
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Title is required and cannot be empty.")]
    [StringLength(250, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 250 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Author is required and cannot be empty.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Author must be between 1 and 200 characters.")]
    public string Author { get; set; } = string.Empty;

    [Required(ErrorMessage = "Genre is required and cannot be empty.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Genre must be between 1 and 100 characters.")]
    public string Genre { get; set; } = string.Empty;

    public string Subjects { get; set; } = string.Empty;

    [Range(1000, 2100, ErrorMessage = "PublishedYear must be between 1000 and 2100.")]
    public int PublishedYear { get; set; }

    [Required(ErrorMessage = "ISBN is required and cannot be empty.")]
    [StringLength(25, MinimumLength = 10, ErrorMessage = "ISBN must be between 10 and 25 characters.")]
    public string ISBN { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "CopiesAvailable cannot be negative.")]
    public int CopiesAvailable { get; set; }

    [Range(0.0, 5.0, ErrorMessage = "Rating must be between 0.0 and 5.0.")]
    public double? Rating { get; set; }

    [Range(1, 10000, ErrorMessage = "PageCount must be between 1 and 10000.")]
    public int PageCount { get; set; } = 300;

    [StringLength(500)]
    public string? CoverImageUrl { get; set; }

    [StringLength(2500, ErrorMessage = "Description cannot exceed 2500 characters.")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this record is an authentic, published book (versus synthetic demo data).
    /// </summary>
    public bool IsAuthentic { get; set; } = true;

    public bool IsSample { get; set; }

    // Navigation property for active and historical book loans
    public ICollection<BookLoan> Loans { get; set; } = new List<BookLoan>();
}
