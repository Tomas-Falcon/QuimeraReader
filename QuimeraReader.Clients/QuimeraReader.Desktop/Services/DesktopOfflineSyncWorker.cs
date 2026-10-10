using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http.Json;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Models;
using QuimeraReader.Shared.Services;
using QuimeraReader.Desktop.Data;

namespace QuimeraReader.Desktop.Services;

public class DesktopOfflineSyncWorker : IOfflineSyncWorker
{
    private readonly ILogger<DesktopOfflineSyncWorker> _logger;
    private readonly INetworkStateService _networkState;
    private readonly IServiceProvider _serviceProvider;
    private bool _isSyncing;

    public DesktopOfflineSyncWorker(ILogger<DesktopOfflineSyncWorker> logger, INetworkStateService networkState, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _networkState = networkState;
        _serviceProvider = serviceProvider;
    }

    public bool IsSyncing => _isSyncing;

    public async Task SyncNowAsync()
    {
        if (_isSyncing || _networkState.IsOffline) return;

        try
        {
            _isSyncing = true;
            _logger.LogInformation("Iniciando sincronización offline en Desktop...");

            using var scope = _serviceProvider.CreateScope();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
            var bookService = scope.ServiceProvider.GetRequiredService<IBookService>();
            var localRepo = scope.ServiceProvider.GetRequiredService<ILocalBookRepository>();
            var dbContext = scope.ServiceProvider.GetRequiredService<DesktopLocalAppDbContext>();
            
            await localRepo.EnsureCreatedAsync();

            // 0. SYNC LOGS: Push local logs to Server
            try
            {
                var localLogs = await dbContext.ClientLogs.ToListAsync();
                if (localLogs.Any())
                {
                    httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
                    var logDtos = localLogs.Select(l => new QuimeraReader.Shared.Models.ClientLogDto 
                    { 
                        Level = l.Level, Message = l.Message, Exception = l.Exception, CreatedAt = l.CreatedAt 
                    }).ToList();
                    
                    var response = await httpClient.PostAsJsonAsync("api/Logs", logDtos);
                    if (response.IsSuccessStatusCode)
                    {
                        dbContext.ClientLogs.RemoveRange(localLogs);
                        await dbContext.SaveChangesAsync();
                        _logger.LogInformation("Sincronizados {Count} logs con el servidor.", localLogs.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error syncing logs: " + ex.Message);
            }

            // 1. PUSH: Local -> Server
            var localBooks = await localRepo.GetOfflineBooksAsync();
            QuimeraReader.Shared.Models.PaginatedResult<QuimeraReader.Shared.Models.Book>? paginatedBooks = null;
            try 
            {
                httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
                var response = await httpClient.GetAsync("api/Books?page=1&pageSize=10000&isAvailableOffline=true");
                if (!response.IsSuccessStatusCode) 
                {
                    _logger.LogWarning("API failed with status {Status}. Aborting sync to prevent deletion.", response.StatusCode);
                    return;
                }
                paginatedBooks = await response.Content.ReadFromJsonAsync<QuimeraReader.Shared.Models.PaginatedResult<QuimeraReader.Shared.Models.Book>>();
            } 
            catch (Exception ex) 
            {
                _logger.LogError(ex, "Network error during sync pull. Aborting sync.");
                return;
            }

            if (paginatedBooks == null || paginatedBooks.Data == null) return;
            var serverBooks = paginatedBooks.Data;

            foreach (var localBook in localBooks)
            {
                var serverBook = serverBooks.FirstOrDefault(b => b.Id == localBook.Id);
                if (serverBook == null)
                {
                    try {
                        serverBook = await bookService.GetBookAsync(localBook.Id);
                    } catch { }
                }

                if (serverBook != null)
                {
                    bool shouldPush = false;
                    
                    if (localBook.PercentageCompleted > (serverBook.PercentageCompleted ?? 0))
                    {
                        shouldPush = true;
                    }
                    else if (localBook.PercentageCompleted == serverBook.PercentageCompleted && 
                            (localBook.LastReadAt > serverBook.LastReadAt || (localBook.LastReadAt != null && serverBook.LastReadAt == null)))
                    {
                        shouldPush = true;
                    }

                    if (shouldPush)
                    {
                        try
                        {
                            await bookService.UpdatePositionAsync(
                                bookId: localBook.Id, 
                                epubCfi: localBook.CurrentEpubCfi, 
                                audioPosition: localBook.CurrentAudioPosition, 
                                percentage: localBook.PercentageCompleted,
                                audioTrackNumber: localBook.CurrentAudioTrackNumber
                            );
                            _logger.LogInformation("Push completado para libro {Id}", localBook.Id);
                            
                            serverBook.CurrentEpubCfi = localBook.CurrentEpubCfi;
                            serverBook.CurrentAudioPosition = localBook.CurrentAudioPosition;
                            serverBook.CurrentAudioTrackNumber = localBook.CurrentAudioTrackNumber;
                            serverBook.PercentageCompleted = localBook.PercentageCompleted;
                            serverBook.LastReadAt = localBook.LastReadAt;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error al hacer PUSH del progreso local del libro {Id}", localBook.Id);
                        }
                    }
                }
            }

            // 1.5 PUSH: Sync Annotations
            var queuedAnnotations = await localRepo.GetQueuedAnnotationsAsync();
            foreach (var ann in queuedAnnotations)
            {
                try
                {
                    var payload = new { CfiRange = ann.CfiRange, SelectedText = ann.SelectedText, ColorHex = ann.ColorHex, Note = ann.Note };
                    var response = await httpClient.PostAsJsonAsync($"api/Books/{ann.BookId}/annotations", payload);
                    if (response.IsSuccessStatusCode)
                    {
                        await localRepo.RemoveQueuedAnnotationAsync(ann.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to sync queued annotation {ann.Id}");
                }
            }

            // 2. DELETE: Local books no longer available offline
            var offlineBooks = serverBooks.Where(b => b.IsAvailableOffline).ToList();
            var offlineBooksIds = offlineBooks.Select(b => b.Id).ToHashSet();
            
            var booksToDelete = localBooks.Where(b => !offlineBooksIds.Contains(b.Id)).ToList();
            foreach (var b in booksToDelete)
            {
                try
                {
                    if (!string.IsNullOrEmpty(b.LocalEpubPath) && File.Exists(b.LocalEpubPath)) File.Delete(b.LocalEpubPath);
                    if (!string.IsNullOrEmpty(b.LocalCoverPath) && File.Exists(b.LocalCoverPath)) File.Delete(b.LocalCoverPath);
                    if (!string.IsNullOrEmpty(b.LocalAudioPath) && File.Exists(b.LocalAudioPath)) File.Delete(b.LocalAudioPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error eliminando archivos locales para el libro {Id}", b.Id);
                }
                await localRepo.DeleteBookAsync(b.Id);
                _logger.LogInformation("Eliminado localmente libro {Id} porque ya no está offline.", b.Id);
            }

            // 3. PULL: Server -> Local
            httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

            foreach (var book in offlineBooks)
            {
                var localEpub = await DownloadFileAsync(httpClient, book.EpubUrl, $"epub_{book.Id}.epub");
                if (localEpub != book.EpubUrl) book.LocalEpubPath = localEpub;
                
                var localCover = await DownloadFileAsync(httpClient, book.CoverUrl, $"cover_{book.Id}.jpg");
                if (localCover != book.CoverUrl) book.LocalCoverPath = localCover;

                var localAudio = await DownloadFileAsync(httpClient, book.AudioUrl, $"audio_{book.Id}.mp3");
                if (localAudio != book.AudioUrl) book.LocalAudioPath = localAudio;

                await localRepo.SaveBookAsync(book);

                try {
                    var syncMapStr = await bookService.GetSyncMapAsync(book.Id);
                    if (!string.IsNullOrEmpty(syncMapStr)) {
                        var existingMap = await dbContext.Books.Include(b => b.SyncMap).FirstOrDefaultAsync(b => b.Id == book.Id);
                        if (existingMap != null) {
                            if (existingMap.SyncMap == null) existingMap.SyncMap = new QuimeraReader.Domain.Entities.SyncMap { BookId = book.Id, SyncMapJson = syncMapStr };
                            else existingMap.SyncMap.SyncMapJson = syncMapStr;
                            await dbContext.SaveChangesAsync();
                        }
                    }
                } catch { }

                try {
                    var serverAnnotations = await bookService.GetAnnotationsAsync(book.Id);
                    if (serverAnnotations != null && serverAnnotations.Any()) {
                        await localRepo.SyncAnnotationsAsync(book.Id, serverAnnotations);
                    }
                } catch { }
            }
            
            _logger.LogInformation("Sincronización offline completada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la sincronización offline.");
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task<string> DownloadFileAsync(HttpClient httpClient, string serverPath, string localFileName)
    {
        if (string.IsNullOrEmpty(serverPath)) return serverPath;
        if (serverPath.StartsWith(DesktopStorage.AppDataDirectory)) return serverPath;

        var localPath = Path.Combine(DesktopStorage.AppDataDirectory, localFileName);
        if (File.Exists(localPath)) return localPath; 

        try
        {
            using var response = await httpClient.GetAsync(serverPath, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                using var fs = new FileStream(localPath, FileMode.Create);
                await response.Content.CopyToAsync(fs);
                return localPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error descargando archivo {Path}", serverPath);
        }
        return serverPath; 
    }
}
