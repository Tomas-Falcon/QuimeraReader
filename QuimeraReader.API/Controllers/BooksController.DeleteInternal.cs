using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;

namespace QuimeraReader.API.Controllers
{
    public partial class BooksController
    {
        private async Task DeleteBooksInternalAsync(List<Book> books)
        {
            var authorIdsToCheck = new HashSet<int>();
            var categoryIdsToCheck = new HashSet<int>();
            var dirsToDelete = new HashSet<string>();

            foreach (var book in books)
            {
                // Archivos fisicos
                if (!string.IsNullOrEmpty(book.SourceFilePath) && System.IO.File.Exists(book.SourceFilePath))
                    try { System.IO.File.Delete(book.SourceFilePath); } catch { }
                
                if (!string.IsNullOrEmpty(book.EpubFilePath) && System.IO.File.Exists(book.EpubFilePath))
                    try { System.IO.File.Delete(book.EpubFilePath); } catch { }
                
                if (!string.IsNullOrEmpty(book.CoverImagePath) && System.IO.File.Exists(book.CoverImagePath))
                    try { System.IO.File.Delete(book.CoverImagePath); } catch { }

                if (book.AudioTracks != null)
                {
                    foreach (var track in book.AudioTracks)
                    {
                        if (!string.IsNullOrEmpty(track.FilePath) && System.IO.File.Exists(track.FilePath))
                            try { System.IO.File.Delete(track.FilePath); } catch { }
                    }
                }

                // Guardar directorios
                string? bookDir = null;
                if (!string.IsNullOrEmpty(book.CoverImagePath)) bookDir = Path.GetDirectoryName(book.CoverImagePath);
                else if (!string.IsNullOrEmpty(book.EpubFilePath)) bookDir = Path.GetDirectoryName(book.EpubFilePath);
                else if (book.AudioTracks != null && book.AudioTracks.Any()) bookDir = Path.GetDirectoryName(book.AudioTracks.First().FilePath);
                
                if (!string.IsNullOrEmpty(bookDir)) dirsToDelete.Add(bookDir);

                // Collect authors/categories for orphan check
                var bookAuthors = await _dbContext.Books.Where(b => b.Id == book.Id).SelectMany(b => b.Authors.Select(a => a.AuthorId)).ToListAsync();
                foreach(var aid in bookAuthors) authorIdsToCheck.Add(aid);

                var bookCategories = await _dbContext.Books.Where(b => b.Id == book.Id).SelectMany(b => b.Categories.Select(c => c.CategoryId)).ToListAsync();
                foreach(var cid in bookCategories) categoryIdsToCheck.Add(cid);
            }

            _dbContext.Books.RemoveRange(books);
            await _dbContext.SaveChangesAsync();

            // Huérfanos
            foreach(var authorId in authorIdsToCheck)
            {
                bool authorHasMoreBooks = await _dbContext.Books.AnyAsync(b => b.Authors.Any(a => a.AuthorId == authorId));
                if (!authorHasMoreBooks)
                {
                    var author = await _dbContext.Authors.FindAsync(authorId);
                    if (author != null) _dbContext.Authors.Remove(author);
                }
            }

            foreach(var catId in categoryIdsToCheck)
            {
                bool catHasMoreBooks = await _dbContext.Books.AnyAsync(b => b.Categories.Any(c => c.CategoryId == catId));
                if (!catHasMoreBooks)
                {
                    var cat = await _dbContext.Categories.FindAsync(catId);
                    if (cat != null) _dbContext.Categories.Remove(cat);
                }
            }
            await _dbContext.SaveChangesAsync();
            await CleanupOrphansAsync();

            // Clean directories
            foreach(var dir in dirsToDelete)
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
    }
}