using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VersOne.Epub;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Services;

public class EpubScannerService
{
    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly HttpClient _httpClient;
    private readonly AppDbContext _dbContext;
    private readonly AudioAlignmentQueue _queue;

    public EpubScannerService(IEnumerable<IMetadataProvider> providers, HttpClient httpClient, AppDbContext dbContext, AudioAlignmentQueue queue)
    {
        _providers = providers;
        _httpClient = httpClient;
        _dbContext = dbContext;
        _queue = queue;
    }

    public async Task<Book> ScanEpubAsync(string sourceFilePath, string preferredProviderName, string? originalFileName = null)
    {
        EpubBook epubBook = await EpubReader.ReadBookAsync(sourceFilePath);
        string bookTitle = string.IsNullOrWhiteSpace(epubBook.Title) && !string.IsNullOrWhiteSpace(originalFileName) 
            ? Path.GetFileNameWithoutExtension(originalFileName) 
            : (epubBook.Title ?? "Sin Título");
            
        var book = await _dbContext.Books
            .Include(b => b.Authors)
            .Include(b => b.Categories)
            .FirstOrDefaultAsync(b => b.Title == bookTitle) 
            ?? new Book { Title = bookTitle };
            
        // Si el libro ya existe, limpiamos los autores para volver a procesarlos (o podríamos saltarlo)
        if (book.Id > 0)
        {
            book.Authors.Clear();
        }

        if (epubBook.AuthorList != null)
        {
            foreach (var authorName in epubBook.AuthorList)
            {
                var existingAuthor = await _dbContext.Authors.FirstOrDefaultAsync(a => a.Name == authorName);
                if (existingAuthor == null)
                {
                    existingAuthor = new Author { Name = authorName, FileAs = authorName };
                    _dbContext.Authors.Add(existingAuthor);
                }
                book.Authors.Add(new BookAuthor { Author = existingAuthor });
            }
        }

        var settingsDict = await _dbContext.SystemSettings
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        string? extractedIsbn = null;
        if (epubBook.Schema?.Package?.Metadata?.Identifiers != null)
        {
            var isbnIdentifier = epubBook.Schema.Package.Metadata.Identifiers
                .FirstOrDefault(id => id.Scheme != null && id.Scheme.Equals("ISBN", StringComparison.OrdinalIgnoreCase));
            
            if (isbnIdentifier != null)
            {
                extractedIsbn = isbnIdentifier.Identifier;
                book.Isbn = extractedIsbn;
            }
        }

        bool hasTitle = !string.IsNullOrWhiteSpace(book.Title);
        bool hasAuthor = book.Authors.Any();
        bool hasSynopsis = !string.IsNullOrWhiteSpace(book.Description);
        bool hasCover = epubBook.CoverImage != null;

        if (!hasTitle || !hasAuthor || !hasCover)
        {
            var fallbackName = originalFileName ?? Path.GetFileNameWithoutExtension(sourceFilePath);
            var query = hasTitle ? book.Title : Path.GetFileNameWithoutExtension(fallbackName);
            var orderedProviders = _providers.OrderByDescending(p => p.ProviderName == preferredProviderName);

            BookMetadata? metadata = null;
            foreach (var provider in orderedProviders)
            {
                metadata = await provider.GetMetadataAsync(query, isbn: extractedIsbn, settings: settingsDict);
                if (metadata != null) break;
            }

            if (metadata != null)
            {
                if (string.IsNullOrWhiteSpace(book.Title) && !string.IsNullOrWhiteSpace(metadata.Title))
                    book.Title = metadata.Title;

                if (string.IsNullOrWhiteSpace(book.Description) && !string.IsNullOrWhiteSpace(metadata.Synopsis))
                    book.Description = metadata.Synopsis;

                if (!book.AverageRating.HasValue && metadata.AverageRating.HasValue)
                    book.AverageRating = metadata.AverageRating.Value;

                if (!book.Authors.Any() && metadata.Authors != null && metadata.Authors.Any())
                {
                    foreach (var auth in metadata.Authors)
                    {
                        var existingAuthor = await _dbContext.Authors.FirstOrDefaultAsync(a => a.Name == auth);
                        if (existingAuthor == null)
                        {
                            existingAuthor = new Author { Name = auth, FileAs = auth };
                            _dbContext.Authors.Add(existingAuthor);
                        }
                        book.Authors.Add(new BookAuthor { Author = existingAuthor });
                    }
                }

                if (metadata.Categories != null && metadata.Categories.Any())
                {
                    foreach (var catName in metadata.Categories)
                    {
                        var category = _dbContext.ChangeTracker.Entries<Category>()
                            .Select(e => e.Entity)
                            .FirstOrDefault(c => c.Name == catName) 
                            ?? await _dbContext.Set<Category>().FirstOrDefaultAsync(c => c.Name == catName);
                        
                        if (category == null)
                        {
                            category = new Category { Name = catName, IsUserGenerated = false };
                            _dbContext.Add(category);
                        }
                        book.Categories.Add(new BookCategory { Category = category });
                    }
                }

                if (!string.IsNullOrWhiteSpace(metadata.SeriesName))
                {
                    var seriesName = metadata.SeriesName;
                    double? seriesVolume = null;
                    
                    var match = System.Text.RegularExpressions.Regex.Match(seriesName, @"(?:Vol\.|Book|#)\s*([\d\.]+)");
                    if (match.Success && double.TryParse(match.Groups[1].Value, out double parsedVol))
                    {
                        seriesVolume = parsedVol;
                    }
                    
                    var series = _dbContext.ChangeTracker.Entries<Series>()
                        .Select(e => e.Entity)
                        .FirstOrDefault(s => s.Name == seriesName)
                        ?? await _dbContext.Set<Series>().FirstOrDefaultAsync(s => s.Name == seriesName);
                        
                    if (series == null)
                    {
                        series = new Series { Name = seriesName };
                        _dbContext.Add(series);
                    }
                    book.Series = series;
                    book.SeriesVolume = seriesVolume;
                }
            }
        }

        // --- ORGANIZACION FÍSICA ---
        settingsDict.TryGetValue("LibraryRootPath", out var libraryRoot);
        if (string.IsNullOrWhiteSpace(libraryRoot)) libraryRoot = Path.Combine(Directory.GetCurrentDirectory(), "Library");

        string mainAuthor = book.Authors.FirstOrDefault()?.Author.Name ?? "Unknown Author";
        string sagaName = book.Series?.Name;
        string volumeStr = book.SeriesVolume.HasValue ? $"Vol {book.SeriesVolume} - " : "";

        string safeAuthor = GetSafeFilename(mainAuthor);
        
        // Si el título sigue estando vacío, usamos el originalFileName o un nombre por defecto
        if (string.IsNullOrWhiteSpace(book.Title))
        {
            book.Title = originalFileName != null ? Path.GetFileNameWithoutExtension(originalFileName) : "Unknown Book";
        }
        string safeTitle = GetSafeFilename(book.Title);
        
        string targetDir = Path.Combine(libraryRoot, safeAuthor);
        if (!string.IsNullOrWhiteSpace(sagaName))
        {
            targetDir = Path.Combine(targetDir, GetSafeFilename(sagaName));
        }
        
        string bookSubDir = Path.Combine(targetDir, $"{volumeStr}{safeTitle}");
        Directory.CreateDirectory(bookSubDir);

        // Mover o crear Symlink basado en IngestionMode
        string newEpubPath = Path.Combine(bookSubDir, $"{safeTitle}.epub");
        settingsDict.TryGetValue("IngestionMode", out var ingestionMode);
        
        if (string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(newEpubPath), StringComparison.OrdinalIgnoreCase))
        {
            book.EpubFilePath = newEpubPath;
        }
        else if (ingestionMode == "LeaveInPlace")
        {
            try
            {
                if (File.Exists(newEpubPath)) File.Delete(newEpubPath);
                File.CreateSymbolicLink(newEpubPath, sourceFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creando Symlink, cayendo de nuevo a Copy: {ex.Message}");
                File.Copy(sourceFilePath, newEpubPath, overwrite: true);
            }
            book.EpubFilePath = newEpubPath;
        }
        else
        {
            File.Copy(sourceFilePath, newEpubPath, overwrite: true);
            book.EpubFilePath = newEpubPath;
        }

        // Mover Audio (si existe)
        string sourceDir = Path.GetDirectoryName(sourceFilePath)!;
        string baseFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
        string[] possibleAudioExtensions = { ".mp3", ".m4b", ".m4a" };
        
        bool hasAudio = false;
        foreach (var ext in possibleAudioExtensions)
        {
            string possibleAudioPath = Path.Combine(sourceDir, baseFileName + ext);
            if (File.Exists(possibleAudioPath))
            {
                string newAudioPath = Path.Combine(bookSubDir, $"{safeTitle}{ext}");
                if (string.Equals(Path.GetFullPath(possibleAudioPath), Path.GetFullPath(newAudioPath), StringComparison.OrdinalIgnoreCase))
                {
                    hasAudio = true;
                }
                else if (ingestionMode == "LeaveInPlace")
                {
                    try
                    {
                        if (File.Exists(newAudioPath)) File.Delete(newAudioPath);
                        File.CreateSymbolicLink(newAudioPath, possibleAudioPath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error creando Symlink Audio: {ex.Message}");
                        File.Copy(possibleAudioPath, newAudioPath, overwrite: true);
                    }
                    hasAudio = true;
                }
                else
                {
                    File.Copy(possibleAudioPath, newAudioPath, overwrite: true);
                    try { File.Delete(possibleAudioPath); } catch { }
                }
                
                book.AudioFilePath = newAudioPath;
                hasAudio = true;
                break;
            }
        }

        // --- MANEJO DE COLA DE SINCRONIZACIÓN (Just-In-Time) ---
        if (hasAudio)
        {
            if (book.LastReadAt != null)
            {
                // El libro ya estaba activo, encolar automáticamente
                book.ProcessingStatus = "PENDING_SYNC";
                await _queue.EnqueueAsync(book.Id);
            }
            else
            {
                // El libro no está activo, esperar al Trigger 1 (Lectura)
                book.ProcessingStatus = "CREATED";
            }
        }
        else
        {
            book.ProcessingStatus = "NONE"; // No hay audio
        }

        // Guardar Carátula
        if (epubBook.CoverImage != null)
        {
            string coverPath = Path.Combine(bookSubDir, "cover.jpg");
            await File.WriteAllBytesAsync(coverPath, epubBook.CoverImage);
            book.CoverImagePath = coverPath;
        }
        
        // Evaluar completitud de metadatos
        hasTitle = !string.IsNullOrWhiteSpace(book.Title);
        hasAuthor = book.Authors.Any();
        hasSynopsis = !string.IsNullOrWhiteSpace(book.Description);
        hasCover = !string.IsNullOrWhiteSpace(book.CoverImagePath) || epubBook.CoverImage != null;
        
        settingsDict.TryGetValue("GoogleBooksApiKey", out var googleBooksKey);
        bool hasGoogleBooksKey = !string.IsNullOrWhiteSpace(googleBooksKey);
        bool needsRating = hasGoogleBooksKey;
        bool ratingSatisfied = !needsRating || book.AverageRating.HasValue;

        book.IsMetadataComplete = hasTitle && hasAuthor && hasSynopsis && hasCover && ratingSatisfied;

        return book;
    }

    public async Task EnrichMetadataAsync(Book book, string preferredProviderName)
    {
        var settingsDict = await _dbContext.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);

        var query = !string.IsNullOrWhiteSpace(book.Title) ? book.Title : Path.GetFileNameWithoutExtension(book.EpubFilePath);
        var orderedProviders = _providers.OrderByDescending(p => p.ProviderName == preferredProviderName);

        BookMetadata? metadata = null;
        foreach (var provider in orderedProviders)
        {
            metadata = await provider.GetMetadataAsync(query, isbn: book.Isbn, settings: settingsDict);
            if (metadata != null) break;
        }

        if (metadata != null)
        {
            if (string.IsNullOrWhiteSpace(book.Title) && !string.IsNullOrWhiteSpace(metadata.Title))
                book.Title = metadata.Title;

            if (string.IsNullOrWhiteSpace(book.Description) && !string.IsNullOrWhiteSpace(metadata.Synopsis))
                book.Description = metadata.Synopsis;

            if (!book.AverageRating.HasValue && metadata.AverageRating.HasValue)
                book.AverageRating = metadata.AverageRating.Value;

            if (!book.Authors.Any() && metadata.Authors != null && metadata.Authors.Any())
            {
                foreach (var auth in metadata.Authors)
                {
                    var existingAuthor = await _dbContext.Authors.FirstOrDefaultAsync(a => a.Name == auth);
                    if (existingAuthor == null)
                    {
                        existingAuthor = new Author { Name = auth, FileAs = auth };
                        _dbContext.Authors.Add(existingAuthor);
                    }
                    book.Authors.Add(new BookAuthor { Author = existingAuthor });
                }
            }

            if (metadata.Categories != null && metadata.Categories.Any())
            {
                foreach (var catName in metadata.Categories)
                {
                    if (!book.Categories.Any(c => c.Category.Name == catName))
                    {
                        var category = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Name == catName);
                        if (category == null)
                        {
                            category = new Category { Name = catName, IsUserGenerated = false };
                            _dbContext.Add(category);
                        }
                        book.Categories.Add(new BookCategory { Category = category });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(metadata.SeriesName) && book.Series == null)
            {
                var seriesName = metadata.SeriesName;
                double? seriesVolume = null;
                var match = System.Text.RegularExpressions.Regex.Match(seriesName, @"(?:Vol\.|Book|#)\s*([\d\.]+)");
                if (match.Success && double.TryParse(match.Groups[1].Value, out double parsedVol))
                {
                    seriesVolume = parsedVol;
                }
                
                var series = await _dbContext.Series.FirstOrDefaultAsync(s => s.Name == seriesName);
                if (series == null)
                {
                    series = new Series { Name = seriesName };
                    _dbContext.Add(series);
                }
                book.Series = series;
                book.SeriesVolume = seriesVolume;
            }

            if (string.IsNullOrEmpty(book.CoverImagePath) && !string.IsNullOrWhiteSpace(metadata.CoverImageUri))
            {
                try
                {
                    var imageBytes = await _httpClient.GetByteArrayAsync(metadata.CoverImageUri);
                    
                    settingsDict.TryGetValue("LibraryRootPath", out var libraryRoot);
                    if (string.IsNullOrWhiteSpace(libraryRoot)) libraryRoot = Path.Combine(Directory.GetCurrentDirectory(), "Library");
                    
                    string mainAuthor = book.Authors.FirstOrDefault()?.Author.Name ?? "Unknown Author";
                    string safeAuthor = GetSafeFilename(mainAuthor);
                    string safeTitle = GetSafeFilename(book.Title);
                    string bookSubDir = Path.Combine(libraryRoot, safeAuthor, safeTitle);
                    
                    Directory.CreateDirectory(bookSubDir);
                    string coverPath = Path.Combine(bookSubDir, "cover.jpg");
                    await File.WriteAllBytesAsync(coverPath, imageBytes);
                    book.CoverImagePath = coverPath;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error descargando carátula de {metadata.CoverImageUri}: {ex.Message}");
                }
            }
        }

        bool hasTitle = !string.IsNullOrWhiteSpace(book.Title);
        bool hasAuthor = book.Authors.Any();
        bool hasSynopsis = !string.IsNullOrWhiteSpace(book.Description);
        bool hasCover = !string.IsNullOrWhiteSpace(book.CoverImagePath);
        
        settingsDict.TryGetValue("GoogleBooksApiKey", out var googleBooksKey);
        bool hasGoogleBooksKey = !string.IsNullOrWhiteSpace(googleBooksKey);
        bool needsRating = hasGoogleBooksKey;
        bool ratingSatisfied = !needsRating || book.AverageRating.HasValue;

        book.IsMetadataComplete = hasTitle && hasAuthor && hasSynopsis && hasCover && ratingSatisfied;
    }

    private string GetSafeFilename(string filename)
    {
        return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
    }
}
