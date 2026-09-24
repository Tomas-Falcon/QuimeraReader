import sys

# 1. Update ILocalBookRepository
path_ilocal = 'QuimeraReader.Clients/QuimeraReader.Shared/Interfaces/ILocalBookRepository.cs'
with open(path_ilocal, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('using QuimeraReader.Domain.Entities;', 'using QuimeraReader.Shared.Models;')
with open(path_ilocal, 'w', encoding='utf-8') as f:
    f.write(content)

# 2. Update MauiOfflineSyncWorker
path_worker = 'QuimeraReader.Clients/QuimeraReader.Mobile/Services/MauiOfflineSyncWorker.cs'
with open(path_worker, 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace('using QuimeraReader.Domain.Entities;', 'using QuimeraReader.Shared.Models;')
with open(path_worker, 'w', encoding='utf-8') as f:
    f.write(content)

# 3. Update LocalBookRepository (it needs to map from Domain to Shared)
# Wait, actually, if LocalBookRepository gets Shared.Models.Book, it needs to map to Domain.Entities.Book!
# Let's just rewrite LocalBookRepository.cs entirely to handle the mapping.
path_local = 'QuimeraReader.Clients/QuimeraReader.Mobile/Data/LocalBookRepository.cs'
new_local = '''using System.Collections.Generic;
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
    }

    private Book MapToShared(QuimeraReader.Domain.Entities.Book domainBook)
    {
        if (domainBook == null) return null;
        return new Book
        {
            Id = domainBook.Id,
            Title = domainBook.Title,
            CoverImagePath = domainBook.CoverImagePath,
            EpubFilePath = domainBook.EpubFilePath,
            IsAvailableOffline = domainBook.IsAvailableOffline,
            CurrentEpubCfi = domainBook.CurrentEpubCfi,
            PercentageCompleted = domainBook.PercentageCompleted,
            Authors = domainBook.Authors?.Select(a => a.Author?.Name ?? "").ToList() ?? new List<string>(),
            Categories = domainBook.Categories?.Select(c => c.Category?.Name ?? "").ToList() ?? new List<string>()
        };
    }

    private QuimeraReader.Domain.Entities.Book MapToDomain(Book sharedBook)
    {
        return new QuimeraReader.Domain.Entities.Book
        {
            Id = sharedBook.Id,
            Title = sharedBook.Title,
            CoverImagePath = sharedBook.CoverImagePath,
            EpubFilePath = sharedBook.EpubFilePath,
            IsAvailableOffline = sharedBook.IsAvailableOffline,
            CurrentEpubCfi = sharedBook.CurrentEpubCfi,
            PercentageCompleted = sharedBook.PercentageCompleted
        };
    }

    public async Task<List<Book>> GetOfflineBooksAsync()
    {
        var domainBooks = await _dbContext.Books
            .Include(b => b.Authors)
            .ThenInclude(a => a.Author)
            .Include(b => b.Categories)
            .ThenInclude(c => c.Category)
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
        var existing = await _dbContext.Books.FindAsync(book.Id);
        if (existing == null)
        {
            _dbContext.Books.Add(MapToDomain(book));
        }
        else
        {
            var dom = MapToDomain(book);
            _dbContext.Entry(existing).CurrentValues.SetValues(dom);
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

    public async Task UpdateProgressAsync(int id, string cfi, double? audioPosition, double? percentage)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book != null)
        {
            if (!string.IsNullOrEmpty(cfi)) book.CurrentEpubCfi = cfi;
            if (percentage.HasValue) book.PercentageCompleted = percentage.Value;
            await _dbContext.SaveChangesAsync();
        }
    }
}
'''
with open(path_local, 'w', encoding='utf-8') as f:
    f.write(new_local)