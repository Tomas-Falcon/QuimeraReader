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

public class LibraryFileAuditTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qr-audit-" + Guid.NewGuid().ToString("N"));
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        Directory.CreateDirectory(Path.Combine(_root, "moved", "Autor"));
        Directory.CreateDirectory(Path.Combine(_root, "ok"));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Touch(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public async Task Report_GroupsMissingFiles_AndRepairFindsThemByName()
    {
        var okEpub = Touch("ok", "bueno.epub");
        var movedEpub = Touch("moved", "Autor", "movido.epub");
        var movedCover = Touch("moved", "Autor", "movido.jpg");
        _db.Books.AddRange(
            new Book { Title = "Bueno", EpubFilePath = okEpub },
            new Book { Title = "Movido", EpubFilePath = "/media/torrents/viejo/movido.epub", CoverImagePath = "/media/torrents/viejo/movido.jpg" },
            new Book { Title = "Perdido", EpubFilePath = "/media/torrents/viejo/perdido.epub" });
        await _db.SaveChangesAsync();

        var report = await LibraryFileAudit.RunAsync(_db, null, new[] { _root }, apply: false);

        report.EpubOk.Should().Be(1);
        report.EpubMissing.Should().Be(2);
        report.EpubFoundElsewhere.Should().Be(1);
        report.CoverFoundElsewhere.Should().Be(1);
        report.MissingEpubFolders.Single().Folder.Should().Be("/media/torrents/viejo");
        report.Repaired.Should().Be(0);

        var applied = await LibraryFileAudit.RunAsync(_db, null, new[] { _root }, apply: true);
        applied.Repaired.Should().Be(1);

        _db.ChangeTracker.Clear();
        var movido = await _db.Books.SingleAsync(b => b.Title == "Movido");
        movido.EpubFilePath.Should().Be(movedEpub);
        movido.CoverImagePath.Should().Be(movedCover);
        (await _db.Books.SingleAsync(b => b.Title == "Perdido")).EpubFilePath.Should().Be("/media/torrents/viejo/perdido.epub");
    }
}
