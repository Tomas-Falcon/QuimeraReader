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
        private Timer? _timer;
        public bool IsSyncing { get; private set; } = false;

        public WebOfflineSyncWorker(ILocalBookRepository localRepo, HttpClient httpClient, ILogger<WebOfflineSyncWorker> logger, IJSRuntime jsRuntime)
        {
            _localRepo = localRepo;
            _httpClient = httpClient;
            _logger = logger;
            _jsRuntime = jsRuntime;
        }

        public void Start()
        {
            _timer = new Timer(async _ => await SyncNowAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        public async Task SyncNowAsync()
        {
            if (IsSyncing) return;
            IsSyncing = true;

            try
            {
                bool isOnline = await _jsRuntime.InvokeAsync<bool>("window.quimeraNetwork.isOnline");
                if (!isOnline) return;

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
            }
        }

        private async Task SyncUpToApiAsync()
        {
            var queue = await _localRepo.GetQueuedAnnotationsAsync();
            if (!queue.Any()) return;

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
                    _logger.LogError(ex, $"[WebOfflineSyncWorker] Error enviando nota {item.Id}");
                }
            }

            // Sync progress
            var books = await _localRepo.GetOfflineBooksAsync();
            foreach (var book in books)
            {
                try
                {
                    var payload = new { CurrentEpubCfi = book.CurrentEpubCfi, PercentageCompleted = book.PercentageCompleted, CurrentAudioPosition = book.CurrentAudioPosition, CurrentAudioTrackNumber = book.CurrentAudioTrackNumber };
                    await _httpClient.PostAsJsonAsync($"api/Books/{book.Id}/positions", payload);
                }
                catch { }
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
                    var baseUrl = _httpClient.BaseAddress?.ToString().TrimEnd('/');
                    
                    var epubPath = localBook?.LocalEpubPath;
                    if (string.IsNullOrEmpty(epubPath) || !epubPath.StartsWith("blob:")) {
                        epubPath = await DownloadFileToDbAsync(serverBook.Id, "epub", serverBook.EpubUrl, baseUrl);
                    }
                    if (!string.IsNullOrEmpty(epubPath)) serverBook.LocalEpubPath = epubPath;

                    var coverPath = localBook?.LocalCoverPath;
                    if (string.IsNullOrEmpty(coverPath) || (!coverPath.StartsWith("blob:") && !coverPath.StartsWith("data:"))) {
                        coverPath = await DownloadFileToDbAsync(serverBook.Id, "cover", serverBook.CoverUrl, baseUrl);
                    }
                    if (!string.IsNullOrEmpty(coverPath)) serverBook.LocalCoverPath = coverPath;

                    var audioPath = localBook?.LocalAudioPath;
                    if (serverBook.HasAudio && (string.IsNullOrEmpty(audioPath) || !audioPath.StartsWith("blob:"))) {
                        audioPath = await DownloadFileToDbAsync(serverBook.Id, "audio", serverBook.AudioUrl, baseUrl);
                    }
                    if (!string.IsNullOrEmpty(audioPath)) serverBook.LocalAudioPath = audioPath;

                                        if (serverBook.HasAudio && serverBook.IsAligned && string.IsNullOrEmpty(serverBook.SyncMapJson))
                    {
                        try {
                            serverBook.SyncMapJson = await _httpClient.GetStringAsync($"api/Books/{serverBook.Id}/syncmap");
                        } catch { }
                    }
                    await _localRepo.SaveBookAsync(serverBook);
                }

                // Delete local books that are no longer available offline
                var serverBookIds = serverBooks.Select(b => b.Id).ToList();
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
                try {
                    var bytes = await _httpClient.GetByteArrayAsync(fullUrl);
                    var b64 = Convert.ToBase64String(bytes);
                    var ext = System.IO.Path.GetExtension(serverPath).TrimStart('.').ToLower();
                    if (ext == "jpg") ext = "jpeg";
                    if (string.IsNullOrEmpty(ext)) ext = "jpeg";
                    return $"data:image/{ext};base64,{b64}";
                } catch { return ""; }
            }

            return await _jsRuntime.InvokeAsync<string>("window.quimeraIndexedDb.downloadFileToDb", bookId, fileType, fullUrl);
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}



