using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;

namespace QuimeraReader.API.BackgroundServices;

public class LibraryScanBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LibraryScanBackgroundService> _logger;
    private readonly LibraryScanState _scanState;

    public LibraryScanBackgroundService(IServiceProvider serviceProvider, ILogger<LibraryScanBackgroundService> logger, LibraryScanState scanState)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _scanState = scanState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LibraryScanBackgroundService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScanBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing LibraryScanBackgroundService.");
                _scanState.IsScanning = false;
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Check more frequently
        }
    }

    private async Task ProcessScanBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scannerService = scope.ServiceProvider.GetRequiredService<EpubScannerService>();
        var audioMatcher = scope.ServiceProvider.GetRequiredService<AudioMatchingService>();

        var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IncomingScanFolder", stoppingToken);
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            return;

        if (!Directory.Exists(setting.Value))
        {
            _logger.LogWarning($"El directorio de escaneo configurado no existe: {setting.Value}");
            return;
        }

        string[] extensions = { ".epub", ".mp3", ".m4b", ".m4a", ".wav" };
        var rawFiles = Directory.GetFiles(setting.Value, "*.*", SearchOption.AllDirectories).Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
        
        // Excluir archivos que ya han sido procesados (para el modo LeaveInPlace)
        var processedSources = await dbContext.Books
            .Where(b => b.SourceFilePath != null)
            .Select(b => b.SourceFilePath)
            .ToListAsync(stoppingToken);

        var allFiles = rawFiles.Where(f => !processedSources.Contains(f)).ToArray();

        if (allFiles.Length == 0)
        {
            return;
        }

        _scanState.IsScanning = true;
        _scanState.TotalFilesFound = allFiles.Length;
        _scanState.FilesProcessed = 0;

        try
        {
            var batchSizeSetting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "ScanBatchSize", stoppingToken);
            int batchSize = int.TryParse(batchSizeSetting?.Value, out int parsedSize) ? parsedSize : 100;

            var files = allFiles.Take(batchSize).ToList();

            foreach (var file in files)
            {
                if (stoppingToken.IsCancellationRequested) break;

                _scanState.CurrentFile = Path.GetFileName(file);
                try
                {
                    var book = await scannerService.ScanEpubAsync(file, "GoogleBooks");
                    if (book.Id == 0)
                    {
                        dbContext.Books.Add(book);
                    }
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation($"Libro importado y organizado: {book.Title}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error escaneando el archivo {file}");
                }
                finally
                {
                    _scanState.FilesProcessed++;
                }
            }
        }
        finally
        {
            // Siempre liberar el cerrojo de escaneo al terminar el bache
            _scanState.IsScanning = false;
        }
    }
}

