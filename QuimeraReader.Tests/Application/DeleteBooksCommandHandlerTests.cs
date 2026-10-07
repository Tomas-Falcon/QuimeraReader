using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Books.Commands.DeleteBooks;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Tests.Common;
using Xunit;

namespace QuimeraReader.Tests.Application;

public class DeleteBooksCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldDeleteBookAndCleanupOrphans()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        
        var authorToOrphan = new Author { Name = "Author Orphan" };
        var authorToKeep = new Author { Name = "Author Keep" };
        
        var bookToDelete = new Book { Title = "Delete Me", Isbn = "1", Description = "Test" };
        var bookToKeep = new Book { Title = "Keep Me", Isbn = "2", Description = "Test" };

        bookToDelete.Authors.Add(new BookAuthor { Author = authorToOrphan, Book = bookToDelete });
        bookToKeep.Authors.Add(new BookAuthor { Author = authorToKeep, Book = bookToKeep });

        dbContext.Books.Add(bookToDelete);
        dbContext.Books.Add(bookToKeep);
        await dbContext.SaveChangesAsync();

        var handler = new DeleteBooksCommandHandler(dbContext);
        var command = new DeleteBooksCommand { Ids = new[] { bookToDelete.Id } };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var booksInDb = await dbContext.Books.ToListAsync();
        booksInDb.Should().HaveCount(1);
        booksInDb.First().Title.Should().Be("Keep Me");

        var authorsInDb = await dbContext.Authors.ToListAsync();
        authorsInDb.Should().HaveCount(1);
        authorsInDb.First().Name.Should().Be("Author Keep"); // The orphan author was deleted!
    }
}
