using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using Xunit;

namespace QuimeraReader.Tests.Api;

public class BookDeduplicatorTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qr-dedup-" + Guid.NewGuid().ToString("N"));
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        Directory.CreateDirectory(_root);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private string File(string name)
    {
        var path = Path.Combine(_root, name);
        System.IO.File.WriteAllText(path, name);
        return path;
    }

    private static Book Book(string title, Author author, string path, string? hash = null)
    {
        var book = new Book { Title = title, EpubFilePath = path, ContentHash = hash };
        book.Authors.Add(new BookAuthor { Author = author });
        return book;
    }

    [Fact]
    public async Task Merges_KeepsUserData_AndOnlyDeletesOrphansWhenAsked()
    {
        var wells = new Author { Name = "Martha Wells" };
        var other = new Author { Name = "Otra Autora" };

        // Mismo EPUB importado dos veces: se queda el que tiene notas
        var copyA = Book("Efecto red", wells, File("a.epub"), "h1");
        var copyB = Book("Efecto red", wells, File("b.epub"), "h1");
        copyB.Annotations.Add(new BookAnnotation { CfiRange = "epubcfi(/6/2)", SelectedText = "x" });

        // Libro perdido con progreso, sin conexión y audio; su copia con archivo no tiene nada
        var lost = Book("Sistemas críticos", wells, "/app/media/books/lost.epub");
        lost.ReadingStatus = "Reading";
        lost.PercentageCompleted = 0.32;
        lost.CurrentEpubCfi = "epubcfi(/6/14)";
        lost.LastReadAt = new DateTime(2026, 10, 1);
        lost.IsAvailableOffline = true;
        lost.AudioTracks.Add(new BookAudioTrack { FilePath = "/library/lost.mp3", TrackNumber = 1, FileName = "lost.mp3" });
        lost.Annotations.Add(new BookAnnotation { CfiRange = "epubcfi(/6/14)", SelectedText = "nota" });
        var present = Book("SISTEMAS CRITICOS", wells, File("sc.epub"));

        var orphan = Book("Nadie lo leyó", other, "/app/media/books/orphan.epub");
        var keptWithData = Book("Leyéndolo", other, "/app/media/books/reading.epub");
        keptWithData.ReadingStatus = "Reading";

        _db.Books.AddRange(copyA, copyB, lost, present, orphan, keptWithData);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var dry = await BookDeduplicator.RunAsync(_db, null, apply: false, deleteOrphans: true);
        dry.HashDuplicates.Should().Be(1);
        dry.MissingMergedIntoCopy.Should().Be(1);
        dry.MissingOrphans.Should().Be(1);
        dry.MissingKeptWithUserData.Should().ContainSingle().Which.Should().Contain("Leyéndolo");
        (await _db.Books.CountAsync()).Should().Be(6);

        var applied = await BookDeduplicator.RunAsync(_db, null, apply: true, deleteOrphans: true);
        applied.Deleted.Should().Be(2);
        applied.OrphansDeleted.Should().Be(1);

        _db.ChangeTracker.Clear();
        (await _db.Books.Select(b => b.Title).OrderBy(t => t).ToListAsync())
            .Should().Equal("Efecto red", "Leyéndolo", "SISTEMAS CRITICOS");
        (await _db.Books.Include(b => b.Annotations).SingleAsync(b => b.Title == "Efecto red")).Annotations.Should().HaveCount(1);

        var keeper = await _db.Books.Include(b => b.AudioTracks).Include(b => b.Annotations).SingleAsync(b => b.Title == "SISTEMAS CRITICOS");
        keeper.ReadingStatus.Should().Be("Reading");
        keeper.PercentageCompleted.Should().Be(0.32);
        keeper.CurrentEpubCfi.Should().Be("epubcfi(/6/14)");
        keeper.IsAvailableOffline.Should().BeTrue();
        keeper.AudioTracks.Should().ContainSingle().Which.FilePath.Should().Be("/library/lost.mp3");
        keeper.Annotations.Should().ContainSingle().Which.SelectedText.Should().Be("nota");
    }
}
