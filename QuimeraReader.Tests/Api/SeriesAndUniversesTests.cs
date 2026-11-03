using System.Collections;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.API.Controllers;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;
using Xunit;

namespace QuimeraReader.Tests.Api;

/// <summary>
/// Controladores de sagas y universos contra SQLite real: algunas consultas que funcionan en memoria
/// necesitan APPLY en SQL y fallan solo en SQLite (pasó con las sugerencias y la lista de sagas).
/// </summary>
public class SeriesAndUniversesTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        var riordan = new Author { Name = "Rick Riordan" };
        var percy = new Series { Name = "Percy Jackson" };
        var magnus = new Series { Name = "Magnus Chase" };
        _db.Books.AddRange(
            Book("El ladrón del rayo", riordan, percy, 1),
            Book("El mar de los monstruos", riordan, percy, 2),
            Book("La espada del tiempo", riordan, magnus, 1));
        await _db.SaveChangesAsync();
    }

    private static Book Book(string title, Author author, Series series, double volume)
    {
        var book = new Book { Title = title, EpubFilePath = $"/library/{title}.epub", Series = series, SeriesVolume = volume };
        book.Authors.Add(new BookAuthor { Author = author });
        return book;
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(((OkObjectResult)result).Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    [Fact]
    public async Task SeriesList_WithAuthorsAndCounts_AndAccentInsensitiveSearch()
    {
        var controller = new SeriesController(_db);

        var all = Json(await controller.GetSeries());
        all.EnumerateArray().Select(s => s.GetProperty("name").GetString()).Should().Equal("Magnus Chase", "Percy Jackson");
        all[1].GetProperty("bookCount").GetInt32().Should().Be(2);
        all[1].GetProperty("authors")[0].GetString().Should().Be("Rick Riordan");

        var found = Json(await controller.GetSeries(search: "PERCY"));
        found.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task SeriesDetail_OrdersBooksByVolume()
    {
        var percyId = await _db.Series.Where(s => s.Name == "Percy Jackson").Select(s => s.Id).SingleAsync();
        var detail = Json(await new SeriesController(_db).GetSeriesById(percyId));

        detail.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("title").GetString())
            .Should().Equal("El ladrón del rayo", "El mar de los monstruos");
    }

    [Fact]
    public async Task Suggestions_GroupSeriesOfTheSameAuthor_AndCreatingTheUniverseConsumesThem()
    {
        var universes = new UniversesController(_db);

        var suggestions = Json(await universes.GetSuggestions());
        suggestions.GetArrayLength().Should().Be(1);
        suggestions[0].GetProperty("author").GetString().Should().Be("Rick Riordan");
        var seriesIds = suggestions[0].GetProperty("series").EnumerateArray().Select(s => s.GetProperty("id").GetInt32()).ToList();
        seriesIds.Should().HaveCount(2);

        var created = await universes.Create(new UniversesController.UniverseRequest { Name = "Riordanverso", SeriesIds = seriesIds });
        created.Should().BeOfType<OkObjectResult>();

        Json(await universes.GetSuggestions()).GetArrayLength().Should().Be(0);
        var list = Json(await universes.GetUniverses());
        list[0].GetProperty("seriesCount").GetInt32().Should().Be(2);
        list[0].GetProperty("bookCount").GetInt32().Should().Be(3);
        list[0].GetProperty("seriesNames").EnumerateArray().Select(n => n.GetString()).Should().Equal("Magnus Chase", "Percy Jackson");
    }

    [Fact]
    public async Task DeletingAUniverse_KeepsItsSeriesAndBooks()
    {
        var universes = new UniversesController(_db);
        var seriesIds = await _db.Series.Select(s => s.Id).ToListAsync();
        var created = Json(await universes.Create(new UniversesController.UniverseRequest { Name = "Riordanverso", SeriesIds = seriesIds }));

        (await universes.Delete(created.GetProperty("id").GetInt32())).Should().BeOfType<OkResult>();

        _db.ChangeTracker.Clear();
        (await _db.Series.CountAsync(s => s.UniverseId == null)).Should().Be(2);
        (await _db.Books.CountAsync()).Should().Be(3);
    }
}
