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

        var audioTrack = bookToSync.AudioTracks.OrderBy(t => t.TrackNumber).FirstOrDefault();
        var audioFilePath = audioTrack?.FilePath;

        if (bookToSync == null || string.IsNullOrEmpty(audioFilePath) || string.IsNullOrEmpty(bookToSync.EpubFilePath))
        {
            _logger.LogWarning("El libro {Id} no es válido para sincronización.", bookId);
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
