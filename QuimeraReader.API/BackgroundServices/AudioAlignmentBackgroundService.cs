using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace QuimeraReader.API.BackgroundServices;

public class AudioAlignmentBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private const double MinCoverage = 0.30;
    private readonly AudioAlignmentQueue _queue;
    private readonly ILogger<AudioAlignmentBackgroundService> _logger;

    public AudioAlignmentBackgroundService(IServiceProvider serviceProvider, AudioAlignmentQueue queue, ILogger<AudioAlignmentBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AudioAlignmentBackgroundService started. Charging pending jobs...");

        await LoadPendingJobsOnStartup(stoppingToken);

        _logger.LogInformation("Waiting for jobs in the Channel Queue...");

        await foreach (var bookId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessBookAsync(bookId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocurrió un error procesando el libro {BookId} en la cola de alineación.", bookId);
            }
        }
    }

    private async Task LoadPendingJobsOnStartup(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Recuperar tanto los pendientes como los que quedaron interrumpidos en "PROCESSING" por un reinicio previo
            var interruptedBooks = await dbContext.Books
                .Where(b => b.ProcessingStatus == "PROCESSING")
                .ToListAsync(stoppingToken);

            foreach (var b in interruptedBooks)
            {
                b.ProcessingStatus = "PENDING_SYNC";
            }
            if (interruptedBooks.Count > 0)
            {
                await dbContext.SaveChangesAsync(stoppingToken);
            }

            var pendingBooks = await dbContext.Books
                .Where(b => b.ProcessingStatus == "PENDING_SYNC")
                .Select(b => b.Id)
                .ToListAsync(stoppingToken);

            foreach (var id in pendingBooks)
            {
                await _queue.EnqueueAsync(id, stoppingToken);
            }
            _logger.LogInformation("Loaded {Count} pending jobs into the queue.", pendingBooks.Count);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error loading pending jobs on startup");
        }
    }

    private async Task ProcessBookAsync(int bookId, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var alignmentService = scope.ServiceProvider.GetRequiredService<AudioAlignmentService>();

        var bookToSync = await dbContext.Books
            .Include(b => b.SyncMap)
            .Include(b => b.AudioTracks)
            .FirstOrDefaultAsync(b => b.Id == bookId, stoppingToken);

        if (bookToSync == null)
        {
            _logger.LogWarning("El libro {Id} no fue encontrado en la base de datos.", bookId);
            return;
        }

        var orderedTracks = bookToSync.AudioTracks
            .Where(t => !string.IsNullOrEmpty(t.FilePath))
            .OrderBy(t => t.TrackNumber)
            .ToList();

        if (orderedTracks.Count == 0 || string.IsNullOrEmpty(bookToSync.EpubFilePath))
        {
            _logger.LogWarning("El libro {Id} no es válido para sincronización (falta audio o epub).", bookId);
            return;
        }

        if (bookToSync.ProcessingStatus == "SYNCED" || bookToSync.ProcessingStatus == "PROCESSING")
        {
            return;
        }

        _logger.LogInformation("Iniciando procesamiento Whisper multipista ({TrackCount} pistas) para el libro: {Title} (ID: {Id})",
            orderedTracks.Count, bookToSync.Title, bookToSync.Id);

        bookToSync.ProcessingStatus = "PROCESSING";
        await dbContext.SaveChangesAsync(stoppingToken);

        var (fullTextContent, spineChapters) = ExtractChaptersFromEpub(bookToSync.EpubFilePath);

        if (string.IsNullOrEmpty(fullTextContent))
        {
            _logger.LogWarning("No se pudo extraer texto del EPUB para el libro {Id}. Cancelando sync.", bookToSync.Id);
            bookToSync.ProcessingStatus = "ERROR";
            await dbContext.SaveChangesAsync(stoppingToken);
            return;
        }

        var trackTuples = orderedTracks.Select(t => (t.TrackNumber, t.FilePath)).ToList();
        var alignmentResult = await alignmentService.GenerateMultiTrackSyncMapAsync(trackTuples, fullTextContent, spineChapters, stoppingToken);

        if (!alignmentResult.Success)
        {
            _logger.LogError("Error en Whisper: {Error}", alignmentResult.ErrorMessage);
            bookToSync.ProcessingStatus = "ERROR";
            await dbContext.SaveChangesAsync(stoppingToken);
            return;
        }

        // Si solo tiene 1 pista y la cobertura es muy baja, devolver a huérfanos; si es multipista con cobertura muy baja, marcar como ERROR sin destruir pistas
        if (alignmentResult.Coverage < MinCoverage)
        {
            if (orderedTracks.Count == 1)
            {
                var singleTrack = orderedTracks[0];
                _logger.LogWarning(
                    "Audio '{Audio}' RECHAZADO para el libro '{Title}' (ID: {Id}): cobertura de trigramas {Coverage:P1} < {Min:P0}. Se devuelve a huérfanos.",
                    singleTrack.FileName, bookToSync.Title, bookToSync.Id, alignmentResult.Coverage, MinCoverage);

                long size = File.Exists(singleTrack.FilePath) ? new FileInfo(singleTrack.FilePath).Length : 0;
                dbContext.UnmatchedAudioTracks.Add(new QuimeraReader.Domain.Entities.UnmatchedAudioTrack
                {
                    OriginalFileName = string.IsNullOrEmpty(singleTrack.FileName) ? Path.GetFileName(singleTrack.FilePath) : singleTrack.FileName,
                    PhysicalPath = singleTrack.FilePath,
                    FileSizeBytes = size,
                    UploadedAt = DateTime.UtcNow
                });
                dbContext.BookAudioTracks.Remove(singleTrack);
                if (bookToSync.SyncMap != null) dbContext.Remove(bookToSync.SyncMap);
                bookToSync.ProcessingStatus = "NONE";
                await dbContext.SaveChangesAsync(stoppingToken);
                return;
            }
            else
            {
                _logger.LogWarning(
                    "Baja cobertura de trigramas ({Coverage:P1}) en audiolibro multipista '{Title}' (ID: {Id}), pero se conserva el SyncMap.",
                    alignmentResult.Coverage, bookToSync.Title, bookToSync.Id);
            }
        }

        _logger.LogInformation("Cobertura de trigramas audio/EPUB para '{Title}': {Coverage:P1}", bookToSync.Title, alignmentResult.Coverage);

        if (bookToSync.SyncMap == null)
        {
            bookToSync.SyncMap = new QuimeraReader.Domain.Entities.SyncMap { BookId = bookToSync.Id, SyncMapJson = alignmentResult.SyncMapJson! };
        }
        else
        {
            bookToSync.SyncMap.SyncMapJson = alignmentResult.SyncMapJson!;
        }

        bookToSync.ProcessingStatus = "SYNCED";

        await dbContext.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Finalizó correctamente el procesamiento de Whisper para el libro: {Title}", bookToSync.Title);
    }

    private (string FullText, List<EpubSpineChapter> Chapters) ExtractChaptersFromEpub(string epubPath)
    {
        var chapters = new List<EpubSpineChapter>();
        try
        {
            var book = VersOne.Epub.EpubReader.ReadBook(epubPath);
            var sb = new System.Text.StringBuilder();
            int spineIdx = 0;

            foreach (var textContentFile in book.ReadingOrder)
            {
                var rawHtml = textContentFile.Content ?? string.Empty;
                var plain = Regex.Replace(rawHtml, "<.*?>", " ");
                sb.AppendLine(plain);

                chapters.Add(new EpubSpineChapter
                {
                    SpineIndex = spineIdx,
                    ChapterHref = textContentFile.FilePath ?? textContentFile.Key ?? string.Empty,
                    PlainText = plain
                });
                spineIdx++;
            }
            return (sb.ToString(), chapters);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error extrayendo texto del EPUB");
            return (string.Empty, chapters);
        }
    }
}
