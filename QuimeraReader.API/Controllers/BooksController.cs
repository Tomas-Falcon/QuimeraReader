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
    private readonly LibraryScanState _scanState;

    public BooksController(AppDbContext dbContext, EpubScannerService scannerService, AudioAlignmentService alignmentService, AudioAlignmentQueue queue, LibraryScanState scanState)
    {
        _dbContext = dbContext;
        _scannerService = scannerService;
        _alignmentService = alignmentService;
        _queue = queue;
        _scanState = scanState;
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
                Id = b.Id,
                Title = b.Title,
                Isbn = b.Isbn,
                Description = b.Description,
                AverageRating = b.AverageRating,
                ProcessingStatus = b.ProcessingStatus == "SYNCED" ? "ALIGNED" : b.ProcessingStatus,
                Authors = b.Authors.Select(a => a.Author!.Name).ToList(),
                Categories = b.Categories.Select(c => c.Category!.Name).ToList(),
                Series = b.Series != null ? b.Series.Name : null,
                Universe = b.Series != null && b.Series.Universe != null ? b.Series.Universe.Name : null,
                HasCover = !string.IsNullOrEmpty(b.CoverImagePath),
                HasEpub = !string.IsNullOrEmpty(b.EpubFilePath),
                HasAudio = !string.IsNullOrEmpty(b.AudioFilePath),
                IsAligned = b.ProcessingStatus == "SYNCED",
                LastReadAt = b.LastReadAt,
                CurrentEpubCfi = b.CurrentEpubCfi,
                CurrentAudioPosition = b.CurrentAudioPosition,
                PercentageCompleted = b.PercentageCompleted
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
            Id = book.Id,
            Title = book.Title,
            Isbn = book.Isbn,
            Description = book.Description,
            AverageRating = book.AverageRating,
            ProcessingStatus = book.ProcessingStatus == "SYNCED" ? "ALIGNED" : book.ProcessingStatus,
            Authors = book.Authors.Select(a => a.Author!.Name).ToList(),
            Categories = book.Categories.Select(c => c.Category!.Name).ToList(),
            Series = book.Series != null ? book.Series.Name : null,
            Universe = book.Series != null && book.Series.Universe != null ? book.Series.Universe.Name : null,
            HasCover = !string.IsNullOrEmpty(book.CoverImagePath),
            HasEpub = !string.IsNullOrEmpty(book.EpubFilePath),
            HasAudio = !string.IsNullOrEmpty(book.AudioFilePath),
            IsAligned = book.ProcessingStatus == "SYNCED",
            LastReadAt = book.LastReadAt,
            CurrentEpubCfi = book.CurrentEpubCfi,
            CurrentAudioPosition = book.CurrentAudioPosition,
            PercentageCompleted = book.PercentageCompleted
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
                HasAudio = !string.IsNullOrEmpty(b.AudioFilePath),
                IsAligned = b.ProcessingStatus == "SYNCED",
                LastReadAt = b.LastReadAt,
                PercentageCompleted = b.PercentageCompleted
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

    public class UpdatePositionRequest
    {
        public string? CurrentEpubCfi { get; set; }
        public double? CurrentAudioPosition { get; set; }
        public double? PercentageCompleted { get; set; }
    }

    [HttpPost("{bookId}/positions")]
    public async Task<IActionResult> UpdatePosition(int bookId, [FromBody] UpdatePositionRequest request)
    {
        var book = await _dbContext.Books.FirstOrDefaultAsync(b => b.Id == bookId);
        if (book == null) return NotFound();

        book.LastReadAt = DateTime.UtcNow;
        if (request.CurrentEpubCfi != null) book.CurrentEpubCfi = request.CurrentEpubCfi;
        if (request.CurrentAudioPosition.HasValue) book.CurrentAudioPosition = request.CurrentAudioPosition;
        if (request.PercentageCompleted.HasValue) book.PercentageCompleted = request.PercentageCompleted;

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
