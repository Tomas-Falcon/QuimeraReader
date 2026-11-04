using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;

namespace QuimeraReader.API.Controllers;

/// <summary>Sagas (series): una secuencia concreta de libros, p. ej. "Percy Jackson". Pueden pertenecer a un universo.</summary>
[ApiController]
[Route("api/series")]
public class SeriesController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public SeriesController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public class AssignUniverseRequest
    {
        public int? UniverseId { get; set; }
    }

    public class RenameRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    // Libro dentro de una saga o universo (lo justo para pintar la tarjeta)
    internal static IQueryable<object> ProjectBooks(IQueryable<Book> books) => books
        .OrderBy(b => b.SeriesVolume == null).ThenBy(b => b.SeriesVolume).ThenBy(b => b.Title)
        .Select(b => new
        {
            b.Id,
            b.Title,
            b.SeriesVolume,
            Authors = b.Authors.Where(a => a.Author != null).Select(a => a.Author!.Name).ToList(),
            HasCover = b.CoverImagePath != null && b.CoverImagePath != "",
            HasEpub = b.EpubFilePath != null && b.EpubFilePath != "",
            HasAudio = b.AudioTracks.Any(),
            b.PercentageCompleted,
            b.ReadingStatus
        });

    [HttpGet]
    public async Task<IActionResult> GetSeries([FromQuery] string? search = null, [FromQuery] int? universeId = null,
        [FromQuery] bool withoutUniverse = false, [FromQuery] int offset = 0, [FromQuery] int limit = 60)
    {
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        var query = _dbContext.Series.AsNoTracking().Where(s => s.Books.Any());
        if (universeId.HasValue) query = query.Where(s => s.UniverseId == universeId);
        if (withoutUniverse) query = query.Where(s => s.UniverseId == null);

        // Las sagas son pocas comparadas con los libros: la búsqueda sin tildes se resuelve en memoria sobre los nombres
        var candidates = await query.Select(s => new { s.Id, s.Name }).ToListAsync();
        var ordered = string.IsNullOrWhiteSpace(search)
            ? candidates.OrderBy(s => s.Name)
            : candidates.Where(s => TextNormalizer.MatchesAllWords(s.Name, search))
                        .OrderBy(s => TextNormalizer.Fold(s.Name).StartsWith(TextNormalizer.Fold(search)) ? 0 : 1)
                        .ThenBy(s => s.Name);
        var pageIds = ordered.Skip(offset).Take(limit).Select(s => s.Id).ToList();

        var rows = await _dbContext.Series.AsNoTracking()
            .Where(s => pageIds.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.UniverseId,
                UniverseName = s.Universe != null ? s.Universe.Name : null,
                BookCount = s.Books.Count(),
                // Portada del primer volumen que la tenga
                SampleBookId = s.Books.Where(b => b.CoverImagePath != null && b.CoverImagePath != "")
                    .OrderBy(b => b.SeriesVolume == null).ThenBy(b => b.SeriesVolume).ThenByDescending(b => b.Id)
                    .Select(b => (int?)b.Id).FirstOrDefault()
            })
            .ToListAsync();

        // Autores por saga en una consulta plana aparte (en una sola consulta EF necesitaría APPLY, que SQLite no soporta)
        var authorPairs = await _dbContext.BookAuthors.AsNoTracking()
            .Where(ba => ba.Book.SeriesId != null && pageIds.Contains(ba.Book.SeriesId.Value))
            .Select(ba => new { SeriesId = ba.Book.SeriesId!.Value, ba.Author.Name })
            .Distinct()
            .ToListAsync();
        var authorsBySeries = authorPairs.GroupBy(p => p.SeriesId).ToDictionary(g => g.Key, g => g.Select(p => p.Name).OrderBy(n => n).Take(2).ToList());

        var position = pageIds.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
        return Ok(rows.OrderBy(r => position[r.Id]).Select(r => new
        {
            r.Id,
            r.Name,
            r.UniverseId,
            r.UniverseName,
            r.BookCount,
            Authors = authorsBySeries.GetValueOrDefault(r.Id) ?? new List<string>(),
            SampleCoverUrl = r.SampleBookId.HasValue ? $"api/media/books/{r.SampleBookId}/cover" : null
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetSeriesById(int id)
    {
        var series = await _dbContext.Series.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new { s.Id, s.Name, s.UniverseId, UniverseName = s.Universe != null ? s.Universe.Name : null })
            .FirstOrDefaultAsync();
        if (series == null) return NotFound();

        var books = await ProjectBooks(_dbContext.Books.AsNoTracking().Where(b => b.SeriesId == id)).ToListAsync();
        return Ok(new { series.Id, series.Name, series.UniverseId, series.UniverseName, Books = books });
    }

    [HttpPut("{id:int}/universe")]
    public async Task<IActionResult> AssignUniverse(int id, [FromBody] AssignUniverseRequest request)
    {
        var series = await _dbContext.Series.FindAsync(id);
        if (series == null) return NotFound();
        if (request.UniverseId.HasValue && !await _dbContext.Universes.AnyAsync(u => u.Id == request.UniverseId)) return NotFound("Universo no encontrado.");

        series.UniverseId = request.UniverseId;
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rename(int id, [FromBody] RenameRequest request)
    {
        var name = EpubSeriesParser.NormalizeName(request.Name);
        if (name.Length == 0) return BadRequest("El nombre no puede estar vacío.");
        var series = await _dbContext.Series.FindAsync(id);
        if (series == null) return NotFound();

        series.Name = name;
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Estado del relleno de sagas de los libros ya importados (se ejecuta una vez en segundo plano).</summary>
    [HttpGet("backfill-status")]
    public async Task<IActionResult> GetBackfillStatus()
    {
        var keys = new[] { BackgroundServices.SeriesBackfillBackgroundService.LastIdKey, BackgroundServices.SeriesBackfillBackgroundService.StatsKey, BackgroundServices.SeriesBackfillBackgroundService.VersionKey };
        var settings = await _dbContext.SystemSettings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(new
        {
            LastBookId = settings.GetValueOrDefault(BackgroundServices.SeriesBackfillBackgroundService.LastIdKey),
            Stats = settings.GetValueOrDefault(BackgroundServices.SeriesBackfillBackgroundService.StatsKey),
            SeriesCount = await _dbContext.Series.CountAsync(s => s.Books.Any())
        });
    }
}
