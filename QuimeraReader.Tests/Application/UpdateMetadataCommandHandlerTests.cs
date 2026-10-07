using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Books.Commands.UpdateMetadata;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Tests.Common;
using Xunit;

namespace QuimeraReader.Tests.Application;

public class UpdateMetadataCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldUpdateTitleAndReadingStatus()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var book = new Book { Title = "Old Title", ReadingStatus = "Unread", Isbn = "123", Description = "Test" };
        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync();

        var handler = new UpdateMetadataCommandHandler(dbContext);
        var command = new UpdateMetadataCommand
        {
            BookId = book.Id,
            Title = "New Title",
            ReadingStatus = "Reading",
            Categories = new System.Collections.Generic.List<string>()
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var updatedBook = await dbContext.Books.FindAsync(book.Id);
        updatedBook!.Title.Should().Be("New Title");
        updatedBook.ReadingStatus.Should().Be("Reading");
        updatedBook.LastReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ShouldUpdateCategories()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var catOld = new Category { Name = "Sci-Fi" };
        var book = new Book { Title = "Test", Isbn = "1", Description = "Test" };
        
        book.Categories.Add(new BookCategory { Category = catOld, Book = book });
        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync();

        var handler = new UpdateMetadataCommandHandler(dbContext);
        var command = new UpdateMetadataCommand
        {
            BookId = book.Id,
            Title = "Test",
            ReadingStatus = "Unread",
            Categories = new System.Collections.Generic.List<string> { "Fantasy", "Drama" }
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var updatedBook = await dbContext.Books
            .Include(b => b.Categories).ThenInclude(bc => bc.Category)
            .FirstOrDefaultAsync(b => b.Id == book.Id);
            
        var catNames = updatedBook!.Categories.Select(c => c.Category!.Name).ToList();
        catNames.Should().HaveCount(2);
        catNames.Should().Contain("Fantasy");
        catNames.Should().Contain("Drama");
        catNames.Should().NotContain("Sci-Fi");
    }
}
