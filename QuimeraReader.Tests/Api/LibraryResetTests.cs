using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Domain.Interfaces;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using QuimeraReader.Tests.Common;
using Xunit;

namespace QuimeraReader.Tests.Api;

/// <summary>Escáner con caché/duplicados y vaciado de la biblioteca, contra SQLite en archivo y EPUB reales.</summary>
public class LibraryResetTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qr-reset-" + Guid.NewGuid().ToString("N"));
    private string Library => Path.Combine(_root, "library");
    private string Media => Path.Combine(_root, "media");
    private string DbPath => Path.Combine(_root, "config", "quimerareader.db");

    private class CountingProvider : IMetadataProvider
    {
        public int Calls;
        public string ProviderName => "GoogleBooks";
        public Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null, string? authorHint = null)
        {
            Calls++;
            return Task.FromResult<BookMetadata?>(null);
        }
        public Task<List<CoverCandidate>> SearchCoversAsync(string title, string? author, Dictionary<string, string>? settings = null) => Task.FromResult(new List<CoverCandidate>());
    }

    private readonly CountingProvider _provider = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Library);
        Directory.CreateDirectory(Media);
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        db.SystemSettings.AddRange(
            new SystemSetting { Key = "LibraryRootPath", Value = Library },
            new SystemSetting { Key = "IncomingScanFolder", Value = Media },
            new SystemSetting { Key = "IngestionMode", Value = "LeaveInPlace" },
            new SystemSetting { Key = "HardcoverApiKey", Value = "secreto" },
            new SystemSetting { Key = LibraryResetService.HashBackfillVersionKey, Value = "1" });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
        return Task.CompletedTask;
    }

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={DbPath}").Options);

    private EpubScannerService Scanner(AppDbContext db) =>
        new(new[] { _provider }, new HttpClient(), db, new AudioAlignmentQueue(), NullLogger<EpubScannerService>.Instance);

    [Fact]
    public async Task Scanner_ReusesCachedMetadata_AndSkipsTheSameFileTwice()
    {
        var epub = Path.Combine(Media, "matabot.epub");
        TestEpub.Write(epub, "titulo del epub", "Martha Wells", TestEpub.FakeJpeg());
        var hash = await BookFingerprint.ComputeAsync(epub);

        await using (var db = Db())
        {
            db.BookMetadataCache.Add(new BookMetadataCache
            {
                ContentHash = hash, Title = "Sistemas críticos", Authors = "Martha Wells", Categories = "Ciencia ficción",
                SeriesName = "Los diarios de Matabot", SeriesVolume = 1, Description = "Corregida a mano"
            });
            await db.SaveChangesAsync();

            var scanner = Scanner(db);
            var book = await scanner.ScanEpubAsync(epub, "GoogleBooks");
            db.Books.Add(book);
            await db.SaveChangesAsync();
            scanner.LastScanOutcome.Should().Be("Imported");

            _provider.Calls.Should().Be(0, "con caché no se consultan servicios externos");
            book.Title.Should().Be("Sistemas críticos");
            book.Description.Should().Be("Corregida a mano");
            book.ContentHash.Should().Be(hash);
            book.Series!.Name.Should().Be("Los diarios de Matabot");
            File.Exists(book.CoverImagePath).Should().BeTrue();
        }

        // El torrent vuelve a bajar el mismo archivo con otro nombre: se ignora
        var copy = Path.Combine(Media, "otra-carpeta-matabot.epub");
        File.Copy(epub, copy);
        await using (var db = Db())
        {
            var scanner = Scanner(db);
            await scanner.ScanEpubAsync(copy, "GoogleBooks");
            await db.SaveChangesAsync();
            scanner.LastScanOutcome.Should().Be("Duplicate");
            (await db.Books.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task Reset_KeepsSettingsAndUserData_ReimportsLibraryOnlyEpubs_AndCachesMetadata()
    {
        // Libro subido (solo existe en la biblioteca), en lectura y sin conexión, con una nota
        var uploadedDir = Path.Combine(Library, "Maria Elena Walsh", "Dailan Kifki");
        Directory.CreateDirectory(uploadedDir);
        var uploaded = Path.Combine(uploadedDir, "Dailan Kifki.epub");
        TestEpub.Write(uploaded, "Dailan Kifki", "María Elena Walsh", TestEpub.FakeJpeg(1));

        // Libro del torrent (en la carpeta de entrada), en un universo
        var torrent = Path.Combine(Media, "rey.epub");
        TestEpub.Write(torrent, "Rey Brujo", "Martha Wells", TestEpub.FakeJpeg(2));

        await using (var db = Db())
        {
            var walsh = new Author { Name = "María Elena Walsh" };
            var wells = new Author { Name = "Martha Wells" };
            var kifki = new Book
            {
                Title = "Dailan Kifki", EpubFilePath = uploaded, ContentHash = await BookFingerprint.ComputeAsync(uploaded),
                ReadingStatus = "Reading", PercentageCompleted = 0.29, LastReadAt = new DateTime(2026, 11, 1), IsAvailableOffline = true,
                Description = "Un elefante en la puerta"
            };
            kifki.Authors.Add(new BookAuthor { Author = walsh });
            kifki.Annotations.Add(new BookAnnotation { CfiRange = "epubcfi(/6/4)", SelectedText = "elefante" });
            var rey = new Book
            {
                Title = "Rey Brujo", EpubFilePath = torrent, ContentHash = await BookFingerprint.ComputeAsync(torrent),
                Series = new Series { Name = "Rey Brujo", Universe = new Universe { Name = "Raksura" } }
            };
            rey.Authors.Add(new BookAuthor { Author = wells });
            db.Books.AddRange(kifki, rey);
            await db.SaveChangesAsync();
        }

        LibraryResetService.Result result;
        await using (var db = Db())
        {
            var backup = new BackupService(db, Scanner(db), NullLogger<BackupService>.Instance);
            var reset = new LibraryResetService(db, backup, Scanner(db), new LibraryScanState(), NullLogger<LibraryResetService>.Instance);

            var plan = await reset.PlanAsync();
            plan.CanRun.Should().BeTrue(string.Join(" ", plan.Blockers));
            plan.LibraryOnlyEpubs.Should().Be(1);
            plan.BooksWithUserData.Should().Be(1);

            result = await reset.RunAsync(_ => { }, _ => { });
        }

        result.MetadataCached.Should().Be(2);
        File.Exists(result.DatabaseSnapshot).Should().BeTrue();
        File.Exists(result.ZipBackup).Should().BeTrue();
        File.Exists(result.PendingRestore).Should().BeTrue();

        await using (var db = Db())
        {
            (await db.SystemSettings.SingleAsync(s => s.Key == "HardcoverApiKey")).Value.Should().Be("secreto");
            (await db.BookMetadataCache.CountAsync()).Should().Be(2);

            // Solo vuelve el libro que estaba solo en la biblioteca; el del torrent lo reimportará el escáner
            var books = await db.Books.Include(b => b.Annotations).ToListAsync();
            var kifki = books.Should().ContainSingle().Subject;
            kifki.Title.Should().Be("Dailan Kifki");
            kifki.ReadingStatus.Should().Be("Reading");
            kifki.IsAvailableOffline.Should().BeTrue();
            kifki.PercentageCompleted.Should().Be(0.29);
            kifki.Annotations.Should().ContainSingle();
            File.Exists(LibraryPathResolver.Resolve(kifki.EpubFilePath, Library)).Should().BeTrue();
            (await db.Universes.CountAsync()).Should().Be(1, "los universos se recrean desde la copia");
        }

        // El escáner reimporta el libro del torrent (sin consultar servicios: caché) y la restauración pendiente le devuelve su universo
        await using (var db = Db())
        {
            var book = await Scanner(db).ScanEpubAsync(torrent, "GoogleBooks");
            db.Books.Add(book);
            await db.SaveChangesAsync();

            var backup = new BackupService(db, Scanner(db), NullLogger<BackupService>.Instance);
            await using var pending = File.OpenRead(result.PendingRestore!);
            var report = await backup.RestoreAsync(pending, isZip: false);
            report.UniversesPending.Should().Be(0);
            report.NotFound.Should().BeEmpty();
        }

        await using (var db = Db())
        {
            var rey = await db.Books.Include(b => b.Series).SingleAsync(b => b.Title == "Rey Brujo");
            rey.Series!.UniverseId.Should().NotBeNull();
            _provider.Calls.Should().Be(0);
        }
    }
}
