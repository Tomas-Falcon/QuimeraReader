using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>Guarda y consulta <see cref="BookMetadataCache"/>: los metadatos ya resueltos de cada EPUB por hash.</summary>
public static class MetadataCacheService
{
    private const string MissingMetadataCategory = "Falta Metadatos";

    /// <summary>Vuelca los metadatos actuales de todos los libros con hash (crea o actualiza). Devuelve cuántos se guardaron.</summary>
    public static async Task<int> SnapshotLibraryAsync(AppDbContext db, Action<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        int saved = 0, lastId = 0;
        while (true)
        {
            var batch = await db.Books.AsNoTracking()
                .Where(b => b.Id > lastId && b.ContentHash != null)
                .OrderBy(b => b.Id)
                .Take(1000)
                .Select(b => new
                {
                    b.Id, b.ContentHash, b.Title, b.Description, b.Isbn, b.AverageRating, b.TotalPages, b.CoverImagePath,
                    b.IsMetadataComplete, b.SeriesVolume, SeriesName = b.Series != null ? b.Series.Name : null,
                    Authors = b.Authors.OrderBy(a => a.AuthorId).Select(a => a.Author.Name).ToList(),
                    Categories = b.Categories.Select(c => c.Category.Name).ToList()
                })
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;

            var hashes = batch.Select(b => b.ContentHash!).ToList();
            var existing = await db.BookMetadataCache.Where(c => hashes.Contains(c.ContentHash)).ToDictionaryAsync(c => c.ContentHash, cancellationToken);

            foreach (var b in batch)
            {
                if (!existing.TryGetValue(b.ContentHash!, out var entry))
                {
                    entry = new BookMetadataCache { ContentHash = b.ContentHash! };
                    db.BookMetadataCache.Add(entry);
                    existing[b.ContentHash!] = entry;
                }
                entry.Title = b.Title;
                entry.Authors = b.Authors.Count > 0 ? string.Join("\n", b.Authors) : null;
                entry.Description = b.Description;
                entry.Isbn = b.Isbn;
                entry.AverageRating = b.AverageRating;
                entry.TotalPages = b.TotalPages;
                var categories = b.Categories.Where(c => c != MissingMetadataCategory).ToList();
                entry.Categories = categories.Count > 0 ? string.Join("\n", categories) : null;
                entry.SeriesName = b.SeriesName;
                entry.SeriesVolume = b.SeriesVolume;
                entry.CoverImagePath = b.CoverImagePath;
                entry.IsMetadataComplete = b.IsMetadataComplete;
                entry.CachedAt = DateTime.UtcNow;
                saved++;
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            onProgress?.Invoke(saved);
        }
        return saved;
    }

    public static Task<BookMetadataCache?> FindAsync(AppDbContext db, string contentHash, CancellationToken cancellationToken = default) =>
        db.BookMetadataCache.AsNoTracking().FirstOrDefaultAsync(c => c.ContentHash == contentHash, cancellationToken);

    public static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Bytes de la portada guardada en caché si el archivo sigue existiendo.</summary>
    public static async Task<byte[]?> ReadCoverAsync(BookMetadataCache cache, string? libraryRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(cache.CoverImagePath) || cache.CoverImagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        var path = LibraryPathResolver.Resolve(cache.CoverImagePath, libraryRoot);
        if (path == null) return null;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return CoverRepairService.IsImage(bytes) ? bytes : null;
    }
}
