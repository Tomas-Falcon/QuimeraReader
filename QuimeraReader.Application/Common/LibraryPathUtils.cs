using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Application.Common;

public static class LibraryPathUtils
{
    public static async Task<string> GetBookOutputDirAsync(Book book, IAppDbContext db)
    {
        if (!string.IsNullOrEmpty(book.CoverImagePath)) return Path.GetDirectoryName(book.CoverImagePath)!;
        if (book.AudioTracks != null && book.AudioTracks.Any()) return Path.GetDirectoryName(book.AudioTracks.First().FilePath)!;
        
        string safeAuthor = string.Join("_", (book.Authors?.FirstOrDefault()?.Author?.Name ?? "Unknown Author").Split(Path.GetInvalidFileNameChars()));
        string safeTitle = string.Join("_", book.Title.Split(Path.GetInvalidFileNameChars()));
        
        var settingsDict = await db.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        settingsDict.TryGetValue("LibraryRootPath", out var libraryRoot);
        if (string.IsNullOrWhiteSpace(libraryRoot)) libraryRoot = Path.Combine(Directory.GetCurrentDirectory(), "Library");
        
        return Path.Combine(libraryRoot, safeAuthor, safeTitle);
    }
}
