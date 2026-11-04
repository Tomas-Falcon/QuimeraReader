using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Clients.Common;
using QuimeraReader.Domain.Entities;
using Xunit;

namespace QuimeraReader.Tests.Common;

/// <summary>
/// BD local de los clientes (Android/Desktop) creada por una versión anterior: cuando el servidor añadió
/// Book.UniverseId, las instalaciones existentes fallaban con "no such column" y el modo sin conexión dejaba de funcionar.
/// </summary>
public class LocalSchemaSyncTests : IAsyncLifetime
{
    // Mismo modelo que LocalAppDbContext (Android): las entidades del servidor sobre una BD SQLite propia
    private sealed class ClientDbContext(DbContextOptions<ClientDbContext> options) : DbContext(options)
    {
        public DbSet<Book> Books { get; set; } = null!;
        public DbSet<BookAudioTrack> BookAudioTracks { get; set; } = null!;
        public DbSet<BookAudioChapter> BookAudioChapters { get; set; } = null!;
        public DbSet<BookAnnotation> BookAnnotations { get; set; } = null!;
        public DbSet<BookCategory> Categories { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<BookAuthor>().HasKey(ba => new { ba.BookId, ba.AuthorId });
            modelBuilder.Entity<BookCategory>().HasKey(bc => new { bc.BookId, bc.CategoryId });
            modelBuilder.Entity<Book>().HasMany(b => b.AudioTracks).WithOne(t => t.Book).HasForeignKey(t => t.BookId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Book>().HasMany(b => b.Annotations).WithOne(a => a.Book).HasForeignKey(a => a.BookId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ClientDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ClientDbContext(new DbContextOptionsBuilder<ClientDbContext>().UseSqlite(_connection).Options);

        // Tabla Books de una versión antigua del cliente: sin UniverseId, SeriesId, ContentHash, IsAvailableOffline...
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE "Books" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Books" PRIMARY KEY AUTOINCREMENT,
                                  "Title" TEXT NOT NULL, "EpubFilePath" TEXT NOT NULL, "ReadingStatus" TEXT NULL);
            INSERT INTO "Books" ("Id", "Title", "EpubFilePath", "ReadingStatus") VALUES (7, 'Efecto red', '/data/epub_7.epub', 'Reading');
            """;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task OldSchema_GetsMissingColumns_AndKeepsLocalData()
    {
        // Antes del arreglo esta consulta fallaba con "no such column"
        var queryOldSchema = () => _db.Books.AsNoTracking().ToListAsync();
        await queryOldSchema.Should().ThrowAsync<SqliteException>();

        var added = await LocalSchemaSync.EnsureSchemaAsync(_db);

        added.Should().Contain(new[] { "Books.UniverseId", "Books.SeriesId", "Books.IsAvailableOffline", "Books.LastReadAt" });
        var book = await _db.Books.AsNoTracking().SingleAsync();
        book.Title.Should().Be("Efecto red");
        book.ReadingStatus.Should().Be("Reading");
        book.IsAvailableOffline.Should().BeFalse("las columnas NOT NULL nuevas reciben un valor por defecto");
        book.UniverseId.Should().BeNull();

        // Las tablas que no existían se crean con todas sus columnas
        _db.BookAudioTracks.Add(new BookAudioTrack { BookId = 7, TrackNumber = 1, FileName = "t1.mp3", FilePath = "/data/t1.mp3" });
        await _db.SaveChangesAsync();
        (await _db.BookAudioTracks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunningTwice_AddsNothingTheSecondTime()
    {
        await LocalSchemaSync.EnsureSchemaAsync(_db);

        var second = await LocalSchemaSync.EnsureSchemaAsync(_db);

        second.Should().BeEmpty();
    }

    [Fact]
    public async Task FreshDatabase_IsCreatedFromTheModel()
    {
        await using var fresh = new SqliteConnection("Data Source=:memory:");
        await fresh.OpenAsync();
        await using var db = new ClientDbContext(new DbContextOptionsBuilder<ClientDbContext>().UseSqlite(fresh).Options);

        var added = await LocalSchemaSync.EnsureSchemaAsync(db);

        added.Should().BeEmpty("todas las tablas se crean completas");
        db.Books.Add(new Book { Title = "Sistemas críticos", EpubFilePath = "/data/1.epub", UniverseId = null });
        await db.SaveChangesAsync();
        (await db.Books.CountAsync()).Should().Be(1);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
