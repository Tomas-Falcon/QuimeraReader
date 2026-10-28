using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Models;

namespace QuimeraReader.Mobile.Data;

public class LocalBookRepository : ILocalBookRepository
{
    private readonly LocalAppDbContext _dbContext;

    public LocalBookRepository(LocalAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnsureCreatedAsync()
    {
        await _dbContext.Database.EnsureCreatedAsync();
        try { await _dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Books ADD COLUMN ReadingStatus TEXT;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Books ADD COLUMN EpubLocationsCache TEXT;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Books ADD COLUMN TotalPages INTEGER;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Books ADD COLUMN Description TEXT;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Books ADD COLUMN CurrentAudioTrackNumber INTEGER;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ClientLogs (Id INTEGER PRIMARY KEY AUTOINCREMENT, Level TEXT, Message TEXT, Exception TEXT, CreatedAt TEXT);"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Author (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, FileAs TEXT);"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Author ADD COLUMN FileAs TEXT;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Category (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, IsUserGenerated INTEGER);"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Category ADD COLUMN IsUserGenerated INTEGER;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS BookAuthor (BookId INTEGER, AuthorId INTEGER, Role TEXT, PRIMARY KEY(BookId, AuthorId));"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE BookAuthor ADD COLUMN Role TEXT;"); } catch { }
        try { await _dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS BookCategory (BookId INTEGER, CategoryId INTEGER, PRIMARY KEY(BookId, CategoryId));"); } catch { }
    }

    private Book MapToShared(QuimeraReader.Domain.Entities.Book? domainBook)
    {
        if (domainBook == null) return null!;
        var orderedTracks = domainBook.AudioTracks?.OrderBy(t => t.TrackNumber).ToList()
                            ?? new List<QuimeraReader.Domain.Entities.BookAudioTrack>();
        var firstAudioPath = orderedTracks.FirstOrDefault()?.FilePath;
        return new Book
        {
            Id = domainBook.Id,
            Title = domainBook.Title,
            Description = domainBook.Description,
            LocalCoverPath = domainBook.CoverImagePath,
            LocalEpubPath = domainBook.EpubFilePath,
            LocalAudioPath = firstAudioPath,
            HasCover = !string.IsNullOrEmpty(domainBook.CoverImagePath),
            HasEpub = !string.IsNullOrEmpty(domainBook.EpubFilePath),
            HasAudio = orderedTracks.Any(),
            IsAligned = domainBook.SyncMap != null && !string.IsNullOrEmpty(domainBook.SyncMap.SyncMapJson),
            IsAvailableOffline = domainBook.IsAvailableOffline,
            CurrentEpubCfi = domainBook.CurrentEpubCfi,
            PercentageCompleted = domainBook.PercentageCompleted,
            LastReadAt = domainBook.LastReadAt,
            ReadingStatus = domainBook.ReadingStatus,
            EpubLocationsCache = domainBook.EpubLocationsCache,
            TotalPages = domainBook.TotalPages,
            CurrentAudioPosition = domainBook.CurrentAudioPosition,
            CurrentAudioTrackNumber = domainBook.CurrentAudioTrackNumber,
            AudioTracks = orderedTracks.Select(t => new AudioTrackDto
            {
                Id = t.Id,
                TrackNumber = t.TrackNumber,
                FileName = t.FileName ?? Path.GetFileName(t.FilePath)
            }).ToList(),
            Authors = domainBook.Authors?.Select(a => a.Author?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
            Categories = domainBook.Categories?.Select(c => c.Category?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
            SyncMapJson = domainBook.SyncMap?.SyncMapJson
        };
    }

    private QuimeraReader.Domain.Entities.Book MapToDomain(Book sharedBook)
    {
        return new QuimeraReader.Domain.Entities.Book
        {
            Id = sharedBook.Id,
            Title = sharedBook.Title,
            Description = sharedBook.Description ?? "",
            CoverImagePath = sharedBook.LocalCoverPath ?? "",
            EpubFilePath = sharedBook.LocalEpubPath ?? "",
            IsAvailableOffline = sharedBook.IsAvailableOffline,
            CurrentEpubCfi = sharedBook.CurrentEpubCfi ?? "",
            PercentageCompleted = sharedBook.PercentageCompleted,
            LastReadAt = sharedBook.LastReadAt,
            ReadingStatus = sharedBook.ReadingStatus,
            EpubLocationsCache = sharedBook.EpubLocationsCache,
            TotalPages = sharedBook.TotalPages,
            CurrentAudioPosition = sharedBook.CurrentAudioPosition,
            CurrentAudioTrackNumber = sharedBook.CurrentAudioTrackNumber
        };
    }

    public async Task<List<Book>> GetOfflineBooksAsync()
    {
        var domainBooks = await _dbContext.Books
            .Include(b => b.Authors).ThenInclude(a => a.Author)
            .Include(b => b.Categories).ThenInclude(c => c.Category)
            .Include(b => b.AudioTracks)
            .Include(b => b.SyncMap)
            .ToListAsync();
        return domainBooks.Select(MapToShared).ToList();
    }

    public async Task<Book?> GetBookByIdAsync(int id)
    {
        var domainBook = await _dbContext.Books
            .Include(b => b.Authors).ThenInclude(a => a.Author)
            .Include(b => b.Categories).ThenInclude(c => c.Category)
            .Include(b => b.AudioTracks)
            .Include(b => b.Annotations)
            .Include(b => b.SyncMap)
            .FirstOrDefaultAsync(b => b.Id == id);
            
        return MapToShared(domainBook);
    }

    public async Task SaveBookAsync(Book book)
    {
        var existing = await _dbContext.Books
            .Include(b => b.Authors).ThenInclude(a => a.Author)
            .Include(b => b.Categories).ThenInclude(c => c.Category)
            .Include(b => b.AudioTracks)
            .Include(b => b.SyncMap)
            .FirstOrDefaultAsync(b => b.Id == book.Id);

        var dom = MapToDomain(book);
        if (existing == null)
        {
            _dbContext.Books.Add(dom);
            existing = dom;
        }
        else
        {
            // No pisar el progreso local más reciente ni el caché de páginas local si vienen vacíos
            if (string.IsNullOrEmpty(dom.EpubLocationsCache) && !string.IsNullOrEmpty(existing.EpubLocationsCache))
            {
                dom.EpubLocationsCache = existing.EpubLocationsCache;
            }
            if (existing.LastReadAt.HasValue && (!dom.LastReadAt.HasValue || existing.LastReadAt > dom.LastReadAt))
            {
                dom.CurrentEpubCfi = existing.CurrentEpubCfi;
                dom.PercentageCompleted = existing.PercentageCompleted;
                dom.CurrentAudioPosition = existing.CurrentAudioPosition;
                dom.CurrentAudioTrackNumber = existing.CurrentAudioTrackNumber;
                dom.LastReadAt = existing.LastReadAt;
                dom.ReadingStatus = existing.ReadingStatus;
            }
            _dbContext.Entry(existing).CurrentValues.SetValues(dom);
        }
        
        if (existing.Authors != null && existing.Authors.Any()) {
            _dbContext.RemoveRange(existing.Authors);
            existing.Authors.Clear();
        } else {
            existing.Authors = new List<QuimeraReader.Domain.Entities.BookAuthor>();
        }
        
        if (existing.Categories != null && existing.Categories.Any()) {
            _dbContext.RemoveRange(existing.Categories);
            existing.Categories.Clear();
        } else {
            existing.Categories = new List<QuimeraReader.Domain.Entities.BookCategory>();
        }

        if (!string.IsNullOrEmpty(book.LocalAudioPath))
        {
            existing.AudioTracks ??= new List<QuimeraReader.Domain.Entities.BookAudioTrack>();
            if (!existing.AudioTracks.Any())
            {
                existing.AudioTracks.Add(new QuimeraReader.Domain.Entities.BookAudioTrack
                {
                    BookId = existing.Id,
                    TrackNumber = 1,
                    FilePath = book.LocalAudioPath,
                    FileName = Path.GetFileName(book.LocalAudioPath)
                });
            }
            else
            {
                existing.AudioTracks.First().FilePath = book.LocalAudioPath;
            }
        }

        if (!string.IsNullOrEmpty(book.SyncMapJson))
        {
            if (existing.SyncMap == null)
            {
                existing.SyncMap = new QuimeraReader.Domain.Entities.SyncMap
                {
                    BookId = existing.Id,
                    SyncMapJson = book.SyncMapJson
                };
            }
            else
            {
                existing.SyncMap.SyncMapJson = book.SyncMapJson;
            }
        }
        
        await _dbContext.SaveChangesAsync();

        if (book.Authors != null)
        {
            foreach (var authorName in book.Authors.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
            {
                var author = await _dbContext.Set<QuimeraReader.Domain.Entities.Author>().FirstOrDefaultAsync(a => a.Name == authorName);
                if (author == null) {
                    author = new QuimeraReader.Domain.Entities.Author { Name = authorName, FileAs = authorName };
                    _dbContext.Set<QuimeraReader.Domain.Entities.Author>().Add(author);
                    await _dbContext.SaveChangesAsync(); 
                }
                existing.Authors.Add(new QuimeraReader.Domain.Entities.BookAuthor { BookId = existing.Id, AuthorId = author.Id });
            }
        }
        
        if (book.Categories != null)
        {
            foreach (var catName in book.Categories.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
            {
                var cat = await _dbContext.Set<QuimeraReader.Domain.Entities.Category>().FirstOrDefaultAsync(c => c.Name == catName);
                if (cat == null) {
                    cat = new QuimeraReader.Domain.Entities.Category { Name = catName };
                    _dbContext.Set<QuimeraReader.Domain.Entities.Category>().Add(cat);
                    await _dbContext.SaveChangesAsync();
                }
                existing.Categories.Add(new QuimeraReader.Domain.Entities.BookCategory { BookId = existing.Id, CategoryId = cat.Id });
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task SaveBooksAsync(IEnumerable<Book> books)
    {
        foreach (var b in books)
        {
            await SaveBookAsync(b);
        }
    }

    public async Task DeleteBookAsync(int id)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book != null)
        {
            _dbContext.Books.Remove(book);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateProgressAsync(int id, string cfi, double? audioPosition, double? percentage, int? audioTrackNumber = null)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book != null)
        {
            if (!string.IsNullOrEmpty(cfi)) book.CurrentEpubCfi = cfi;
            if (percentage.HasValue)
            {
                book.PercentageCompleted = percentage.Value;
                if (percentage.Value >= 99.5) book.ReadingStatus = "Read";
                else if (book.ReadingStatus != "Read") book.ReadingStatus = "Reading";
            }
            if (audioPosition.HasValue) book.CurrentAudioPosition = audioPosition.Value;
            if (audioTrackNumber.HasValue) book.CurrentAudioTrackNumber = audioTrackNumber.Value;
            book.LastReadAt = System.DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    public Task QueueAnnotationAsync(int bookId, string cfiRange, string selectedText, string colorHex, string note)
    {
        var queueStr = Microsoft.Maui.Storage.Preferences.Get("AnnotationQueue", "[]");
        var queue = System.Text.Json.JsonSerializer.Deserialize<List<AnnotationQueueItem>>(queueStr) ?? new List<AnnotationQueueItem>();
        var existing = queue.FirstOrDefault(q => q.BookId == bookId && q.CfiRange == cfiRange);
        if (existing != null)
        {
            existing.ColorHex = colorHex;
            existing.Note = note;
        }
        else
        {
            queue.Add(new AnnotationQueueItem { BookId = bookId, CfiRange = cfiRange, SelectedText = selectedText, ColorHex = colorHex, Note = note });
        }
        Microsoft.Maui.Storage.Preferences.Set("AnnotationQueue", System.Text.Json.JsonSerializer.Serialize(queue));
        return Task.CompletedTask;
    }

    public Task<List<AnnotationQueueItem>> GetQueuedAnnotationsAsync()
    {
        var queueStr = Microsoft.Maui.Storage.Preferences.Get("AnnotationQueue", "[]");
        var queue = System.Text.Json.JsonSerializer.Deserialize<List<AnnotationQueueItem>>(queueStr) ?? new List<AnnotationQueueItem>();
        return Task.FromResult(queue);
    }

    public Task RemoveQueuedAnnotationAsync(string id)
    {
        var queueStr = Microsoft.Maui.Storage.Preferences.Get("AnnotationQueue", "[]");
        var queue = System.Text.Json.JsonSerializer.Deserialize<List<AnnotationQueueItem>>(queueStr) ?? new List<AnnotationQueueItem>();
        queue.RemoveAll(a => a.Id == id);
        Microsoft.Maui.Storage.Preferences.Set("AnnotationQueue", System.Text.Json.JsonSerializer.Serialize(queue));
        return Task.CompletedTask;
    }

    public async Task<List<AnnotationDto>> GetAnnotationsAsync(int bookId)
    {
        var queued = await GetQueuedAnnotationsAsync();
        var dbAnns = await _dbContext.Books
            .Where(b => b.Id == bookId)
            .SelectMany(b => b.Annotations)
            .Select(a => new AnnotationDto
            {
                Id = a.Id,
                CfiRange = a.CfiRange,
                SelectedText = a.SelectedText,
                Note = a.Note,
                ColorHex = a.ColorHex,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        int idx = 1;
        foreach (var q in queued.Where(x => x.BookId == bookId))
        {
            var existing = dbAnns.FirstOrDefault(a => a.CfiRange == q.CfiRange);
            if (existing != null)
            {
                existing.ColorHex = q.ColorHex;
                existing.Note = q.Note;
            }
            else
            {
                dbAnns.Add(new AnnotationDto
                {
                    Id = -idx++,
                    CfiRange = q.CfiRange,
                    SelectedText = q.SelectedText,
                    Note = q.Note,
                    ColorHex = q.ColorHex,
                    CreatedAt = q.CreatedAt
                });
            }
        }
        return dbAnns;
    }

    public async Task SyncAnnotationsAsync(int bookId, List<AnnotationDto> annotations)
    {
        var book = await _dbContext.Books.Include(b => b.Annotations).FirstOrDefaultAsync(b => b.Id == bookId);
        if (book != null) {
            _dbContext.RemoveRange(book.Annotations);
            book.Annotations = annotations.Select(a => new QuimeraReader.Domain.Entities.BookAnnotation
            {
                BookId = bookId,
                CfiRange = a.CfiRange,
                SelectedText = a.SelectedText,
                Note = a.Note ?? "",
                ColorHex = a.ColorHex ?? "",
                CreatedAt = a.CreatedAt
            }).ToList();
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<string?> GetBookFileUrlAsync(int bookId, string fileType)
    {
        var book = await GetBookByIdAsync(bookId);
        if (book == null) return null;

        return fileType.ToLower() switch
        {
            "epub" => book.LocalEpubPath,
            "cover" => book.LocalCoverPath,
            "audio" => book.LocalAudioPath,
            _ => null
        };
    }
}
