using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Application.Interfaces;

namespace QuimeraReader.Infrastructure;

public class AppDbContext : DbContext, IAppDbContext
{
    public DbSet<Book> Books { get; set; } = null!;
    public DbSet<Author> Authors { get; set; } = null!;
    public DbSet<BookAuthor> BookAuthors { get; set; } = null!;
    public DbSet<SyncMap> SyncMaps { get; set; } = null!;
    public DbSet<SystemSetting> SystemSettings { get; set; } = null!;

    public DbSet<Universe> Universes { get; set; } = null!;
    public DbSet<Series> Series { get; set; } = null!;
    public DbSet<ScannedSourceFile> ScannedSourceFiles { get; set; } = null!;
    public DbSet<BookMetadataCache> BookMetadataCache { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<BookCategory> BookCategories { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<BookAudioTrack> BookAudioTracks { get; set; }
    public DbSet<BookAudioChapter> BookAudioChapters { get; set; }
    public DbSet<UnmatchedAudioTrack> UnmatchedAudioTracks { get; set; } = null!;
        public DbSet<BookAnnotation> BookAnnotations { get; set; } = null!;

    /// <summary>
    /// Guarda y mantiene al día los índices de búsqueda (BooksSearch/AuthorsSearch) para lo que haya cambiado.
    /// Un fallo del índice nunca hace fallar el guardado: el mantenimiento nocturno lo reconstruye.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (!Database.IsSqlite())
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var bookIds = new HashSet<int>();
        var authorIds = new HashSet<int>();
        var seriesIds = new HashSet<int>();
        var addedBooks = new List<Book>();
        var addedAuthors = new List<Author>();

        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case Book book when entry.State == EntityState.Added:
                    addedBooks.Add(book);
                    break;
                case Book book when entry.State == EntityState.Deleted:
                    bookIds.Add(book.Id);
                    break;
                // El progreso de lectura se guarda cada pocos segundos: solo reindexar si cambia algo buscable
                case Book book when entry.State == EntityState.Modified &&
                                    (entry.Property(nameof(Book.Title)).IsModified || entry.Property(nameof(Book.SeriesId)).IsModified):
                    bookIds.Add(book.Id);
                    break;
                case BookAuthor bookAuthor when entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified:
                    bookIds.Add(bookAuthor.BookId);
                    break;
                case Author author when entry.State == EntityState.Added:
                    addedAuthors.Add(author);
                    break;
                case Author author when entry.State == EntityState.Deleted ||
                                        (entry.State == EntityState.Modified && entry.Property(nameof(Author.Name)).IsModified):
                    authorIds.Add(author.Id);
                    break;
                case QuimeraReader.Domain.Entities.Series series when entry.State == EntityState.Modified && entry.Property(nameof(QuimeraReader.Domain.Entities.Series.Name)).IsModified:
                    seriesIds.Add(series.Id);
                    break;
            }
        }

        // Autores renombrados o borrados afectan también a las filas de sus libros
        var renamedAuthorIds = authorIds.ToList();

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        try
        {
            foreach (var book in addedBooks) bookIds.Add(book.Id);
            foreach (var author in addedAuthors) authorIds.Add(author.Id);

            if (renamedAuthorIds.Count > 0)
            {
                var ids = await BookAuthors.Where(ba => renamedAuthorIds.Contains(ba.AuthorId)).Select(ba => ba.BookId).ToListAsync(cancellationToken);
                bookIds.UnionWith(ids);
            }
            if (seriesIds.Count > 0)
            {
                var ids = await Books.Where(b => b.SeriesId != null && seriesIds.Contains(b.SeriesId.Value)).Select(b => b.Id).ToListAsync(cancellationToken);
                bookIds.UnionWith(ids);
            }

            if (bookIds.Count > 0 || authorIds.Count > 0)
            {
                await Services.SqliteSearchIndex.RefreshAsync(this, bookIds, authorIds, cancellationToken);
            }
        }
        catch
        {
            // Índice desfasado para estas filas hasta la próxima reconstrucción (arranque o mantenimiento nocturno)
        }

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Author>().HasIndex(a => a.Name).IsUnique();
        modelBuilder.Entity<Category>().HasIndex(c => c.Name).IsUnique();
        
        modelBuilder.Entity<BookAudioTrack>()
            .HasOne(t => t.Book)
            .WithMany(b => b.AudioTracks)
            .HasForeignKey(t => t.BookId)
            .OnDelete(DeleteBehavior.Cascade);

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

        // Borrar un universo no borra nada más: sus sagas y libros sueltos quedan sin universo
        modelBuilder.Entity<Series>()
            .HasOne(s => s.Universe)
            .WithMany(u => u.Series)
            .HasForeignKey(s => s.UniverseId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Book>()
            .HasOne(b => b.Universe)
            .WithMany(u => u.Books)
            .HasForeignKey(b => b.UniverseId)
            .OnDelete(DeleteBehavior.SetNull);

        // Las sagas se buscan por nombre al importar
        modelBuilder.Entity<Series>().HasIndex(s => s.Name);
        modelBuilder.Entity<Book>().HasIndex(b => b.ContentHash);
        modelBuilder.Entity<ScannedSourceFile>().HasIndex(f => f.Path).IsUnique();
        modelBuilder.Entity<BookMetadataCache>().HasIndex(c => c.ContentHash).IsUnique();

        // SystemSettings configuration
        modelBuilder.Entity<SystemSetting>()
            .HasKey(s => s.Key);
    }
}


