using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;

namespace QuimeraReader.API.Controllers;

/// <summary>
/// Universos: agrupan varias sagas que comparten mundo (Percy Jackson + Magnus Chase) y libros sueltos de ese mundo
/// (Animales fantásticos en el de Harry Potter). Los EPUB no traen este dato: se crean y asignan a mano, con sugerencias.
/// </summary>
[ApiController]
[Route("api/universes")]
public class UniversesController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public UniversesController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public class UniverseRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        // Al crear: sagas que se asignan directamente al nuevo universo (p. ej. desde una sugerencia)
        public List<int> SeriesIds { get; set; } = new();
    }

    [HttpGet]
    public async Task<IActionResult> GetUniverses()
    {
        var rows = await _dbContext.Universes.AsNoTracking()
            .OrderBy(u => u.Name)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Description,
                SeriesCount = u.Series.Count(),
                BookCount = u.Series.SelectMany(s => s.Books).Count() + u.Books.Count(b => b.SeriesId == null),
                SampleBookId = u.Series.SelectMany(s => s.Books).Concat(u.Books)
                    .Where(b => b.CoverImagePath != null && b.CoverImagePath != "")
                    .OrderByDescending(b => b.Id).Select(b => (int?)b.Id).FirstOrDefault()
            })
            .ToListAsync();

        // Nombres de sagas por universo en consulta aparte (evita APPLY, no soportado en SQLite)
        var seriesNames = (await _dbContext.Series.AsNoTracking().Where(s => s.UniverseId != null)
                .Select(s => new { UniverseId = s.UniverseId!.Value, s.Name }).ToListAsync())
            .GroupBy(s => s.UniverseId).ToDictionary(g => g.Key, g => g.Select(s => s.Name).OrderBy(n => n).Take(4).ToList());

        return Ok(rows.Select(r => new
        {
            r.Id,
            r.Name,
            r.Description,
            r.SeriesCount,
            r.BookCount,
            SeriesNames = seriesNames.GetValueOrDefault(r.Id) ?? new List<string>(),
            SampleCoverUrl = r.SampleBookId.HasValue ? $"api/media/books/{r.SampleBookId}/cover" : null
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUniverse(int id)
    {
        var universe = await _dbContext.Universes.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (universe == null) return NotFound();

        var series = await _dbContext.Series.AsNoTracking()
            .Where(s => s.UniverseId == id)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();

        var seriesWithBooks = new List<object>();
        foreach (var s in series)
        {
            var books = await SeriesController.ProjectBooks(_dbContext.Books.AsNoTracking().Where(b => b.SeriesId == s.Id)).ToListAsync();
            seriesWithBooks.Add(new { s.Id, s.Name, Books = books });
        }

        var standalone = await SeriesController.ProjectBooks(_dbContext.Books.AsNoTracking().Where(b => b.UniverseId == id && b.SeriesId == null)).ToListAsync();

        return Ok(new { universe.Id, universe.Name, universe.Description, Series = seriesWithBooks, StandaloneBooks = standalone });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UniverseRequest request)
    {
        var name = EpubSeriesParser.NormalizeName(request.Name);
        if (name.Length == 0) return BadRequest("El nombre no puede estar vacío.");
        if (await _dbContext.Universes.AnyAsync(u => u.Name == name)) return Conflict("Ya existe un universo con ese nombre.");

        var universe = new Universe { Name = name, Description = request.Description?.Trim() };
        _dbContext.Universes.Add(universe);
        if (request.SeriesIds.Count > 0)
        {
            var series = await _dbContext.Series.Where(s => request.SeriesIds.Contains(s.Id)).ToListAsync();
            foreach (var s in series) s.Universe = universe;
        }
        await _dbContext.SaveChangesAsync();
        return Ok(new { universe.Id, universe.Name });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UniverseRequest request)
    {
        var name = EpubSeriesParser.NormalizeName(request.Name);
        if (name.Length == 0) return BadRequest("El nombre no puede estar vacío.");
        var universe = await _dbContext.Universes.FindAsync(id);
        if (universe == null) return NotFound();
        if (await _dbContext.Universes.AnyAsync(u => u.Id != id && u.Name == name)) return Conflict("Ya existe un universo con ese nombre.");

        universe.Name = name;
        universe.Description = request.Description?.Trim();
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Borra el universo; sus sagas y libros sueltos quedan sin universo (no se borra ningún libro).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var universe = await _dbContext.Universes.Include(u => u.Series).Include(u => u.Books).FirstOrDefaultAsync(u => u.Id == id);
        if (universe == null) return NotFound();

        foreach (var s in universe.Series) s.UniverseId = null;
        foreach (var b in universe.Books) b.UniverseId = null;
        _dbContext.Universes.Remove(universe);
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Añade un libro suelto (sin saga) al universo.</summary>
    [HttpPut("{id:int}/books/{bookId:int}")]
    public async Task<IActionResult> AddBook(int id, int bookId)
    {
        if (!await _dbContext.Universes.AnyAsync(u => u.Id == id)) return NotFound();
        var book = await _dbContext.Books.FindAsync(bookId);
        if (book == null) return NotFound();

        book.UniverseId = id;
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}/books/{bookId:int}")]
    public async Task<IActionResult> RemoveBook(int id, int bookId)
    {
        var book = await _dbContext.Books.FirstOrDefaultAsync(b => b.Id == bookId && b.UniverseId == id);
        if (book == null) return NotFound();

        book.UniverseId = null;
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Sugerencias: autores con dos o más sagas sin universo (Rick Riordan -> Percy Jackson, Magnus Chase...).
    /// Es solo una pista para crear el universo de un toque; el usuario decide.
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions([FromQuery] int limit = 30)
    {
        // Consulta plana desde BookAuthors (anidar SelectMany sobre Series necesitaría APPLY, no soportado en SQLite)
        var pairs = await _dbContext.BookAuthors.AsNoTracking()
            .Where(ba => ba.Book.SeriesId != null && ba.Book.Series!.UniverseId == null)
            .Select(ba => new { AuthorName = ba.Author.Name, SeriesId = ba.Book.SeriesId!.Value, SeriesName = ba.Book.Series!.Name })
            .Distinct()
            .ToListAsync();

        var suggestions = pairs
            .GroupBy(p => p.AuthorName)
            .Select(g => new
            {
                Author = g.Key,
                Series = g.GroupBy(p => p.SeriesId).Select(sg => new { Id = sg.Key, Name = sg.First().SeriesName }).OrderBy(s => s.Name).ToList()
            })
            .Where(g => g.Series.Count >= 2)
            .OrderByDescending(g => g.Series.Count).ThenBy(g => g.Author)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();

        return Ok(suggestions);
    }
}
