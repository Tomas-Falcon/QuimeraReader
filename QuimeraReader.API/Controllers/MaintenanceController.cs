using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.API.BackgroundServices;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/maintenance")]
public class MaintenanceController : ControllerBase
{
    private const string MissingFilesJob = "missing-files";
    private const string DuplicatesJob = "duplicates";

    private readonly MaintenanceJobRunner _runner;
    private readonly AppDbContext _dbContext;

    public MaintenanceController(MaintenanceJobRunner runner, AppDbContext dbContext)
    {
        _runner = runner;
        _dbContext = dbContext;
    }

    private static async Task<(string? LibraryRoot, string? Incoming)> GetFoldersAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        settings.TryGetValue("LibraryRootPath", out var libraryRoot);
        settings.TryGetValue("IncomingScanFolder", out var incoming);
        return (libraryRoot, incoming);
    }

    /// <summary>Estado de la revisión de archivos que faltan y el último informe.</summary>
    [HttpGet("missing-files")]
    public IActionResult MissingFilesStatus() => Ok(_runner.GetStatus(MissingFilesJob));

    /// <summary>
    /// Lanza en segundo plano la revisión de libros cuyo EPUB o portada no existe (recorrer /media puede tardar mucho).
    /// Con apply=true guarda las rutas reencontradas por nombre de archivo.
    /// </summary>
    [HttpPost("missing-files/run")]
    public IActionResult RunMissingFiles([FromQuery] bool apply = false)
    {
        var started = _runner.TryStart(MissingFilesJob, new { apply }, async (services, progress, token) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var (libraryRoot, incoming) = await GetFoldersAsync(db, token);
            return await LibraryFileAudit.RunAsync(db, libraryRoot, new[] { libraryRoot, incoming, "/library", "/media" }!, apply, token, progress);
        });
        return started ? Accepted(_runner.GetStatus(MissingFilesJob)) : Conflict(_runner.GetStatus(MissingFilesJob));
    }

    /// <summary>Estado de la limpieza de duplicados y su último informe.</summary>
    [HttpGet("duplicates")]
    public IActionResult DuplicatesStatus() => Ok(_runner.GetStatus(DuplicatesJob));

    /// <summary>
    /// Une libros repetidos (mismo hash, o sin archivo con una copia del mismo título y autor) conservando
    /// lo que hizo el usuario. Sin apply solo informa. deleteOrphans borra además los libros sin archivo, sin copia y sin datos del usuario.
    /// </summary>
    [HttpPost("duplicates/run")]
    public IActionResult RunDuplicates([FromQuery] bool apply = false, [FromQuery] bool deleteOrphans = false)
    {
        var started = _runner.TryStart(DuplicatesJob, new { apply, deleteOrphans }, async (services, progress, token) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var (libraryRoot, _) = await GetFoldersAsync(db, token);
            return await BookDeduplicator.RunAsync(db, libraryRoot, apply, deleteOrphans, token);
        });
        return started ? Accepted(_runner.GetStatus(DuplicatesJob)) : Conflict(_runner.GetStatus(DuplicatesJob));
    }

    /// <summary>
    /// Copia de seguridad de todo lo que el usuario ha hecho con sus libros (estado, progreso, sin conexión, notas, audio).
    /// Se devuelve y además se guarda junto a la base de datos, para poder reaplicarla tras una limpieza.
    /// </summary>
    [HttpGet("reading-state")]
    public async Task<IActionResult> ReadingState(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        settings.TryGetValue("LibraryRootPath", out var libraryRoot);

        var books = await _dbContext.Books.AsNoTracking()
            .Where(b => (b.ReadingStatus != null && b.ReadingStatus != "Unread") || b.IsAvailableOffline || b.LastReadAt != null
                        || b.PercentageCompleted > 0 || b.CurrentAudioPosition > 0 || b.CurrentEpubCfi != null
                        || b.Annotations.Any() || b.AudioTracks.Any())
            .Select(b => new
            {
                b.Id, b.Title, b.Isbn, b.ReadingStatus, b.IsAvailableOffline, b.LastReadAt, b.PercentageCompleted,
                b.CurrentEpubCfi, b.CurrentAudioPosition, b.CurrentAudioTrackNumber, b.EpubFilePath, b.SourceFilePath,
                b.SeriesId, b.SeriesVolume, b.UniverseId
            })
            .ToListAsync(cancellationToken);
        var ids = books.Select(b => b.Id).ToList();

        var authors = (await _dbContext.BookAuthors.AsNoTracking().Where(ba => ids.Contains(ba.BookId)).Select(ba => new { ba.BookId, ba.Author.Name }).ToListAsync(cancellationToken))
            .GroupBy(x => x.BookId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());
        var annotations = (await _dbContext.BookAnnotations.AsNoTracking().Where(a => ids.Contains(a.BookId)).ToListAsync(cancellationToken))
            .GroupBy(a => a.BookId).ToDictionary(g => g.Key, g => g.ToList());
        var tracks = (await _dbContext.BookAudioTracks.AsNoTracking().Where(t => ids.Contains(t.BookId)).Select(t => new { t.BookId, t.TrackNumber, t.FilePath, t.FileName }).ToListAsync(cancellationToken))
            .GroupBy(t => t.BookId).ToDictionary(g => g.Key, g => g.OrderBy(t => t.TrackNumber).ToList());

        var snapshot = new
        {
            CreatedAt = DateTime.UtcNow,
            Books = books.Select(b => new
            {
                Book = b,
                Authors = authors.GetValueOrDefault(b.Id) ?? new(),
                EpubExists = LibraryPathResolver.Resolve(b.EpubFilePath, libraryRoot) != null,
                Annotations = annotations.GetValueOrDefault(b.Id),
                AudioTracks = tracks.GetValueOrDefault(b.Id)
            }).ToList()
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var dbPath = Environment.GetEnvironmentVariable("QUIMERA_DB_PATH") ?? "quimerareader.db";
        var backupPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", $"reading-state-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await System.IO.File.WriteAllTextAsync(backupPath, json, cancellationToken);

        return Content(json, "application/json");
    }
}
