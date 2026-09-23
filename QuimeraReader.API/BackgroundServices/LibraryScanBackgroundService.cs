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
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value) || !Directory.Exists(setting.Value))
            return;

        string[] epubExtensions = { ".epub" };
        string[] audioExtensions = { ".mp3", ".m4b", ".m4a", ".wav" };
        
        var rawFiles = Directory.GetFiles(setting.Value, "*.*", SearchOption.AllDirectories)
                                .Where(f => epubExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) || audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                .ToArray();
        
        // Excluir archivos que ya han sido procesados
        var processedSources = await dbContext.Books.Where(b => b.SourceFilePath != null).Select(b => b.SourceFilePath).ToListAsync(stoppingToken);
        var processedAudios = await dbContext.BookAudioTracks.Select(t => t.FilePath).ToListAsync(stoppingToken);

        var allFiles = rawFiles.Where(f => !processedSources.Contains(f) && !processedAudios.Contains(f)).ToArray();

        if (allFiles.Length == 0) return;

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
                    if (epubExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    {
                        var book = await scannerService.ScanEpubAsync(file, "GoogleBooks");
                        if (book.Id == 0)
                        {
                            dbContext.Books.Add(book);
                        }
                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation($"Libro importado y organizado: {book.Title}");
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
                                string bookDir = Path.GetDirectoryName(book.EpubFilePath ?? book.CoverImagePath) ?? setting.Value;
                                string ext = Path.GetExtension(file);
                                string newAudioPath = Path.Combine(bookDir, $"{safeTitle} track {nextTrack}{ext}");

                                if (file != newAudioPath)
                                {
                                    try 
                                    {
                                        var modeSetting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IngestionMode", stoppingToken);
                                        if (modeSetting?.Value == "LeaveInPlace") {
                                            File.Copy(file, newAudioPath, true);
                                        } else {
                                            File.Move(file, newAudioPath, true);
                                        }
                                        book.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = nextTrack });
                                    } 
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error al mover/renombrar el audio huérfano.");
                                        book.AudioTracks.Add(new BookAudioTrack { FilePath = file, TrackNumber = nextTrack });
                                    }
                                }
                                else
                                {
                                    book.AudioTracks.Add(new BookAudioTrack { FilePath = file, TrackNumber = nextTrack });
                                }
                                await dbContext.SaveChangesAsync(stoppingToken);
                                _logger.LogInformation($"Audio importado y asociado al libro {book.Title} como Track {nextTrack}");
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
        finally
        {
            _scanState.IsScanning = false;
        }
    }
}