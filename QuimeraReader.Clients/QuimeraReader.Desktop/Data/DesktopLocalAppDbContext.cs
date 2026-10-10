using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Desktop.Services;

namespace QuimeraReader.Desktop.Data;

public class DesktopLocalAppDbContext : DbContext
{
    public DbSet<Book> Books { get; set; } = null!;
    public DbSet<BookAudioTrack> BookAudioTracks { get; set; } = null!;
    public DbSet<BookAudioChapter> BookAudioChapters { get; set; } = null!;
    public DbSet<BookAnnotation> BookAnnotations { get; set; } = null!;
    public DbSet<BookCategory> Categories { get; set; } = null!;
    public DbSet<ClientLog> ClientLogs { get; set; } = null!;

    public DesktopLocalAppDbContext()
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Filename={DesktopStorage.DatabasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<BookAuthor>().HasKey(ba => new { ba.BookId, ba.AuthorId });
        modelBuilder.Entity<BookCategory>().HasKey(bc => new { bc.BookId, bc.CategoryId });

        modelBuilder.Entity<Book>()
            .HasMany(b => b.AudioTracks)
            .WithOne(t => t.Book)
            .HasForeignKey(t => t.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Book>()
            .HasMany(b => b.Annotations)
            .WithOne(a => a.Book)
            .HasForeignKey(a => a.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
