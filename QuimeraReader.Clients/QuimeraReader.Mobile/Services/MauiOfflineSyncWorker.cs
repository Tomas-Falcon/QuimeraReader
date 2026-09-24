using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Models;
using QuimeraReader.Mobile.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Mobile.Services;

public class MauiOfflineSyncWorker : IOfflineSyncWorker
{
    private readonly ILogger<MauiOfflineSyncWorker> _logger;
    private readonly INetworkStateService _networkState;
    private readonly IServiceProvider _serviceProvider;
    private bool _isSyncing;

    public MauiOfflineSyncWorker(ILogger<MauiOfflineSyncWorker> logger, INetworkStateService networkState, IServiceProvider serviceProvider)
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
            _logger.LogInformation("Iniciando sincronización offline...");

            using var scope = _serviceProvider.CreateScope();
            var bookService = scope.ServiceProvider.GetRequiredService<IBookService>();
            var localRepo = scope.ServiceProvider.GetRequiredService<ILocalBookRepository>();
            var dbContext = scope.ServiceProvider.GetRequiredService<LocalAppDbContext>();
            
            await localRepo.EnsureCreatedAsync();

            // 1. PUSH: Local -> Server
            var localBooks = await localRepo.GetOfflineBooksAsync();
            var paginatedBooks = await bookService.GetBooksAsync(1, 1000);
            if (paginatedBooks == null || paginatedBooks.Data == null) return;
            
            var serverBooks = paginatedBooks.Data;

            foreach (var localBook in localBooks)
            {
                var serverBook = serverBooks.FirstOrDefault(b => b.Id == localBook.Id);
                if (serverBook != null)
                {
                    // Si el local fue leido despues que el server, hacer PUSH al server
                    if (localBook.LastReadAt > serverBook.LastReadAt || (localBook.LastReadAt != null && serverBook.LastReadAt == null))
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
                            
                            // Prevenir que luego se pise el local actualizando el serverBook en memoria
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

            // 2. PULL: Server -> Local
            var offlineBooks = serverBooks.Where(b => b.IsAvailableOffline).ToList();

            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

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
        if (serverPath.StartsWith(FileSystem.AppDataDirectory)) return serverPath;

        var localPath = Path.Combine(FileSystem.AppDataDirectory, localFileName);
        // FIXME: Esto deberia verificar si el archivo en el servidor fue modificado.
        // Por ahora, para no romper compatibilidad offline actual, checamos solo si existe.
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
