using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using Xunit;

namespace QuimeraReader.Tests.Common;

/// <summary>
/// Índices FTS5 contra SQLite real (en memoria): los casos que fallaban en la biblioteca de producción.
/// </summary>
public class SqliteSearchIndexTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;
    private SqliteSearchIndex _index = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        await SqliteSearchIndex.EnsureUpToDateAsync(_db);
        _index = new SqliteSearchIndex(_db);

        var rowling = new Author { Name = "J. K. Rowling" };
        var wells = new Author { Name = "Martha Wells" };
        var zafon = new Author { Name = "Carlos Ruiz Zafón" };
        var otherGarcia = new Author { Name = "José María García Márquez" };
        var gabo = new Author { Name = "Gabriel García Márquez" };
        var matabot = new Series { Name = "Matabot" };

        AddBook(1, "Harry Potter y la piedra filosofal", rowling);
        AddBook(2, "Animales fantásticos y dónde encontrarlos", rowling);
        AddBook(3, "Sistemas críticos", wells, matabot);
        AddBook(4, "La sombra del viento", zafon);
        AddBook(5, "La gran represión", otherGarcia);
        AddBook(6, "Cien años de soledad", gabo);
        AddBook(7, "García Márquez: historia de un deicidio", null);
        await _db.SaveChangesAsync(); // el índice se mantiene solo al guardar
    }

    private void AddBook(int id, string title, Author? author, Series? series = null)
    {
        var book = new Book { Id = id, Title = title, EpubFilePath = $"/library/{id}.epub", Series = series };
        if (author != null) book.Authors.Add(new BookAuthor { Author = author });
        _db.Books.Add(book);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<int[]> Search(string text) =>
        (await _index.SearchBookIdsAsync(SearchQueryBuilder.Build(text)!, 100)).ToArray();

    [Theory]
    [InlineData("zafon")]          // sin tilde
    [InlineData("Zafón")]          // con tilde
    [InlineData("ruiz zafon")]
    public async Task AccentsDoNotMatter(string query)
    {
        (await Search(query)).Should().Equal(4);
    }

    [Fact]
    public async Task WordsFromTitleAndAuthorCanBeMixed()
    {
        (await Search("harry potter rowling")).Should().Equal(1);
    }

    [Theory]
    [InlineData("Wells Martha")]
    [InlineData("martha wells")]
    public async Task WordOrderDoesNotMatter(string query)
    {
        (await Search(query)).Should().Equal(3);
    }

    [Fact]
    public async Task InitialsWrittenTogetherMatchSeparatedInitials()
    {
        (await Search("JK Rowling")).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task SeriesNameIsSearchable()
    {
        (await Search("matabot")).Should().Equal(3);
    }

    [Fact]
    public async Task FullAuthorName_RanksTheirBooks_AboveNamesakes()
    {
        var results = await Search("Gabriel García Márquez");
        results.Should().Equal(6); // solo coincide del todo el de Gabriel
        (await Search("garcia marquez")).Should().HaveCount(3);
    }

    [Fact]
    public async Task RenamingAnAuthor_UpdatesBookAndAuthorIndexes()
    {
        var author = await _db.Authors.SingleAsync(a => a.Name == "Martha Wells");
        author.Name = "M. Wells Renombrada";
        await _db.SaveChangesAsync();

        (await Search("renombrada")).Should().Equal(3);
        (await _index.SearchAuthorIdsAsync(SearchQueryBuilder.Build("renombrada")!, 10)).Should().Equal(author.Id);
    }

    [Fact]
    public async Task DeletedBooks_DisappearFromTheIndex()
    {
        _db.Books.Remove(await _db.Books.SingleAsync(b => b.Id == 4));
        await _db.SaveChangesAsync();

        (await Search("zafon")).Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorSearch_IgnoresAccents()
    {
        var ids = await _index.SearchAuthorIdsAsync(SearchQueryBuilder.Build("zafon")!, 10);
        ids.Should().HaveCount(1);
    }
}
