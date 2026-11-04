using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Domain.Interfaces;
using QuimeraReader.Infrastructure.Services;
using QuimeraReader.Tests.Common;
using Xunit;

namespace QuimeraReader.Tests.Api;

public class CoverRepairServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qr-covers-" + Guid.NewGuid().ToString("N"));
    private readonly string _library;
    private readonly string _torrent;

    public CoverRepairServiceTests()
    {
        _library = Path.Combine(_root, "library");
        _torrent = Path.Combine(_root, "media");
        Directory.CreateDirectory(Path.Combine(_library, "Martha Wells", "Sistemas criticos"));
        Directory.CreateDirectory(_torrent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Sin red en los tests");
    }

    private static CoverRepairService Service() =>
        new(null!, new NoHttp(), Array.Empty<IMetadataProvider>(), NullLogger<CoverRepairService>.Instance);

    [Fact]
    public async Task LostCover_IsExtractedFromTheEpub_AndSavedNextToIt()
    {
        var cover = TestEpub.FakeJpeg();
        var epub = Path.Combine(_library, "Martha Wells", "Sistemas criticos", "Sistemas criticos.epub");
        TestEpub.Write(epub, "Sistemas críticos", "Martha Wells", cover);
        var book = new Book { Id = 665, Title = "Sistemas críticos", EpubFilePath = epub, CoverImagePath = "/app/media/books/x/cover.jpg" };

        CoverRepairService.HasLocalCover(book, _library).Should().BeFalse();
        var source = await Service().RepairAsync(book, _library, online: false);

        source.Should().Be(CoverRepairService.Source.Epub);
        book.CoverImagePath.Should().Be(Path.Combine(Path.GetDirectoryName(epub)!, "cover.jpg"));
        (await File.ReadAllBytesAsync(book.CoverImagePath!)).Should().Equal(cover);
        CoverRepairService.HasLocalCover(book, _library).Should().BeTrue();
    }

    [Fact]
    public async Task EpubOutsideTheLibrary_TorrentMode_SavesCoverInLibraryCovers()
    {
        var epub = Path.Combine(_torrent, "descarga.epub");
        TestEpub.Write(epub, "Rey Brujo", "Martha Wells", TestEpub.FakeJpeg(0x11));
        var book = new Book { Id = 1664, Title = "Rey Brujo", EpubFilePath = epub };

        (await Service().RepairAsync(book, _library, online: false)).Should().Be(CoverRepairService.Source.Epub);

        book.CoverImagePath.Should().Be(Path.Combine(_library, "_covers", "001", "1664.jpg"));
        File.Exists(book.CoverImagePath).Should().BeTrue();
        Directory.GetFiles(_torrent).Should().ContainSingle("no se escribe en la carpeta de descargas");
    }

    [Fact]
    public async Task EpubWithoutCover_AndOffline_ReportsNone()
    {
        var epub = Path.Combine(_library, "sin-portada.epub");
        TestEpub.Write(epub, "Sin portada", "Nadie");
        var book = new Book { Id = 1, Title = "Sin portada", EpubFilePath = epub };

        (await Service().RepairAsync(book, _library, online: false)).Should().Be(CoverRepairService.Source.None);
        book.CoverImagePath.Should().BeNull();
    }

    [Theory]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C })] // "<html"
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF })]             // JPEG de 3 bytes: el "sin portada" de algunos servicios
    public void IsImage_RejectsNonImagesAndPlaceholders(byte[] bytes) => CoverRepairService.IsImage(bytes).Should().BeFalse();
}
