using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Models;

namespace QuimeraReader.Web.Services
{
    public class WebOfflineSyncWorker : IOfflineSyncWorker, IDisposable
    {
        private readonly ILocalBookRepository _localRepo;
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebOfflineSyncWorker> _logger;
        private readonly IJSRuntime _jsRuntime;
        private readonly INetworkStateService _networkState;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private Timer? _timer;
        public bool IsSyncing { get; private set; } = false;

        public WebOfflineSyncWorker(
            ILocalBookRepository localRepo,
            HttpClient httpClient,
            ILogger<WebOfflineSyncWorker> logger,
            IJSRuntime jsRuntime,
            INetworkStateService networkState)
        {
            _localRepo = localRepo;
            _httpClient = httpClient;
            _logger = logger;
            _jsRuntime = jsRuntime;
            _networkState = networkState;
        }

        public void Start()
        {
            _timer = new Timer(async _ => await SyncNowAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        public async Task SyncNowAsync()
        {
            if (_networkState.IsOffline) return;
            if (!await _syncLock.WaitAsync(0)) return;
            IsSyncing = true;

            try
            {
                bool isOnline = await _jsRuntime.InvokeAsync<bool>("window.quimeraNetwork.isOnline");
                if (!isOnline || _networkState.IsOffline) return;

                await SyncUpToApiAsync();
                await SyncDownToLocalAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WebOfflineSyncWorker] Error durante la sincronización.");
            }
            finally
            {
                IsSyncing = false;
                _syncLock.Release();
            }
        }

        private async Task SyncUpToApiAsync()
        {
            var queue = await _localRepo.GetQueuedAnnotationsAsync();
            foreach (var item in queue)
            {
                try
                {
                    var payload = new { CfiRange = item.CfiRange, SelectedText = item.SelectedText, ColorHex = item.ColorHex, Note = item.Note };
                    var response = await _httpClient.PostAsJsonAsync($"api/Books/{item.BookId}/annotations", payload);
                    if (response.IsSuccessStatusCode)
                    {
                        await _localRepo.RemoveQueuedAnnotationAsync(item.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[WebOfflineSyncWorker] Error enviando nota {Id}", item.Id);
                }
            }
        }

        private async Task SyncDownToLocalAsync()
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<PaginatedResult<Book>>("api/Books?page=1&pageSize=10000&isAvailableOffline=true");
                if (response == null || response.Items == null) return;

                var serverBooks = response.Items.ToList();
                var localBooks = await _localRepo.GetOfflineBooksAsync();

                foreach (var serverBook in serverBooks)
                {
                    var localBook = localBooks.FirstOrDefault(b => b.Id == serverBook.Id);

                    // Comparar progreso por LastReadAt y PercentageCompleted antes de decidir dirección de sincronización
                    if (localBook != null)
                    {
                        bool localIsNewer = localBook.LastReadAt.HasValue &&
                                            (!serverBook.LastReadAt.HasValue || localBook.LastReadAt.Value > serverBook.LastReadAt.Value);
                        bool localIsFurther = (localBook.PercentageCompleted ?? 0) > (serverBook.PercentageCompleted ?? 0);

                        if (localIsNewer || localIsFurther)
                        {
                            try
                            {
                                var payload = new
                                {
                                    CurrentEpubCfi = localBook.CurrentEpubCfi,
                                    PercentageCompleted = localBook.PercentageCompleted,
                                    CurrentAudioPosition = localBook.CurrentAudioPosition,
                                    CurrentAudioTrackNumber = localBook.CurrentAudioTrackNumber
                                };
                                var posResp = await _httpClient.PostAsJsonAsync($"api/Books/{localBook.Id}/positions", payload);
                                if (posResp.IsSuccessStatusCode)
                                {
                                    serverBook.CurrentEpubCfi = localBook.CurrentEpubCfi;
                                    serverBook.PercentageCompleted = localBook.PercentageCompleted;
                                    serverBook.CurrentAudioPosition = localBook.CurrentAudioPosition;
                                    serverBook.CurrentAudioTrackNumber = localBook.CurrentAudioTrackNumber;
                                    serverBook.LastReadAt = localBook.LastReadAt;
                                }
                            }
                            catch { }
                        }

                        if (string.IsNullOrEmpty(serverBook.EpubLocationsCache) && !string.IsNullOrEmpty(localBook.EpubLocationsCache))
                        {
                            serverBook.EpubLocationsCache = localBook.EpubLocationsCache;
                        }
                    }

                    var baseUrl = _httpClient.BaseAddress?.ToString().TrimEnd('/');
                    
                    var epubPath = localBook?.LocalEpubPath;
                    if (serverBook.HasEpub && (string.IsNullOrEmpty(epubPath) || !epubPath.StartsWith("blob:")))
                    {
                        epubPath = await DownloadFileToDbAsync(serverBook.Id, "epub", serverBook.EpubUrl, baseUrl);
                    }
                    if (!string.IsNullOrEmpty(epubPath)) serverBook.LocalEpubPath = epubPath;

                    var coverPath = localBook?.LocalCoverPath;
                    if (serverBook.HasCover && (string.IsNullOrEmpty(coverPath) || (!coverPath.StartsWith("blob:") && !coverPath.StartsWith("data:"))))
                    {
                        coverPath = await DownloadFileToDbAsync(serverBook.Id, "cover", serverBook.CoverUrl, baseUrl);
                    }
                    if (!string.IsNullOrEmpty(coverPath)) serverBook.LocalCoverPath = coverPath;

                    var audioPath = localBook?.LocalAudioPath;
                    if (serverBook.HasAudio && (string.IsNullOrEmpty(audioPath) || !audioPath.StartsWith("blob:")))
                    {
                        audioPath = await DownloadFileToDbAsync(serverBook.Id, "audio", serverBook.AudioUrl, baseUrl);
                    }
                    if (!string.IsNullOrEmpty(audioPath)) serverBook.LocalAudioPath = audioPath;

                    var existingMap = localBook?.SyncMapJson;
                    if (!string.IsNullOrEmpty(existingMap))
                    {
                        serverBook.SyncMapJson = existingMap;
                    }
                    else if (serverBook.HasAudio && serverBook.IsAligned)
                    {
                        try
                        {
                            using var syncResp = await _httpClient.GetAsync($"api/Books/{serverBook.Id}/syncmap");
                            if (syncResp.IsSuccessStatusCode)
                            {
                                serverBook.SyncMapJson = await syncResp.Content.ReadAsStringAsync();
                            }
                        }
                        catch { }
                    }
                    await _localRepo.SaveBookAsync(serverBook);
                }

                // Delete local books that are no longer available offline
                var serverBookIds = serverBooks.Select(b => b.Id).ToHashSet();
                var toDelete = localBooks.Where(b => !serverBookIds.Contains(b.Id)).ToList();
                foreach (var book in toDelete)
                {
                    await _localRepo.DeleteBookAsync(book.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WebOfflineSyncWorker] Error descargando libros.");
            }
        }

        private async Task<string> DownloadFileToDbAsync(int bookId, string fileType, string? serverPath, string? baseUrl)
        {
            if (string.IsNullOrEmpty(serverPath)) return "";
            if (serverPath.StartsWith("blob:") || serverPath.StartsWith("data:")) return serverPath;
            
            var fullUrl = serverPath.StartsWith("http") ? serverPath : $"{baseUrl}/{serverPath}";

            if (fileType == "cover")
            {
                try
                {
                    var bytes = await _httpClient.GetByteArrayAsync(fullUrl);
                    var b64 = Convert.ToBase64String(bytes);
                    var ext = System.IO.Path.GetExtension(serverPath).TrimStart('.').ToLower();
                    if (ext == "jpg" || ext.Contains("cover")) ext = "jpeg";
                    if (string.IsNullOrEmpty(ext)) ext = "jpeg";
                    return $"data:image/{ext};base64,{b64}";
                }
                catch { return ""; }
            }

            return await _jsRuntime.InvokeAsync<string>("window.quimeraIndexedDb.downloadFileToDb", bookId, fileType, fullUrl);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _syncLock.Dispose();
        }
    }
}
