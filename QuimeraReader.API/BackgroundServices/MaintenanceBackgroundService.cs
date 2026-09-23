using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;

namespace QuimeraReader.API.BackgroundServices;

public class MaintenanceBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MaintenanceBackgroundService> _logger;

    public MaintenanceBackgroundService(IServiceProvider serviceProvider, ILogger<MaintenanceBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MaintenanceBackgroundService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = await CalculateDelayToNextRunAsync(stoppingToken);
            _logger.LogInformation("Próxima rutina de mantenimiento programada en {Hours}h {Minutes}m", delay.Hours, delay.Minutes);

            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await RunMaintenanceTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico durante la rutina de mantenimiento de medianoche.");
            }
        }
    }

    private async Task<TimeSpan> CalculateDelayToNextRunAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cronSetting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "MaintenanceCronTime", stoppingToken);
        string cronTimeStr = cronSetting?.Value ?? "00:00";

        if (!TimeSpan.TryParse(cronTimeStr, out TimeSpan targetTime))
        {
            targetTime = TimeSpan.Zero; // Fallback to 00:00 (Midnight)
        }

        DateTime now = DateTime.Now;
        DateTime nextRun = now.Date.Add(targetTime);

        if (now > nextRun)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun - now;
    }

    private async Task RunMaintenanceTasksAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== INICIANDO RUTINA DE MANTENIMIENTO ===");

        using var scope = _serviceProvider.CreateScope();
        var deepScanner = scope.ServiceProvider.GetRequiredService<DeepLibraryScannerService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audioQueue = scope.ServiceProvider.GetRequiredService<AudioAlignmentQueue>();

        // 1. Escaneo profundo de la librería para encontrar MP3/M4B huérfanos puestos manualmente
        _logger.LogInformation("1. Ejecutando Deep Scan...");
        await deepScanner.ScanLibraryForOrphanAudioAsync(stoppingToken);

        // 2. Armar la cola de procesamiento de Whisper priorizando el libro actual y su saga
        _logger.LogInformation("2. Organizando cola de procesamiento de Audio Whisper...");
        
        var pendingAudioBooks = await dbContext.Books
            .Where(b => b.AudioTracks.Any() && b.SyncMap == null && b.ProcessingStatus != "PROCESSING")
            .ToListAsync(stoppingToken);

        if (pendingAudioBooks.Any())
        {
            // Ordenamiento: 
            // - Primero los que se están leyendo, del más reciente al más antiguo.
            // - Luego los que tengan serie y no se hayan leído (posibles siguientes libros) ordenados por volumen.
            // - Por último el resto.
            var orderedBooks = pendingAudioBooks
                .OrderByDescending(b => b.LastReadAt.HasValue) // Los leídos primero
                .ThenByDescending(b => b.LastReadAt) // Los leídos más recientemente
                .ThenBy(b => b.SeriesId.HasValue ? 0 : 1) // Los que pertenecen a series primero
                .ThenBy(b => b.SeriesId) // Agrupados por serie
                .ThenBy(b => b.SeriesVolume) // Ordenados lógicamente por volumen
                .ToList();

            foreach (var book in orderedBooks)
            {
                book.ProcessingStatus = "PENDING_SYNC";
                await audioQueue.EnqueueAsync(book.Id, stoppingToken);
                _logger.LogInformation("Libro encolado para Whisper: {Title} (Prioridad de lectura: {LastReadAt})", 
                    book.Title, book.LastReadAt?.ToString("g") ?? "Nunca leído");
            }
            await dbContext.SaveChangesAsync(stoppingToken);
        }

        _logger.LogInformation("=== RUTINA DE MANTENIMIENTO FINALIZADA ===");
    }
}
