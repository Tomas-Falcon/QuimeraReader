using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuimeraReader.Application.Books.Queries.GetBooks;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Tests.Common;
using Xunit;

namespace QuimeraReader.Tests.Application;

public class GetBooksQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldExcludeGhostBooksWithoutEpubOrAudio()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new GetBooksQueryHandler(context);

        // Libro válido con EPUB
        context.Books.Add(new Book
        {
            Id = 1,
            Title = "Libro con EPUB",
            EpubFilePath = "/media/book1.epub"
        });

        // Libro válido con Audio
        var bookWithAudio = new Book
        {
            Id = 2,
            Title = "Audiolibro",
            EpubFilePath = ""
        };
        bookWithAudio.AudioTracks.Add(new BookAudioTrack { TrackNumber = 1, FilePath = "/media/audio1.mp3" });
        context.Books.Add(bookWithAudio);

        // Libro fantasma (sin EPUB y sin Audio)
        context.Books.Add(new Book
        {
            Id = 3,
            Title = "Libro Fantasma",
            EpubFilePath = ""
        });

        await context.SaveChangesAsync();

        var query = new GetBooksQuery { Page = 1, PageSize = 10 };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Total.Should().Be(2);
        result.Data.Should().HaveCount(2);
        result.Data.Should().NotContain(b => b.Id == 3);
    }

    [Fact]
    public async Task Handle_ShouldFilterByReadingStatus()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new GetBooksQueryHandler(context);

        context.Books.AddRange(
            new Book { Id = 1, Title = "Libro En Lectura", EpubFilePath = "/books/b1.epub", ReadingStatus = "Reading" },
            new Book { Id = 2, Title = "Libro Terminado", EpubFilePath = "/books/b2.epub", ReadingStatus = "Read" },
            new Book { Id = 3, Title = "Libro Sin Empezar", EpubFilePath = "/books/b3.epub", ReadingStatus = "Unread" }
        );
        await context.SaveChangesAsync();

        var query = new GetBooksQuery { ReadingStatus = "Reading", Page = 1, PageSize = 10 };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Total.Should().Be(1);
        result.Data.Should().ContainSingle(b => b.Title == "Libro En Lectura");
    }
}
