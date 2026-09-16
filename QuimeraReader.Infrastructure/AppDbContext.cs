using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure;

public class AppDbContext : DbContext
{
    public DbSet<Book> Books { get; set; } = null!;
    public DbSet<Author> Authors { get; set; } = null!;
    public DbSet<BookAuthor> BookAuthors { get; set; } = null!;
    public DbSet<SyncMap> SyncMaps { get; set; } = null!;
    public DbSet<SystemSetting> SystemSettings { get; set; } = null!;

    public DbSet<Universe> Universes { get; set; } = null!;
    public DbSet<Series> Series { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<BookCategory> BookCategories { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Many-to-Many configuration for Book and Author
        modelBuilder.Entity<BookAuthor>()
            .HasKey(ba => new { ba.BookId, ba.AuthorId });

        modelBuilder.Entity<BookAuthor>()
            .HasOne(ba => ba.Book)
            .WithMany(b => b.Authors)
            .HasForeignKey(ba => ba.BookId);

        modelBuilder.Entity<BookAuthor>()
            .HasOne(ba => ba.Author)
            .WithMany(a => a.Books)
            .HasForeignKey(ba => ba.AuthorId);

        // Many-to-Many configuration for Book and Category
        modelBuilder.Entity<BookCategory>()
            .HasKey(bc => new { bc.BookId, bc.CategoryId });

        modelBuilder.Entity<BookCategory>()
            .HasOne(bc => bc.Book)
            .WithMany(b => b.Categories)
            .HasForeignKey(bc => bc.BookId);

        modelBuilder.Entity<BookCategory>()
            .HasOne(bc => bc.Category)
            .WithMany(c => c.Books)
            .HasForeignKey(bc => bc.CategoryId);

        // SystemSettings configuration
        modelBuilder.Entity<SystemSetting>()
            .HasKey(s => s.Key);
    }
}
