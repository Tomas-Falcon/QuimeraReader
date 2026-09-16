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

    public LibraryScanBackgroundService(IServiceProvider serviceProvider, ILogger<LibraryScanBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
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
            }

            // Esperar 1 minuto hasta el próximo chequeo
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ProcessScanBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scannerService = scope.ServiceProvider.GetRequiredService<EpubScannerService>();

        var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IncomingScanFolder", stoppingToken);
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            return; // No hay carpeta configurada para escanear

        if (!Directory.Exists(setting.Value))
        {
            _logger.LogWarning($"El directorio de escaneo configurado no existe: {setting.Value}");
            return;
        }

        var batchSizeSetting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "ScanBatchSize", stoppingToken);
        int batchSize = int.TryParse(batchSizeSetting?.Value, out int parsedSize) ? parsedSize : 100;

        var files = Directory.GetFiles(setting.Value, "*.epub", SearchOption.AllDirectories)
            .Take(batchSize) // Configurable, por defecto 100 archivos por minuto
            .ToList();

        if (!files.Any())
        {
            _logger.LogInformation("No hay más archivos EPUB para escanear en la carpeta temporal.");
            // Opcional: borrar el setting para no seguir buscando
            return;
        }

        foreach (var file in files)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var book = await scannerService.ScanEpubAsync(file, "GoogleBooks");
                dbContext.Books.Add(book);
                _logger.LogInformation($"Libro importado y organizado: {book.Title}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error escaneando el archivo {file}");
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }
}
