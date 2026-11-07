using LibraryMCPServer.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryMCPServer.Data;

/// <summary>
/// Entity Framework Core DbContext for the Library database with loans and rich catalog metadata.
/// </summary>
public class LibraryDbContext : DbContext
{
    public LibraryDbContext(DbContextOptions<LibraryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookLoan> BookLoans => Set<BookLoan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Book>(entity =>
        {
            entity.ToTable("Books", t =>
            {
                t.HasCheckConstraint("CK_Books_CopiesAvailable", "[CopiesAvailable] >= 0");
                t.HasCheckConstraint("CK_Books_Rating", "[Rating] >= 0.0 AND [Rating] <= 5.0");
                t.HasCheckConstraint("CK_Books_PageCount", "[PageCount] > 0");
            });

            entity.HasKey(b => b.Id);

            entity.Property(b => b.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(b => b.Author)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(b => b.Genre)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(b => b.Subjects)
                .IsRequired()
                .HasColumnType("nvarchar(max)");

            entity.Property(b => b.PublishedYear)
                .IsRequired();

            entity.Property(b => b.ISBN)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(b => b.CopiesAvailable)
                .IsRequired();

            entity.Property(b => b.Rating);

            entity.Property(b => b.PageCount)
                .HasDefaultValue(300);

            entity.Property(b => b.CoverImageUrl)
                .HasMaxLength(500);

            entity.Property(b => b.Description)
                .HasMaxLength(1500);

            // Constraints and Indexes
            entity.HasIndex(b => b.ISBN)
                .IsUnique()
                .HasDatabaseName("IX_Books_ISBN");

            entity.HasIndex(b => b.Title)
                .HasDatabaseName("IX_Books_Title");

            entity.HasIndex(b => b.Author)
                .HasDatabaseName("IX_Books_Author");

            entity.HasIndex(b => b.Genre)
                .HasDatabaseName("IX_Books_Genre");

            entity.HasIndex(b => b.Rating)
                .HasDatabaseName("IX_Books_Rating");
        });

        modelBuilder.Entity<BookLoan>(entity =>
        {
            entity.ToTable("BookLoans");

            entity.HasKey(l => l.Id);

            entity.Property(l => l.BorrowerName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(l => l.BorrowedAt)
                .IsRequired();

            entity.Property(l => l.DueDate)
                .IsRequired();

            entity.Property(l => l.IsReturned)
                .HasDefaultValue(false);

            entity.HasOne(l => l.Book)
                .WithMany(b => b.Loans)
                .HasForeignKey(l => l.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => l.BorrowerName)
                .HasDatabaseName("IX_BookLoans_BorrowerName");

            entity.HasIndex(l => l.IsReturned)
                .HasDatabaseName("IX_BookLoans_IsReturned");
        });
    }
}
