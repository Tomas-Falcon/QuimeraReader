using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace QuimeraReader.API.BackgroundServices;

public class AudioAlignmentBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    // Umbral de cobertura de trigramas (transcripción completa vs EPUB) para aceptar el emparejamiento.
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
        catch(Exception e)
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

        var audioTrack = bookToSync.AudioTracks.OrderBy(t => t.TrackNumber).FirstOrDefault();
        var audioFilePath = audioTrack?.FilePath;

        if (string.IsNullOrEmpty(audioFilePath) || string.IsNullOrEmpty(bookToSync.EpubFilePath))
        {
            _logger.LogWarning("El libro {Id} no es válido para sincronización (falta audio o epub).", bookId);
            return;
        }

        if (bookToSync.ProcessingStatus == "SYNCED" || bookToSync.ProcessingStatus == "PROCESSING")
        {
            return; // Ya fue procesado o está en proceso por alguna otra razón
        }

        _logger.LogInformation("Iniciando procesamiento Whisper para el libro: {Title} (ID: {Id})", bookToSync.Title, bookToSync.Id);
                    
        // Marcar como procesando
        bookToSync.ProcessingStatus = "PROCESSING";
        await dbContext.SaveChangesAsync(stoppingToken);

        string textContent = ExtractTextFromEpub(bookToSync.EpubFilePath);

        if (string.IsNullOrEmpty(textContent))
        {
            _logger.LogWarning("No se pudo extraer texto del EPUB para el libro {Id}. Cancelando sync.", bookToSync.Id);
            bookToSync.ProcessingStatus = "ERROR";
            await dbContext.SaveChangesAsync(stoppingToken);
            return;
        }

        // Invocar Whisper (bloqueante, sincrónico en este hilo para cumplir la regla FIFO 1x1)
        var alignmentResult = await alignmentService.GenerateSyncMapAsync(audioFilePath, textContent);

        if (!alignmentResult.Success)
        {
            _logger.LogError("Error en Whisper: {Error}", alignmentResult.ErrorMessage);
            bookToSync.ProcessingStatus = "ERROR";
            await dbContext.SaveChangesAsync(stoppingToken);
            return;
        }

        // Validación definitiva: ¿la transcripción completa coincide con este libro?
        if (alignmentResult.Coverage < MinCoverage)
        {
            _logger.LogWarning(
                "Audio '{Audio}' RECHAZADO para el libro '{Title}' (ID: {Id}): cobertura de trigramas {Coverage:P1} < {Min:P0}. Se devuelve a huérfanos.",
                audioTrack!.FileName, bookToSync.Title, bookToSync.Id, alignmentResult.Coverage, MinCoverage);

            long size = File.Exists(audioFilePath) ? new FileInfo(audioFilePath).Length : 0;
            dbContext.UnmatchedAudioTracks.Add(new QuimeraReader.Domain.Entities.UnmatchedAudioTrack
            {
                OriginalFileName = string.IsNullOrEmpty(audioTrack.FileName) ? Path.GetFileName(audioFilePath) : audioTrack.FileName,
                PhysicalPath = audioFilePath,
                FileSizeBytes = size,
                UploadedAt = DateTime.UtcNow
            });
            dbContext.BookAudioTracks.Remove(audioTrack);
            if (bookToSync.SyncMap != null) dbContext.Remove(bookToSync.SyncMap);
            bookToSync.ProcessingStatus = "NONE";
            await dbContext.SaveChangesAsync(stoppingToken);
            return;
        }

        _logger.LogInformation("Cobertura de trigramas audio/EPUB para '{Title}': {Coverage:P1}", bookToSync.Title, alignmentResult.Coverage);

        // Guardar resultado
        if (bookToSync.SyncMap == null)
        {
            bookToSync.SyncMap = new QuimeraReader.Domain.Entities.SyncMap { BookId = bookToSync.Id, SyncMapJson = alignmentResult.SyncMapJson! };
        }
        else
        {
            bookToSync.SyncMap.SyncMapJson = alignmentResult.SyncMapJson!;
        }
        
        bookToSync.ProcessingStatus = "SYNCED"; // Mapearemos a ALIGNED en el DTO
        
        await dbContext.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Finalizó correctamente el procesamiento de Whisper para el libro: {Title}", bookToSync.Title);
    }

    private string ExtractTextFromEpub(string epubPath)
    {
        try
        {
            var book = VersOne.Epub.EpubReader.ReadBook(epubPath);
            var sb = new System.Text.StringBuilder();
            foreach (var textContentFile in book.ReadingOrder)
            {
                sb.AppendLine(textContentFile.Content); 
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "<.*?>", string.Empty);
        }
        catch(Exception e)
        {
            _logger.LogError(e, "Error extrayendo texto del EPUB");
            return string.Empty;
        }
    }
}

