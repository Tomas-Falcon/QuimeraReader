import sys

path_worker = 'QuimeraReader.Clients/QuimeraReader.Mobile/Services/MauiOfflineSyncWorker.cs'
with open(path_worker, 'w', encoding='utf-8') as f:
    f.write('''using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Mobile.Services;

public class MauiOfflineSyncWorker : IOfflineSyncWorker
{
    private readonly HttpClient _httpClient;
    private readonly ILocalBookRepository _localRepo;
    private readonly ILogger<MauiOfflineSyncWorker> _logger;
    private readonly INetworkStateService _networkState;
    private bool _isSyncing;

    public MauiOfflineSyncWorker(HttpClient httpClient, ILocalBookRepository localRepo, ILogger<MauiOfflineSyncWorker> logger, INetworkStateService networkState)
    {
        _httpClient = httpClient;
        _localRepo = localRepo;
        _logger = logger;
        _networkState = networkState;
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
            
            foreach (var book in offlineBooks)
            {
                // Descargar EPUB
                if (!string.IsNullOrEmpty(book.EpubFilePath))
                {
                    book.EpubFilePath = await DownloadFileAsync(book.EpubFilePath, $"epub_{book.Id}.epub");
                }
                
                // Descargar Portada
                if (!string.IsNullOrEmpty(book.CoverImagePath))
                {
                    book.CoverImagePath = await DownloadFileAsync(book.CoverImagePath, $"cover_{book.Id}.jpg");
                }

                // Obtener detalles completos (SyncMap, AudioTracks, etc)
                var detailedBook = await _httpClient.GetFromJsonAsync<Book>($"api/Books/{book.Id}");
                if (detailedBook != null)
                {
                    book.SyncMap = detailedBook.SyncMap;
                    book.AudioTracks = detailedBook.AudioTracks;
                    book.Annotations = detailedBook.Annotations;

                    if (book.AudioTracks != null)
                    {
                        foreach (var track in book.AudioTracks)
                        {
                            if (!string.IsNullOrEmpty(track.FilePath))
                            {
                                track.FilePath = await DownloadFileAsync(track.FilePath, $"audio_{book.Id}_{track.TrackNumber}.mp3");
                            }
                        }
                    }
                }

                await _localRepo.SaveBookAsync(book);
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
        // Check if serverPath is already a local app path
        if (serverPath.StartsWith(FileSystem.AppDataDirectory)) return serverPath;

        var localPath = Path.Combine(FileSystem.AppDataDirectory, localFileName);
        if (File.Exists(localPath)) return localPath; // Already downloaded

        try
        {
            // Transform server path to URL if needed, assuming the server serves static files or api/Media
            // Our app uses api/Media/stream?filePath=... for epub/audio, and raw URL for cover.
            // But let's build the correct URL:
            string downloadUrl = serverPath;
            if (serverPath.Contains("api/Media")) {
                downloadUrl = serverPath;
            } else if (!serverPath.StartsWith("http")) {
                downloadUrl = $"api/Media/stream?filePath={Uri.EscapeDataString(serverPath)}";
            }

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
        return serverPath; // Fallback to original
    }
}
''')