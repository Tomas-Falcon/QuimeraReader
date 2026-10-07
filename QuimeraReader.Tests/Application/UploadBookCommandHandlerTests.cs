using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using QuimeraReader.Application.Books.Commands.UploadBook;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Tests.Common;
using Xunit;

namespace QuimeraReader.Tests.Application;

public class UploadBookCommandHandlerTests
{
    [Fact]
    public async Task Handle_EpubUpload_ShouldScanAndSaveBook()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var mockScanner = new Mock<IEpubScannerService>();
        var mockAudioMatcher = new Mock<IAudioMatchingService>();

        var scannedBook = new Book { Title = "Mocked Epub", Isbn = "123", Description = "Test", EpubFilePath = "fake/path.epub" };
        mockScanner.Setup(s => s.ScanEpubAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                   .ReturnsAsync(scannedBook);

        var handler = new UploadBookCommandHandler(dbContext, mockAudioMatcher.Object, mockScanner.Object);
        var command = new UploadBookCommand
        {
            FileName = "test_book.epub",
            Length = 1000,
            FileStream = new MemoryStream(new byte[1000])
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("Mocked Epub");
        result.HasEpub.Should().BeTrue();
        
        var bookInDb = await dbContext.Books.FirstOrDefaultAsync(b => b.Title == "Mocked Epub");
        bookInDb.Should().NotBeNull();
    }
}
