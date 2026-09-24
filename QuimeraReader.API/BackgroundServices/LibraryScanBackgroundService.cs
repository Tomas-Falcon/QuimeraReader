using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using QuimeraReader.Domain.Entities;

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
                await PerformFullScanAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing LibraryScanBackgroundService.");
                _scanState.IsScanning = false;
            }

            // Descansar 5 minutos antes de volver a comprobar si hay archivos nuevos
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task PerformFullScanAsync(CancellationToken stoppingToken)
    {
        string scanFolder;
        int batchSize;
        HashSet<string> processedSources;
        HashSet<string> processedAudios;

        // Fase 1: Leer configuración y estado actual de la BD
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IncomingScanFolder", stoppingToken);
            if (setting == null || string.IsNullOrWhiteSpace(setting.Value) || !Directory.Exists(setting.Value))
                return;
            scanFolder = setting.Value;

            var batchSizeSetting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "ScanBatchSize", stoppingToken);
            batchSize = int.TryParse(batchSizeSetting?.Value, out int parsedSize) ? parsedSize : 100;

            var sourcesList = await dbContext.Books.Where(b => b.SourceFilePath != null).Select(b => b.SourceFilePath!).ToListAsync(stoppingToken);
            processedSources = new HashSet<string>(sourcesList);
            
            var audiosList = await dbContext.BookAudioTracks.Where(t => t.FilePath != null).Select(t => t.FilePath!).ToListAsync(stoppingToken);
            processedAudios = new HashSet<string>(audiosList);
        }

        // Fase 2: Escanear disco
        string[] epubExtensions = { ".epub" };
        string[] audioExtensions = { ".mp3", ".m4b", ".m4a", ".wav" };
        
        var rawFiles = Directory.GetFiles(scanFolder, "*.*", SearchOption.AllDirectories)
                                .Where(f => epubExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) || audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                .ToArray();

        var pendingFiles = rawFiles.Where(f => !processedSources.Contains(f) && !processedAudios.Contains(f)).ToArray();

        if (pendingFiles.Length == 0) return;

        _scanState.IsScanning = true;
        _scanState.TotalFilesFound = pendingFiles.Length;
        _scanState.FilesProcessed = 0;

        _logger.LogInformation($"Iniciando procesamiento de {pendingFiles.Length} archivos en lotes de {batchSize}.");

        try
        {
            // Procesar en lotes creando un Scope nuevo por lote para no saturar el DbContext
            for (int i = 0; i < pendingFiles.Length; i += batchSize)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var batchFiles = pendingFiles.Skip(i).Take(batchSize).ToArray();
                await ProcessBatchFilesAsync(batchFiles, scanFolder, epubExtensions, audioExtensions, stoppingToken);
            }
        }
        finally
        {
            _scanState.IsScanning = false;
            _logger.LogInformation("Escaneo de biblioteca finalizado.");
        }
    }

    private async Task ProcessBatchFilesAsync(string[] files, string scanFolder, string[] epubExtensions, string[] audioExtensions, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scannerService = scope.ServiceProvider.GetRequiredService<EpubScannerService>();
        var audioMatcher = scope.ServiceProvider.GetRequiredService<AudioMatchingService>();
        var mediaPackager = scope.ServiceProvider.GetRequiredService<MediaPackagerService>();

        var modeSetting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IngestionMode", stoppingToken);
        bool leaveInPlace = modeSetting?.Value == "LeaveInPlace";

        foreach (var file in files)
        {
            if (stoppingToken.IsCancellationRequested) break;
            _scanState.CurrentFile = Path.GetFileName(file);
            
            try
            {
                if (epubExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    var book = await scannerService.ScanEpubAsync(file, "GoogleBooks");
                    if (book.Id == 0)
                    {
                        dbContext.Books.Add(book);
                    }
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                else if (audioExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    var bookId = await audioMatcher.TryMatchAudioToBookAsync(file, stoppingToken);
                    if (bookId.HasValue)
                    {
                        var book = await dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == bookId.Value, stoppingToken);
                        if (book != null)
                        {
                            int nextTrack = book.AudioTracks.Any() ? book.AudioTracks.Max(t => t.TrackNumber) + 1 : 1;
                            string safeTitle = string.Join("_", book.Title.Split(Path.GetInvalidFileNameChars()));
                            string bookDir = Path.GetDirectoryName(book.EpubFilePath ?? book.CoverImagePath) ?? scanFolder;
                            string newAudioPath = Path.Combine(bookDir, $"{safeTitle} track {nextTrack}.mp3");

                            if (file != newAudioPath)
                            {
                                try 
                                {
                                    bool converted = await mediaPackager.NormalizeAudioAsync(file, newAudioPath);
                                    if (converted)
                                    {
                                        if (!leaveInPlace) {
                                            try { File.Delete(file); } catch { }
                                        }
                                        book.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = nextTrack });
                                    }
                                    else
                                    {
                                        File.Copy(file, newAudioPath, true);
                                        book.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = nextTrack });
                                    }
                                } 
                                catch (Exception)
                                {
                                    book.AudioTracks.Add(new BookAudioTrack { FilePath = file, TrackNumber = nextTrack });
                                }
                            }
                            else
                            {
                                book.AudioTracks.Add(new BookAudioTrack { FilePath = file, TrackNumber = nextTrack });
                            }
                            await dbContext.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
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
}