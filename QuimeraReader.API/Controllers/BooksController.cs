using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using QuimeraReader.Domain.Entities;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using Microsoft.Extensions.Logging;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BooksController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly EpubScannerService _scannerService;
    private readonly AudioAlignmentService _alignmentService;
    private readonly AudioAlignmentQueue _queue;
    private readonly LibraryScanState _scanState;
    private readonly ILogger<BooksController> _logger;

    public BooksController(
        AppDbContext dbContext, 
        EpubScannerService scannerService, 
        AudioAlignmentService alignmentService, 
        AudioAlignmentQueue queue, 
        LibraryScanState scanState,
        ILogger<BooksController> logger)
    {
        _dbContext = dbContext;
        _scannerService = scannerService;
        _alignmentService = alignmentService;
        _queue = queue;
        _scanState = scanState;
        _logger = logger;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _dbContext.Categories
            .Select(c => new {
                c.Id,
                c.Name,
                c.IsUserGenerated,
                BookCount = c.Books.Count,
                SampleCoverUrl = c.Books.Where(b => b.Book.CoverImagePath != null)
                                        .Select(b => "api/media/books/" + b.Book.Id + "/cover")
                                        .FirstOrDefault()
            })
            .OrderBy(c => c.Name)
            .ToListAsync();
        return Ok(categories);
    }

    [HttpGet("authors")]
    public async Task<IActionResult> GetAuthors()
    {
        try
        {
            var authors = await _dbContext.Authors
                .Select(a => new {
                    a.Id,
                    Name = a.Name ?? "Desconocido",
                    ProfileImageUrl = (string?)null,
                    BookCount = a.Books.Count,
                    SampleCoverUrl = a.Books.Where(b => b.Book.CoverImagePath != null)
                                            .Select(b => "api/media/books/" + b.Book.Id + "/cover")
                                            .FirstOrDefault()
                })
                .OrderBy(a => a.Name)
                .ToListAsync();
                
            // Eliminar duplicados en memoria si la BD todavÃ­a tiene problemas
            var uniqueAuthors = authors
                .GroupBy(a => a.Name)
                .Select(g => g.First())
                .ToList();

            return Ok(uniqueAuthors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar autores");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetBooks([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null, [FromQuery] int[]? categoryIds = null)
    {
        try 
        {
            var mediator = HttpContext.RequestServices.GetRequiredService<MediatR.IMediator>();
            var result = await mediator.Send(new QuimeraReader.Application.Books.Queries.GetBooks.GetBooksQuery 
            { 
                Page = page, 
                PageSize = pageSize, 
                Search = search,
                CategoryIds = categoryIds?.ToList() 
            });
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar libros");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetBook(int id)
    {
        var book = await _dbContext.Books
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.Categories).ThenInclude(bc => bc.Category)
            .Include(b => b.Series)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (book == null) return NotFound();

        return Ok(new 
        {
            Id = book.Id,
            Title = book.Title,
            Isbn = book.Isbn,
            Description = book.Description,
            AverageRating = book.AverageRating,
            ProcessingStatus = book.ProcessingStatus == "SYNCED" ? "ALIGNED" : book.ProcessingStatus,
            Authors = book.Authors.Where(a => a.Author != null).Select(a => a.Author!.Name).ToList(),
            Categories = book.Categories.Where(c => c.Category != null).Select(c => c.Category!.Name).ToList(),
            Series = book.Series != null ? book.Series.Name : null,
            Universe = book.Series != null && book.Series.Universe != null ? book.Series.Universe.Name : null,
            HasCover = !string.IsNullOrEmpty(book.CoverImagePath),
            HasEpub = !string.IsNullOrEmpty(book.EpubFilePath),
            HasAudio = book.AudioTracks.Any(),
            IsAligned = book.ProcessingStatus == "SYNCED",
            LastReadAt = book.LastReadAt,
            CurrentEpubCfi = book.CurrentEpubCfi,
            CurrentAudioPosition = book.CurrentAudioPosition,
            PercentageCompleted = book.PercentageCompleted,
            ReadingStatus = book.ReadingStatus,
            EpubLocationsCache = book.EpubLocationsCache,
            TotalPages = book.TotalPages
        });
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations()
    {
        // 1. Get categories from recently read books
        var readBooksCategories = await _dbContext.Books
            .Where(b => b.LastReadAt != null)
            .Include(b => b.Categories)
            .SelectMany(b => b.Categories.Select(c => c.CategoryId))
            .Distinct()
            .ToListAsync();

        var query = _dbContext.Books
            .Where(b => !string.IsNullOrEmpty(b.EpubFilePath) || b.AudioTracks.Any())
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.Categories).ThenInclude(bc => bc.Category)
            .AsQueryable();

        // 2. Prioritize unread books from those categories
        if (readBooksCategories.Any())
        {
            query = query.Where(b => b.LastReadAt == null && b.Categories.Any(c => readBooksCategories.Contains(c.CategoryId)));
        }
        else
        {
            // Fallback: Highest rated unread books
            query = query.Where(b => b.LastReadAt == null).OrderByDescending(b => b.AverageRating);
        }

        var recommendations = await query
            .Take(8)
            .Select(b => new 
            {
                Id = b.Id,
                Title = b.Title,
                Isbn = b.Isbn,
                Description = b.Description,
                AverageRating = b.AverageRating,
                ProcessingStatus = b.ProcessingStatus == "SYNCED" ? "ALIGNED" : b.ProcessingStatus,
                Authors = b.Authors.Select(a => a.Author!.Name).ToList(),
                Categories = b.Categories.Select(c => c.Category!.Name).ToList(),
                HasCover = !string.IsNullOrEmpty(b.CoverImagePath),
                HasEpub = !string.IsNullOrEmpty(b.EpubFilePath),
                HasAudio = b.AudioTracks.Any(),
                IsAligned = b.ProcessingStatus == "SYNCED",
                LastReadAt = b.LastReadAt,
                CurrentEpubCfi = b.CurrentEpubCfi,
                CurrentAudioPosition = b.CurrentAudioPosition,
                PercentageCompleted = b.PercentageCompleted,
                ReadingStatus = b.ReadingStatus,
                EpubLocationsCache = b.EpubLocationsCache,
                TotalPages = b.TotalPages
            })
            .ToListAsync();

        return Ok(recommendations);
    }

    [HttpGet("scan/status")]
    public IActionResult GetScanStatus()
    {
        return Ok(new
        {
            _scanState.IsScanning,
            _scanState.TotalFilesFound,
            _scanState.FilesProcessed,
            _scanState.CurrentFile
        });
    }

    [HttpPost("scan")]
    public async Task<IActionResult> ScanFolder([FromBody] ScanRequest request)
    {
        if (!Directory.Exists(request.FolderPath)) return BadRequest("Directorio no encontrado.");

        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IncomingScanFolder");
        if (setting == null) 
        {
            _dbContext.SystemSettings.Add(new SystemSetting { Key = "IncomingScanFolder", Value = request.FolderPath });
        }
        else 
        {
            setting.Value = request.FolderPath;
        }

        await _dbContext.SaveChangesAsync();
        _scanState.IsScanning = true;
        return Ok(new { Message = "Escaneo programado." });
    }

    [HttpPost("scan/cancel")]
    public async Task<IActionResult> CancelScan()
    {
        _scanState.IsScanning = false;
        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IncomingScanFolder");
        if (setting != null)
        {
            setting.Value = ""; // Clear incoming scan folder to prevent background worker from picking it up again
            await _dbContext.SaveChangesAsync();
        }
        return Ok(new { Message = "Escaneo cancelado." });
    }

    [HttpPost("scan/metadata")]
    public IActionResult RescanMetadata([FromServices] IServiceScopeFactory scopeFactory, [FromServices] LibraryScanState scanState)
    {
        if (scanState.IsScanning) return BadRequest("Ya hay un escaneo en progreso.");

        _ = Task.Run(async () =>
        {
            scanState.IsScanning = true;
            scanState.FilesProcessed = 0;
            
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scanner = scope.ServiceProvider.GetRequiredService<QuimeraReader.Infrastructure.Services.EpubScannerService>();

                var ghostBooks = await dbContext.Books
                    .Include(b => b.Authors)
                    .ThenInclude(ba => ba.Author)
                    .Include(b => b.Categories)
                    .ThenInclude(bc => bc.Category)
                    .Where(b => !b.IsMetadataComplete || string.IsNullOrEmpty(b.CoverImagePath) || string.IsNullOrEmpty(b.Description))
                    .ToListAsync();

                scanState.TotalFilesFound = ghostBooks.Count;

                foreach (var book in ghostBooks)
                {
                    scanState.CurrentFile = $"Obteniendo metadatos: {book.Title}";
                    try
                    {
                        await scanner.EnrichMetadataAsync(book, "GoogleBooks");
                        await dbContext.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error enriqueciendo metadatos del libro {BookId}", book.Id);
                    }
                    scanState.FilesProcessed++;
                }
            }
            finally
            {
                // Limpieza de huÃ©rfanos generados durante la actualización de metadatos
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var orphanedAuthors = await db.Authors.Include(a => a.Books).Where(a => !a.Books.Any()).ToListAsync();
                    if (orphanedAuthors.Any()) db.Authors.RemoveRange(orphanedAuthors);

                    var orphanedCategories = await db.Categories.Include(c => c.Books).Where(c => !c.Books.Any()).ToListAsync();
                    if (orphanedCategories.Any()) db.Categories.RemoveRange(orphanedCategories);

                    var orphanedSeries = await db.Series.Include(s => s.Books).Where(s => !s.Books.Any()).ToListAsync();
                    if (orphanedSeries.Any()) db.Series.RemoveRange(orphanedSeries);

                    await db.SaveChangesAsync();

                    var orphanedUniverses = await db.Universes.Include(u => u.Series).Where(u => !u.Series.Any()).ToListAsync();
                    if (orphanedUniverses.Any()) db.Universes.RemoveRange(orphanedUniverses);

                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en la limpieza de huÃ©rfanos post-escaneo");
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var syncService = scope.ServiceProvider.GetRequiredService<QuimeraReader.Infrastructure.Services.HardcoverSyncService>();
                    await syncService.PullReadBooksAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en la sincronización con Hardcover");
                }

                scanState.IsScanning = false;
                scanState.CurrentFile = string.Empty;
                scanState.TotalFilesFound = 0;
                scanState.FilesProcessed = 0;
            }
        });
        return Ok(new { Message = "Escaneo de metadatos iniciado en segundo plano." });
    }

    [HttpPost("{id}/scan/metadata")]
    public async Task<IActionResult> RescanSingleBookMetadata(int id)
    {
        var book = await _dbContext.Books
            .Include(b => b.Authors)
            .ThenInclude(ba => ba.Author)
            .Include(b => b.Categories)
            .ThenInclude(bc => bc.Category)
            .FirstOrDefaultAsync(b => b.Id == id);
            
        if (book == null) return NotFound();

        try
        {
            await _scannerService.EnrichMetadataAsync(book, "GoogleBooks");
            await _dbContext.SaveChangesAsync();
            return Ok(new { Message = "Metadatos actualizados." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error interno: {ex.Message}");
        }
    }

    [HttpPost("maintenance/merge-duplicates")]
    public async Task<IActionResult> MergeDuplicates()
    {
        try
        {
            var mediator = HttpContext.RequestServices.GetRequiredService<MediatR.IMediator>();
            var result = await mediator.Send(new QuimeraReader.Application.Books.Commands.MergeDuplicates.MergeDuplicatesCommand());
            return Ok(new { Message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crÃ­tico durante MergeDuplicates");
            return StatusCode(500, new { Message = "Error interno durante la fusión", Details = ex.Message, Inner = ex.InnerException?.Message });
        }
    }

    [HttpPost("{bookId}/categories")]
    public async Task<IActionResult> AddCustomCategory(int bookId, [FromBody] string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return BadRequest("El nombre de la categorÃ­a no puede estar vacÃ­o.");

        var book = await _dbContext.Books.Include(b => b.Categories).ThenInclude(bc => bc.Category).FirstOrDefaultAsync(b => b.Id == bookId);
        if (book == null) return NotFound();

        var category = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Name == categoryName);
        if (category == null)
        {
            category = new Category { Name = categoryName };
            _dbContext.Categories.Add(category);
            await _dbContext.SaveChangesAsync();
        }

        if (!book.Categories.Any(bc => bc.CategoryId == category.Id))
        {
            book.Categories.Add(new BookCategory { Category = category });
            await _dbContext.SaveChangesAsync();
        }

        return Ok(new { Message = "CategorÃ­a aÃ±adida exitosamente." });
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadEpub(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No se proporcionó ningún archivo.");

        string[] audioExtensions = { ".mp3", ".m4b", ".m4a", ".wav", ".ogg" };
        bool isAudio = audioExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant());

        if (!file.FileName.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) && !isAudio)
            return BadRequest("Solo se permiten archivos .epub o audios compatibles (.mp3, .m4b, .m4a, .wav).");

        string baseTemp = Path.GetTempFileName();
        string tempPath = baseTemp + Path.GetExtension(file.FileName);
        
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            if (isAudio)
            {
                var audioMatcher = HttpContext.RequestServices.GetRequiredService<QuimeraReader.Infrastructure.Services.AudioMatchingService>();
                var matchedBookId = await audioMatcher.TryMatchAudioToBookAsync(tempPath);
                if (matchedBookId.HasValue)
                {
                    var matchedBook = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == matchedBookId.Value);
                    if (matchedBook != null)
                    {
                        string? outDir = Path.GetDirectoryName(matchedBook.EpubFilePath);
                        if (!string.IsNullOrEmpty(outDir))
                        {
                            string newAudioPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(matchedBook.EpubFilePath) + "_audio" + Path.GetExtension(file.FileName));
                            System.IO.File.Move(tempPath, newAudioPath, true);
                            matchedBook.AudioTracks.Clear();
                            matchedBook.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = 1 });
                            await _dbContext.SaveChangesAsync();
                            return Ok(new { Message = "Audio emparejado con libro existente: " + matchedBook.Title });
                        }
                    }
                }
                return BadRequest("El audio no pudo emparejarse con ningún libro de la biblioteca.");
            }

            var scannerService = HttpContext.RequestServices.GetRequiredService<QuimeraReader.Infrastructure.Services.EpubScannerService>();
            
            var book = await scannerService.ScanEpubAsync(tempPath, "GoogleBooks", file.FileName);
            
            if (book.Id == 0)
            {
                _dbContext.Books.Add(book);
            }
            else
            {
                _dbContext.Books.Update(book);
            }
            
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                Id = book.Id,
                Title = book.Title,
                HasEpub = !string.IsNullOrEmpty(book.EpubFilePath),
                HasCover = !string.IsNullOrEmpty(book.CoverImagePath),
                HasAudio = book.AudioTracks != null && book.AudioTracks.Any(),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en UploadEpub");
            return StatusCode(500, $"Error interno: {ex.Message}");
        }
        finally
        {
            // Limpieza de archivos temporales
            if (System.IO.File.Exists(baseTemp))
            {
                try { System.IO.File.Delete(baseTemp); } catch { }
            }
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }
    }

    [HttpPost("{id}/audio")]
    public async Task<IActionResult> UploadAudio(int id, IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("No se proporcionó ningún archivo de audio.");
        
        string[] audioExtensions = { ".mp3", ".m4b", ".m4a", ".wav", ".ogg" };
        string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!audioExtensions.Contains(ext)) return BadRequest("Formato de audio no soportado.");

        var book = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == id);
        if (book == null) return NotFound("Libro no encontrado.");

        string bookDir;
        if (!string.IsNullOrEmpty(book.EpubFilePath)) bookDir = Path.GetDirectoryName(book.EpubFilePath)!;
        else if (!string.IsNullOrEmpty(book.CoverImagePath)) bookDir = Path.GetDirectoryName(book.CoverImagePath)!;
        else return BadRequest("El libro no tiene una ruta física establecida.");

        int nextTrack = book.AudioTracks.Any() ? book.AudioTracks.Max(t => t.TrackNumber) + 1 : 1;
        string safeTitle = string.Join("_", book.Title.Split(Path.GetInvalidFileNameChars()));
        string newAudioPath = Path.Combine(bookDir, $"{safeTitle} track {nextTrack}{ext}");

        try
        {
            using (var stream = new FileStream(newAudioPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            book.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = nextTrack, FileName = file.FileName });
            await _dbContext.SaveChangesAsync();
            return Ok(new { Message = "Audio añadido correctamente." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error añadiendo audio al libro {BookId}", id);
            return StatusCode(500, "Error interno del servidor.");
        }
    }

        public class BulkStatusUpdateRequest
    {
        public List<int> BookIds { get; set; } = new();
        public string Status { get; set; } = string.Empty;
    }

    public class UpdateMetadataRequest
    {
        public string Title { get; set; } = string.Empty;
        public string? ReadingStatus { get; set; }
        public List<string> Categories { get; set; } = new();
    }

    public class UpdateCoverRequest
    {
        public string ImageUrl { get; set; } = string.Empty;
    }
    public class UpdatePositionRequest
    {
        public string? CurrentEpubCfi { get; set; }
        public double? CurrentAudioPosition { get; set; }
        public double? PercentageCompleted { get; set; }
    }

    [HttpPost("{bookId}/positions")]
    public async Task<IActionResult> UpdatePosition(int bookId, [FromBody] UpdatePositionRequest request)
    {
        var book = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == bookId);
        if (book == null) return NotFound();

        book.LastReadAt = DateTime.UtcNow;
        if (book.ReadingStatus != "Read") book.ReadingStatus = "Reading";
        if (request.CurrentEpubCfi != null) book.CurrentEpubCfi = request.CurrentEpubCfi;
        if (request.CurrentAudioPosition.HasValue) book.CurrentAudioPosition = request.CurrentAudioPosition;
        if (request.PercentageCompleted.HasValue) book.PercentageCompleted = request.PercentageCompleted;

        if (book.AudioTracks.Any() && 
            (string.IsNullOrEmpty(book.ProcessingStatus) || 
             (book.ProcessingStatus != "SYNCED" && book.ProcessingStatus != "PROCESSING" && book.ProcessingStatus != "PENDING_SYNC")))
        {
            book.ProcessingStatus = "PENDING_SYNC";
            await _queue.EnqueueAsync(book.Id);
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("sync-batch")]
    public async Task<IActionResult> SyncBatch([FromBody] int[] bookIds)
    {
        if (bookIds == null || !bookIds.Any()) return BadRequest("No se proporcionaron IDs.");

        var books = await _dbContext.Books
            .Include(b => b.AudioTracks)
            .Where(b => bookIds.Contains(b.Id))
            .ToListAsync();

        int queuedCount = 0;
        foreach (var book in books)
        {
            if (book.AudioTracks.Any() && 
                book.ProcessingStatus != "SYNCED" && 
                book.ProcessingStatus != "PROCESSING" && 
                book.ProcessingStatus != "PENDING_SYNC")
            {
                book.ProcessingStatus = "PENDING_SYNC";
                await _queue.EnqueueAsync(book.Id);
                queuedCount++;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok(new { Message = $"Se agregaron {queuedCount} libros a la cola de sincronización." });
    }

    [HttpGet("{id}/syncmap")]
    public async Task<IActionResult> GetSyncMap(int id)
    {
        var book = await _dbContext.Books
            .Include(b => b.SyncMap)
            .FirstOrDefaultAsync(b => b.Id == id);
            
        if (book == null || book.SyncMap == null || string.IsNullOrEmpty(book.SyncMap.SyncMapJson))
            return NotFound();

        return Content(book.SyncMap.SyncMapJson, "application/json");
    }

    [HttpDelete("{id}/epub")]
    public async Task<IActionResult> DeleteEpub(int id)
    {
        var book = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == id);
        if (book == null) return NotFound();

        if (!string.IsNullOrEmpty(book.EpubFilePath) && System.IO.File.Exists(book.EpubFilePath))
        {
            try { System.IO.File.Delete(book.EpubFilePath); } catch { }
        }
        book.EpubFilePath = null;
        
        if (!book.AudioTracks.Any())
        {
            await DeleteBookEntityAndFoldersAsync(book);
        }
        else 
        {
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    [HttpDelete("{id}/audio")]
    public async Task<IActionResult> DeleteAudio(int id)
    {
        var book = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == id);
        if (book == null) return NotFound();

        foreach (var track in book.AudioTracks)
        {
            if (!string.IsNullOrEmpty(track.FilePath) && System.IO.File.Exists(track.FilePath))
            {
                try { System.IO.File.Delete(track.FilePath); } catch { }
            }
        }
        book.AudioTracks.Clear();
        
        if (string.IsNullOrEmpty(book.EpubFilePath))
        {
            await DeleteBookEntityAndFoldersAsync(book);
        }
        else
        {
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    private async Task DeleteBookEntityAndFoldersAsync(Book book)
    {
        // Guardamos la ruta de la carpeta para borrarla luego
        string? bookDir = null;
        if (!string.IsNullOrEmpty(book.CoverImagePath)) bookDir = Path.GetDirectoryName(book.CoverImagePath);
        else if (!string.IsNullOrEmpty(book.EpubFilePath)) bookDir = Path.GetDirectoryName(book.EpubFilePath);
        else if (book.AudioTracks != null && book.AudioTracks.Any()) bookDir = Path.GetDirectoryName(book.AudioTracks.First().FilePath);

        _dbContext.Books.Remove(book);
        await _dbContext.SaveChangesAsync();
        await CleanupOrphansAsync();

        if (!string.IsNullOrEmpty(bookDir) && Directory.Exists(bookDir))
        {
            try 
            {
                // Borrar carpeta del libro y todo su contenido (cover.jpg, metadata.opf, etc.)
                Directory.Delete(bookDir, true);

                // Comprobar si la carpeta padre (Autor o Saga) quedÃ³ vacÃ­a
                var parentDir = Directory.GetParent(bookDir)?.FullName;
                if (parentDir != null && Directory.Exists(parentDir) && !Directory.EnumerateFileSystemEntries(parentDir).Any())
                {
                    Directory.Delete(parentDir);

                    // Comprobar si la carpeta abuelo (Autor si estÃ¡bamos dentro de una Saga) quedÃ³ vacÃ­a
                    var grandParentDir = Directory.GetParent(parentDir)?.FullName;
                    if (grandParentDir != null && Directory.Exists(grandParentDir) && !Directory.EnumerateFileSystemEntries(grandParentDir).Any())
                    {
                        Directory.Delete(grandParentDir);
                    }
                }
            } 
            catch (Exception ex) 
            { 
                _logger.LogError(ex, "Error limpiando directorios del libro"); 
            }
        }
    }

    private async Task CleanupOrphansAsync()
    {
        var orphanedAuthors = await _dbContext.Authors.Where(a => !a.Books.Any()).ToListAsync();
        if (orphanedAuthors.Any()) _dbContext.Authors.RemoveRange(orphanedAuthors);

        var orphanedCategories = await _dbContext.Categories.Where(c => !c.Books.Any()).ToListAsync();
        if (orphanedCategories.Any()) _dbContext.Categories.RemoveRange(orphanedCategories);

        var orphanedSeries = await _dbContext.Series.Where(s => !s.Books.Any()).ToListAsync();
        if (orphanedSeries.Any()) _dbContext.Series.RemoveRange(orphanedSeries);

        await _dbContext.SaveChangesAsync();

        var orphanedUniverses = await _dbContext.Universes.Where(u => !u.Series.Any()).ToListAsync();
        if (orphanedUniverses.Any()) _dbContext.Universes.RemoveRange(orphanedUniverses);

        await _dbContext.SaveChangesAsync();
    }
    [HttpPut("bulk/status")]
    public async Task<IActionResult> BulkUpdateStatus([FromBody] BulkStatusUpdateRequest request)
    {
        var books = await _dbContext.Books.Where(b => request.BookIds.Contains(b.Id)).ToListAsync();
        foreach (var book in books)
        {
            book.ReadingStatus = request.Status;
        }
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("{bookId}/metadata")]
    public async Task<IActionResult> UpdateMetadata(int bookId, [FromBody] UpdateMetadataRequest request)
    {
        var book = await _dbContext.Books.Include(b => b.Categories).ThenInclude(bc => bc.Category).FirstOrDefaultAsync(b => b.Id == bookId);
        if (book == null) return NotFound();

        book.Title = request.Title;
        book.ReadingStatus = request.ReadingStatus;
        
        // Remove old categories not in new list
        var toRemove = book.Categories.Where(c => !request.Categories.Contains(c.Category.Name)).ToList();
        foreach (var r in toRemove) book.Categories.Remove(r);

        // Add new categories
        var existingNames = book.Categories.Select(c => c.Category.Name).ToList();
        var toAdd = request.Categories.Where(c => !existingNames.Contains(c)).ToList();
        foreach (var newCatName in toAdd)
        {
            var cat = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Name == newCatName);
            if (cat == null) 
            {
                cat = new Category { Name = newCatName };
                _dbContext.Categories.Add(cat);
                await _dbContext.SaveChangesAsync(); // save to get ID
            }
            book.Categories.Add(new BookCategory { BookId = book.Id, CategoryId = cat.Id });
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("{bookId}/cover")]
    public async Task<IActionResult> UpdateCover(int bookId, [FromBody] UpdateCoverRequest request)
    {
        var book = await _dbContext.Books.FindAsync(bookId);
        if (book == null) return NotFound();
        
        book.CoverImagePath = request.ImageUrl; // For simplicity, using URL directly. In a real app we might download it to local storage.
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{bookId}/epub-locations")]
    public async Task<IActionResult> SaveEpubLocations(int bookId, [FromBody] string locationsJson)
    {
        var book = await _dbContext.Books.FindAsync(bookId);
        if (book == null) return NotFound();
        
        book.EpubLocationsCache = locationsJson;
        await _dbContext.SaveChangesAsync();
        return Ok();
    }
}
public class ScanRequest 
{ 
    public string FolderPath { get; set; } = string.Empty; 
}

