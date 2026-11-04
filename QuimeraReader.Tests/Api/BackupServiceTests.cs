using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using Xunit;

namespace QuimeraReader.Tests.Api;

public class BackupServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qr-backup-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _source = new("Data Source=:memory:");
    private readonly SqliteConnection _target = new("Data Source=:memory:");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "libro"));
        await _source.OpenAsync();
        await _target.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _target.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static async Task<AppDbContext> CreateAsync(SqliteConnection connection)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    // El escáner solo se usa al importar libros que no existen; aquí todos existen
    private static BackupService Service(AppDbContext db) => new(db, null!, NullLogger<BackupService>.Instance);

    [Fact]
    public async Task Zip_RestoresStateNotesSeriesAndAudio_IntoALibraryThatLostThem_AndIsIdempotent()
    {
        var epub = Path.Combine(_root, "libro", "sistemas.epub");
        await File.WriteAllTextAsync(epub, "contenido del epub");
        var audio = Path.Combine(_root, "libro", "sistemas track 1.mp3");
        await File.WriteAllTextAsync(audio, "audio");
        var hash = await BookFingerprint.ComputeAsync(epub);

        await using (var db = await CreateAsync(_source))
        {
            var book = new Book
            {
                Title = "Sistemas críticos", EpubFilePath = epub, ContentHash = hash, ReadingStatus = "Reading",
                PercentageCompleted = 0.32, CurrentEpubCfi = "epubcfi(/6/14)", LastReadAt = new DateTime(2026, 10, 1),
                IsAvailableOffline = true, Series = new Series { Name = "Los diarios de Matabot" }, SeriesVolume = 1
            };
            book.Authors.Add(new BookAuthor { Author = new Author { Name = "Martha Wells" } });
            book.Annotations.Add(new BookAnnotation { CfiRange = "epubcfi(/6/14!/4/2)", SelectedText = "Matabot", Note = "nota" });
            book.AudioTracks.Add(new BookAudioTrack { FilePath = audio, TrackNumber = 1, FileName = "sistemas.mp3" });
            db.Books.Add(book);
            db.Books.Add(new Book { Title = "Sin leer", EpubFilePath = epub + ".otro" });
            await db.SaveChangesAsync();
        }

        var zip = new MemoryStream();
        await using (var db = await CreateAsync(_source))
        {
            (await Service(db).BuildManifestAsync()).Books.Should().ContainSingle();
            await Service(db).WriteZipAsync(zip);
        }

        // La biblioteca nueva tiene el libro (mismo archivo) pero nada de lo del usuario y el audio se perdió
        File.Delete(audio);
        await using (var db = await CreateAsync(_target))
        {
            db.Books.Add(new Book { Title = "Sistemas criticos", EpubFilePath = epub, ContentHash = hash });
            await db.SaveChangesAsync();
        }

        await using (var db = await CreateAsync(_target))
        {
            zip.Position = 0;
            var report = await Service(db).RestoreAsync(zip, isZip: true);
            report.MatchedByHash.Should().Be(1);
            report.AnnotationsAdded.Should().Be(1);
            report.AudioTracksAdded.Should().Be(1);
            report.NotFound.Should().BeEmpty();
            report.Errors.Should().BeEmpty();
        }

        await using (var db = await CreateAsync(_target))
        {
            var book = await db.Books.Include(b => b.Annotations).Include(b => b.AudioTracks).Include(b => b.Series).SingleAsync();
            book.ReadingStatus.Should().Be("Reading");
            book.PercentageCompleted.Should().Be(0.32);
            book.CurrentEpubCfi.Should().Be("epubcfi(/6/14)");
            book.IsAvailableOffline.Should().BeTrue();
            book.Series!.Name.Should().Be("Los diarios de Matabot");
            book.Annotations.Should().ContainSingle().Which.Note.Should().Be("nota");
            var track = book.AudioTracks.Should().ContainSingle().Subject;
            File.Exists(track.FilePath).Should().BeTrue();
            (await File.ReadAllTextAsync(track.FilePath)).Should().Be("audio");

            // Restaurar dos veces no duplica nada
            zip.Position = 0;
            var again = await Service(db).RestoreAsync(zip, isZip: true);
            again.AnnotationsAdded.Should().Be(0);
            again.AudioTracksAdded.Should().Be(0);
        }
    }
}
