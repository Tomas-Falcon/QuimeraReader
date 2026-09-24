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
            }

            _dbContext.Books.RemoveRange(books);
            await _dbContext.SaveChangesAsync();

            // CleanupOrphansAsync ya se encarga de buscar y borrar cualquier autor o categoria sin libros 
            // de forma masiva sin hacer N+1
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