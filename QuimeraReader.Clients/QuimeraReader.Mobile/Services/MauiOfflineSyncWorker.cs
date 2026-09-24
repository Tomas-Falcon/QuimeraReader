using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Models;
using QuimeraReader.Mobile.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace QuimeraReader.Mobile.Services;

public class MauiOfflineSyncWorker : IOfflineSyncWorker
{
    private readonly HttpClient _httpClient;
    private readonly ILocalBookRepository _localRepo;
    private readonly ILogger<MauiOfflineSyncWorker> _logger;
    private readonly INetworkStateService _networkState;
    private readonly IServiceProvider _serviceProvider;
    private bool _isSyncing;

    public MauiOfflineSyncWorker(HttpClient httpClient, ILocalBookRepository localRepo, ILogger<MauiOfflineSyncWorker> logger, INetworkStateService networkState, IServiceProvider serviceProvider)
    {
        _httpClient = httpClient;
        _localRepo = localRepo;
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

            await _localRepo.EnsureCreatedAsync();

            var books = await _httpClient.GetFromJsonAsync<List<Book>>("api/Books");
            if (books == null) return;

            var offlineBooks = books.FindAll(b => b.IsAvailableOffline);
            
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LocalAppDbContext>();

            foreach (var book in offlineBooks)
            {
                // Descargar EPUB
                var localEpub = await DownloadFileAsync(book.EpubUrl, $"epub_{book.Id}.epub");
                if (localEpub != book.EpubUrl) book.LocalEpubPath = localEpub;
                
                // Descargar Portada
                var localCover = await DownloadFileAsync(book.CoverUrl, $"cover_{book.Id}.jpg");
                if (localCover != book.CoverUrl) book.LocalCoverPath = localCover;

                // Descargar Audio
                var localAudio = await DownloadFileAsync(book.AudioUrl, $"audio_{book.Id}.mp3");
                if (localAudio != book.AudioUrl) book.LocalAudioPath = localAudio;

                await _localRepo.SaveBookAsync(book);

                // Obtener SyncMap
                try {
                    var syncMapStr = await _httpClient.GetStringAsync($"api/Books/{book.Id}/syncmap");
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

    private async Task<string> DownloadFileAsync(string serverPath, string localFileName)
    {
        if (string.IsNullOrEmpty(serverPath)) return serverPath;
        if (serverPath.StartsWith(FileSystem.AppDataDirectory)) return serverPath;

        var localPath = Path.Combine(FileSystem.AppDataDirectory, localFileName);
        if (File.Exists(localPath)) return localPath; // Already downloaded

        try
        {
            string downloadUrl = serverPath;
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
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
