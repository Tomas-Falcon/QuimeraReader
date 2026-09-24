using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using System.IO;

namespace QuimeraReader.Mobile.Data
{
    public class LocalAppDbContext : DbContext
    {
        public DbSet<Book> Books { get; set; } = null!;
        public DbSet<BookAudioTrack> BookAudioTracks { get; set; } = null!;
        public DbSet<BookAnnotation> BookAnnotations { get; set; } = null!;
        public DbSet<BookCategory> Categories { get; set; } = null!;

        public LocalAppDbContext()
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string dbPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "quimerareader_local.db");
            optionsBuilder.UseSqlite($"Filename={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Replicate necessary relationships
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
}