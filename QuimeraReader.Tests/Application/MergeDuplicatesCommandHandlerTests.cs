using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using QuimeraReader.Application.Books.Commands.MergeDuplicates;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Tests.Common;
using Xunit;

namespace QuimeraReader.Tests.Application;

public class MergeDuplicatesCommandHandlerTests
{
    private readonly Mock<ILogger<MergeDuplicatesCommandHandler>> _loggerMock = new();

    [Fact]
    public async Task Handle_WhenNoDuplicatesExist_ShouldReturnZeroMerged()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new MergeDuplicatesCommandHandler(context, _loggerMock.Object);

        var book1 = new Book { Id = 1, Title = "El Imperio Final", EpubFilePath = "/media/book1.epub" };
        var book2 = new Book { Id = 2, Title = "Juego de Tronos", EpubFilePath = "/media/book2.epub" };

        var author1 = new Author { Id = 1, Name = "Brandon Sanderson" };
        var author2 = new Author { Id = 2, Name = "George R.R. Martin" };
        var cat1 = new Category { Id = 1, Name = "Fantasía" };
        var cat2 = new Category { Id = 2, Name = "Ciencia Ficción" };

        context.Books.AddRange(book1, book2);
        context.Authors.AddRange(author1, author2);
        context.Categories.AddRange(cat1, cat2);

        context.BookAuthors.AddRange(
            new BookAuthor { BookId = 1, AuthorId = 1 },
            new BookAuthor { BookId = 2, AuthorId = 2 }
        );
        context.BookCategories.AddRange(
            new BookCategory { BookId = 1, CategoryId = 1 },
            new BookCategory { BookId = 2, CategoryId = 2 }
        );
        await context.SaveChangesAsync();

        // Act
        var result = await handler.Handle(new MergeDuplicatesCommand(), CancellationToken.None);

        // Assert
        result.AuthorsMerged.Should().Be(0);
        result.CategoriesMerged.Should().Be(0);
        context.Authors.Count().Should().Be(2);
        context.Categories.Count().Should().Be(2);
    }

    [Fact]
    public async Task Handle_WhenDuplicateAuthorsExist_ShouldMergeIntoLowestIdAndReassignBooks()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new MergeDuplicatesCommandHandler(context, _loggerMock.Object);

        var author1 = new Author { Id = 1, Name = "Brandon Sanderson" };
        var author2 = new Author { Id = 2, Name = "Brandon Sanderson" }; // Duplicado

        var book1 = new Book { Id = 10, Title = "Mistborn" };
        var book2 = new Book { Id = 20, Title = "El Camino de los Reyes" };

        context.Authors.AddRange(author1, author2);
        context.Books.AddRange(book1, book2);

        // Asignamos book1 a author1 y book2 a author2
        context.BookAuthors.AddRange(
            new BookAuthor { BookId = 10, AuthorId = 1 },
            new BookAuthor { BookId = 20, AuthorId = 2 }
        );
        await context.SaveChangesAsync();

        // Act
        var result = await handler.Handle(new MergeDuplicatesCommand(), CancellationToken.None);

        // Assert
        result.AuthorsMerged.Should().Be(1);

        // El autor 2 debe haber sido eliminado, quedando solo el autor con menor ID (1)
        var remainingAuthors = await context.Authors.ToListAsync();
        remainingAuthors.Should().ContainSingle(a => a.Id == 1 && a.Name == "Brandon Sanderson");

        // Ambos libros deben apuntar ahora al autor 1
        var bookAuthors = await context.BookAuthors.ToListAsync();
        bookAuthors.Should().HaveCount(2);
        bookAuthors.All(ba => ba.AuthorId == 1).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenDuplicateCategoriesExist_ShouldMergeIntoLowestIdAndReassignBooks()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new MergeDuplicatesCommandHandler(context, _loggerMock.Object);

        var cat1 = new Category { Id = 5, Name = "Fantasía" };
        var cat2 = new Category { Id = 12, Name = "Fantasía" }; // Duplicada

        var book = new Book { Id = 1, Title = "El Hobbit" };

        context.Categories.AddRange(cat1, cat2);
        context.Books.Add(book);
        context.BookCategories.Add(new BookCategory { BookId = 1, CategoryId = 12 });
        await context.SaveChangesAsync();

        // Act
        var result = await handler.Handle(new MergeDuplicatesCommand(), CancellationToken.None);

        // Assert
        result.CategoriesMerged.Should().Be(1);
        context.Categories.Should().ContainSingle(c => c.Id == 5 && c.Name == "Fantasía");
        context.BookCategories.Should().ContainSingle(bc => bc.BookId == 1 && bc.CategoryId == 5);
    }
}
