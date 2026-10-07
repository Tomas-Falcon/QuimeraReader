using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Application.Books.Commands.DeleteBooks;

public class DeleteBooksCommandHandler : IRequestHandler<DeleteBooksCommand, Unit>
{
    private readonly IAppDbContext _dbContext;

    public DeleteBooksCommandHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Unit> Handle(DeleteBooksCommand request, CancellationToken cancellationToken)
    {
        if (request.Ids == null || request.Ids.Length == 0) return Unit.Value;

        var books = await _dbContext.Books
            .Include(b => b.AudioTracks)
            .Where(b => request.Ids.Contains(b.Id))
            .ToListAsync(cancellationToken);

        if (books.Any())
        {
            await DeleteBooksInternalAsync(books, cancellationToken);
        }

        return Unit.Value;
    }

    private async Task DeleteBooksInternalAsync(List<Book> books, CancellationToken cancellationToken)
    {
        var dirsToDelete = new HashSet<string>();

        foreach (var book in books)
        {
            if (!string.IsNullOrEmpty(book.SourceFilePath) && File.Exists(book.SourceFilePath))
                try { File.Delete(book.SourceFilePath); } catch { }
            
            if (!string.IsNullOrEmpty(book.EpubFilePath) && File.Exists(book.EpubFilePath))
                try { File.Delete(book.EpubFilePath); } catch { }
            
            if (!string.IsNullOrEmpty(book.CoverImagePath) && File.Exists(book.CoverImagePath))
                try { File.Delete(book.CoverImagePath); } catch { }

            if (book.AudioTracks != null)
            {
                foreach (var track in book.AudioTracks)
                {
                    if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
                        try { File.Delete(track.FilePath); } catch { }
                }
            }

            string? bookDir = null;
            if (!string.IsNullOrEmpty(book.CoverImagePath)) bookDir = Path.GetDirectoryName(book.CoverImagePath);
            else if (!string.IsNullOrEmpty(book.EpubFilePath)) bookDir = Path.GetDirectoryName(book.EpubFilePath);
            else if (book.AudioTracks != null && book.AudioTracks.Any()) bookDir = Path.GetDirectoryName(book.AudioTracks.First().FilePath);
            
            if (!string.IsNullOrEmpty(bookDir)) dirsToDelete.Add(bookDir);
        }

        _dbContext.Books.RemoveRange(books);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await CleanupOrphansAsync(cancellationToken);

        foreach (var dir in dirsToDelete)
        {
            if (Directory.Exists(dir))
            {
                try 
                {
                    Directory.Delete(dir, true);
                    var parentDir = Directory.GetParent(dir)?.FullName;
                    if (parentDir != null && Directory.Exists(parentDir) && !Directory.EnumerateFileSystemEntries(parentDir).Any())
                    {
                        Directory.Delete(parentDir);
                        var grandParentDir = Directory.GetParent(parentDir)?.FullName;
                        if (grandParentDir != null && Directory.Exists(grandParentDir) && !Directory.EnumerateFileSystemEntries(grandParentDir).Any())
                            Directory.Delete(grandParentDir);
                    }
                } catch { }
            }
        }
    }

    private async Task CleanupOrphansAsync(CancellationToken cancellationToken)
    {
        var orphanedAuthors = await _dbContext.Authors.Where(a => !a.Books.Any()).ToListAsync(cancellationToken);
        if (orphanedAuthors.Any()) _dbContext.Authors.RemoveRange(orphanedAuthors);

        var orphanedCategories = await _dbContext.Categories.Where(c => !c.Books.Any()).ToListAsync(cancellationToken);
        if (orphanedCategories.Any()) _dbContext.Categories.RemoveRange(orphanedCategories);

        var orphanedSeries = await _dbContext.Series.Where(s => !s.Books.Any()).ToListAsync(cancellationToken);
        if (orphanedSeries.Any()) _dbContext.Series.RemoveRange(orphanedSeries);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var orphanedUniverses = await _dbContext.Universes.Where(u => !u.Series.Any()).ToListAsync(cancellationToken);
        if (orphanedUniverses.Any()) _dbContext.Universes.RemoveRange(orphanedUniverses);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
