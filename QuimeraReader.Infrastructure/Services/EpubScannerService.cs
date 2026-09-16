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

    public async Task<Book> ScanEpubAsync(string sourceFilePath, string preferredProviderName)
    {
        EpubBook epubBook = await EpubReader.ReadBookAsync(sourceFilePath);
        var book = new Book { Title = epubBook.Title ?? "" };

        if (epubBook.AuthorList != null)
        {
            foreach (var authorName in epubBook.AuthorList)
                book.Authors.Add(new BookAuthor { Author = new Author { Name = authorName, FileAs = authorName } });
        }

        var settingsDict = await _dbContext.SystemSettings
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        if (string.IsNullOrWhiteSpace(book.Title) || !book.Authors.Any() || epubBook.CoverImage == null)
        {
            var query = !string.IsNullOrWhiteSpace(book.Title) ? book.Title : Path.GetFileNameWithoutExtension(sourceFilePath);
            var orderedProviders = _providers.OrderByDescending(p => p.ProviderName == preferredProviderName);

            BookMetadata? metadata = null;
            foreach (var provider in orderedProviders)
            {
                metadata = await provider.GetMetadataAsync(query, isbn: null, settings: settingsDict);
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
                        book.Authors.Add(new BookAuthor { Author = new Author { Name = auth, FileAs = auth } });
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
        string safeTitle = GetSafeFilename(book.Title);
        
        string targetDir = Path.Combine(libraryRoot, safeAuthor);
        if (!string.IsNullOrWhiteSpace(sagaName))
        {
            targetDir = Path.Combine(targetDir, GetSafeFilename(sagaName));
        }
        
        string bookSubDir = Path.Combine(targetDir, $"{volumeStr}{safeTitle}");
        Directory.CreateDirectory(bookSubDir);

        // Mover EPUB
        string newEpubPath = Path.Combine(bookSubDir, $"{safeTitle}.epub");
        File.Move(sourceFilePath, newEpubPath, overwrite: true);
        book.EpubFilePath = newEpubPath;

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
                File.Move(possibleAudioPath, newAudioPath, overwrite: true);
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
        bool hasTitle = !string.IsNullOrWhiteSpace(book.Title);
        bool hasAuthor = book.Authors.Any();
        bool hasSynopsis = !string.IsNullOrWhiteSpace(book.Description);
        bool hasCover = !string.IsNullOrWhiteSpace(book.CoverImagePath) || epubBook.CoverImage != null;
        
        bool hasGoogleBooksKey = settingsDict.ContainsKey("GoogleBooksApiKey") && !string.IsNullOrWhiteSpace(settingsDict["GoogleBooksApiKey"]);
        bool needsRating = hasGoogleBooksKey;
        bool ratingSatisfied = !needsRating || book.AverageRating.HasValue;

        book.IsMetadataComplete = hasTitle && hasAuthor && hasSynopsis && hasCover && ratingSatisfied;

        return book;
    }

    private string GetSafeFilename(string filename)
    {
        return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
    }
}
