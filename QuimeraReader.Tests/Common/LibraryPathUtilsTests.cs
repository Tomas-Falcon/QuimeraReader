using System;
using System.IO;
using FluentAssertions;
using QuimeraReader.Application.Common;
using Xunit;

namespace QuimeraReader.Tests.Common;

public class LibraryPathUtilsTests
{
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\win.ini")]
    [InlineData("/var/lib/x.mp3")]
    [InlineData("C:\\fakepath\\x.mp3")]
    [InlineData("..")]
    [InlineData(".")]
    public void SafeFileName_ShouldNeverEscapeTheTargetDirectory(string malicious)
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "qr-root");

        // Act
        var safe = LibraryPathUtils.SafeFileName(malicious);
        var combined = Path.GetFullPath(Path.Combine(root, safe));

        // Assert
        safe.Should().NotContain("/").And.NotContain("\\");
        Path.GetDirectoryName(combined).Should().Be(Path.GetFullPath(root));
    }

    [Fact]
    public void SafeFileName_ShouldKeepTitlesWithSlashesReadable()
    {
        LibraryPathUtils.SafeFileName("AC/DC: Highway").Should().Be("AC_DC_ Highway");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SafeFileName_ShouldUseFallbackForEmptyNames(string? name)
    {
        LibraryPathUtils.SafeFileName(name, "Unknown").Should().Be("Unknown");
    }

    [Fact]
    public void GetUniqueFilePath_ShouldNotOverwriteExistingFiles()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), "qr-unique-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "track.mp3"), "a");
            File.WriteAllText(Path.Combine(dir, "track (2).mp3"), "b");

            // Act
            var path = LibraryPathUtils.GetUniqueFilePath(dir, "track.mp3");

            // Assert
            path.Should().Be(Path.Combine(dir, "track (3).mp3"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BookOutputDir_IsTheEpubFolderInTheLibrary_NeverTheCoverOrAnotherBooksAudio()
    {
        var book = new QuimeraReader.Domain.Entities.Book
        {
            Title = "Sistemas críticos",
            EpubFilePath = "/library/Martha Wells/Sistemas críticos/Sistemas críticos.epub",
            CoverImagePath = "https://books.google.com/cover.jpg"
        };
        book.AudioTracks.Add(new QuimeraReader.Domain.Entities.BookAudioTrack { FilePath = "/library/Garcilaso de la Vega/Obras/x.mp3" });

        LibraryPathUtils.GetBookOutputDir(book, "/library").Replace("\\", "/").Should().Be("/library/Martha Wells/Sistemas críticos");
    }

    [Fact]
    public void BookOutputDir_EpubInTheTorrentFolder_UsesAuthorAndTitleInTheLibrary()
    {
        var book = new QuimeraReader.Domain.Entities.Book { Title = "Rey Brujo", EpubFilePath = "/media/biblioteca/Rey_Brujo.epub" };
        book.Authors.Add(new QuimeraReader.Domain.Entities.BookAuthor { Author = new QuimeraReader.Domain.Entities.Author { Name = "Martha Wells" } });

        LibraryPathUtils.GetBookOutputDir(book, "/library").Replace("\\", "/").Should().Be("/library/Martha Wells/Rey Brujo");
    }

    [Theory]
    [InlineData("capitulo.m4b", null, ".m4b")]
    [InlineData("Dailan Kifki", "/tmp/upload.MP3", ".mp3")]
    [InlineData(null, "/library/x/track1", ".mp3")]
    public void AudioExtension_NeverEmpty(string? original, string? physical, string expected) =>
        LibraryPathUtils.AudioExtension(original, physical).Should().Be(expected);
}
