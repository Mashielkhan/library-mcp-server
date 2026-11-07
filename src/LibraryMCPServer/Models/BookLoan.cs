using System.ComponentModel.DataAnnotations;

namespace LibraryMCPServer.Models;

/// <summary>
/// Represents a book checkout / borrowing transaction in the library.
/// </summary>
public class BookLoan
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    [Required(ErrorMessage = "Borrower name is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Borrower name must be between 1 and 100 characters.")]
    public string BorrowerName { get; set; } = string.Empty;

    public DateTime BorrowedAt { get; set; } = DateTime.UtcNow;

    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(14);

    public DateTime? ReturnedAt { get; set; }

    public bool IsReturned { get; set; } = false;
}
