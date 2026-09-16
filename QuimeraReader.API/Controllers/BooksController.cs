using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using QuimeraReader.Domain.Entities;
using System.Threading.Tasks;
using System.Linq;
using System.IO;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BooksController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly EpubScannerService _scannerService;
    private readonly AudioAlignmentService _alignmentService;

    private readonly AudioAlignmentQueue _queue;

    public BooksController(AppDbContext dbContext, EpubScannerService scannerService, AudioAlignmentService alignmentService, AudioAlignmentQueue queue)
    {
        _dbContext = dbContext;
        _scannerService = scannerService;
        _alignmentService = alignmentService;
        _queue = queue;
    }

    [HttpGet]
    public async Task<IActionResult> GetBooks([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        var query = _dbContext.Books.AsQueryable();
        var totalBooks = await query.CountAsync();

        var books = await query
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.Categories).ThenInclude(bc => bc.Category)
            .Include(b => b.Series).ThenInclude(s => s.Universe)
            .OrderByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new 
            {
                id = b.Id,
                title = b.Title,
                isbn = b.Isbn,
                description = b.Description,
                processing_status = b.ProcessingStatus == "SYNCED" ? "ALIGNED" : b.ProcessingStatus,
                authors = b.Authors.Select(a => new { name = a.Author!.Name, file_as = a.Author!.FileAs, role = a.Role }).ToList(),
                categories = b.Categories.Select(c => c.Category!.Name).ToList(),
                series = b.Series != null ? new { name = b.Series.Name, volume = b.SeriesVolume, universe = b.Series.Universe != null ? b.Series.Universe.Name : null } : null,
                cover_url = !string.IsNullOrEmpty(b.CoverImagePath) ? $"/api/media/books/{b.Id}/cover" : null,
                epub_url = !string.IsNullOrEmpty(b.EpubFilePath) ? $"/api/media/books/{b.Id}/epub" : null,
                audio_url = !string.IsNullOrEmpty(b.AudioFilePath) ? $"/api/media/books/{b.Id}/audio" : null,
                syncmap_url = b.ProcessingStatus == "SYNCED" ? $"/api/books/{b.Id}/syncmap" : null
            })
            .ToListAsync();

        return Ok(new 
        {
            total = totalBooks,
            page = page,
            pageSize = pageSize,
            data = books
        });
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
            id = book.Id,
            title = book.Title,
            isbn = book.Isbn,
            description = book.Description,
            processing_status = book.ProcessingStatus == "SYNCED" ? "ALIGNED" : book.ProcessingStatus,
            cover_url = !string.IsNullOrEmpty(book.CoverImagePath) ? $"/api/media/books/{book.Id}/cover" : null,
            audio_url = !string.IsNullOrEmpty(book.AudioFilePath) ? $"/api/media/books/{book.Id}/audio" : null
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
        return Ok(new { Message = "Escaneo programado. El servicio procesará los libros en segundo plano." });
    }

    [HttpPost("{bookId}/categories")]
    public async Task<IActionResult> AddCustomCategory(int bookId, [FromBody] string categoryName)
    {
        var book = await _dbContext.Books.Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == bookId);
        if (book == null) return NotFound();

        var category = await _dbContext.Set<Category>().FirstOrDefaultAsync(c => c.Name == categoryName);
        if (category == null)
        {
            category = new Category { Name = categoryName, IsUserGenerated = true };
            _dbContext.Add(category);
        }

        if (!book.Categories.Any(c => c.CategoryId == category.Id))
        {
            book.Categories.Add(new BookCategory { BookId = book.Id, Category = category });
            await _dbContext.SaveChangesAsync();
        }

        return Ok();
    }

    [HttpPost("{bookId}/positions")]
    public async Task<IActionResult> UpdatePosition(int bookId, [FromBody] object positionData)
    {
        var book = await _dbContext.Books.FirstOrDefaultAsync(b => b.Id == bookId);
        if (book == null) return NotFound();

        book.LastReadAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(book.AudioFilePath) && 
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
            .Where(b => bookIds.Contains(b.Id))
            .ToListAsync();

        int queuedCount = 0;
        foreach (var book in books)
        {
            if (!string.IsNullOrEmpty(book.AudioFilePath) && 
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
}

public class ScanRequest 
{ 
    public string FolderPath { get; set; } = string.Empty; 
}
