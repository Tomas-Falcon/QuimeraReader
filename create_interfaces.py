import os

# 1. ILocalBookRepository
path_ilocal = 'QuimeraReader.Clients/QuimeraReader.Shared/Interfaces/ILocalBookRepository.cs'
with open(path_ilocal, 'w', encoding='utf-8') as f:
    f.write('''using System.Collections.Generic;
using System.Threading.Tasks;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Shared.Interfaces;

public interface ILocalBookRepository
{
    Task<List<Book>> GetOfflineBooksAsync();
    Task<Book?> GetBookByIdAsync(int id);
    Task SaveBookAsync(Book book);
    Task SaveBooksAsync(IEnumerable<Book> books);
    Task DeleteBookAsync(int id);
    Task UpdateProgressAsync(int id, string cfi, double? audioPosition, double? percentage);
    Task EnsureCreatedAsync();
}
''')

# 2. LocalBookRepository
path_local = 'QuimeraReader.Clients/QuimeraReader.Mobile/Data/LocalBookRepository.cs'
with open(path_local, 'w', encoding='utf-8') as f:
    f.write('''using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Shared.Interfaces;

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

    public async Task<List<Book>> GetOfflineBooksAsync()
    {
        return await _dbContext.Books
            .Include(b => b.Authors)
            .Include(b => b.Categories)
            .ToListAsync();
    }

    public async Task<Book?> GetBookByIdAsync(int id)
    {
        return await _dbContext.Books
            .Include(b => b.Authors)
            .Include(b => b.Categories)
            .Include(b => b.AudioTracks)
            .Include(b => b.Annotations)
            .Include(b => b.SyncMap)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task SaveBookAsync(Book book)
    {
        var existing = await _dbContext.Books.FindAsync(book.Id);
        if (existing == null)
        {
            _dbContext.Books.Add(book);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(book);
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task SaveBooksAsync(IEnumerable<Book> books)
    {
        foreach (var b in books)
        {
            var existing = await _dbContext.Books.FindAsync(b.Id);
            if (existing == null) _dbContext.Books.Add(b);
            else _dbContext.Entry(existing).CurrentValues.SetValues(b);
        }
        await _dbContext.SaveChangesAsync();
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
''')

# 3. IOfflineSyncWorker
path_iworker = 'QuimeraReader.Clients/QuimeraReader.Shared/Interfaces/IOfflineSyncWorker.cs'
with open(path_iworker, 'w', encoding='utf-8') as f:
    f.write('''using System.Threading.Tasks;

namespace QuimeraReader.Shared.Interfaces;

public interface IOfflineSyncWorker
{
    Task SyncNowAsync();
    bool IsSyncing { get; }
}
''')