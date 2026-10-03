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
                    var payload = new { EpubCfi = book.CurrentEpubCfi, Percentage = book.PercentageCompleted, AudioPosition = book.CurrentAudioPosition };
                    await _httpClient.PutAsJsonAsync($"api/Books/{book.Id}/position", payload);
                }
                catch { }
            }
        }

        private async Task SyncDownToLocalAsync()
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<PaginatedList<Book>>("api/Books?limit=100");
                if (response == null || response.Items == null) return;

                var serverBooks = response.Items.Where(b => b.IsAvailableOffline).ToList();
                var localBooks = await _localRepo.GetOfflineBooksAsync();

                foreach (var serverBook in serverBooks)
                {
                    var baseUrl = _httpClient.BaseAddress?.ToString().TrimEnd('/');
                    
                    var epubPath = await DownloadFileToDbAsync(serverBook.Id, "epub", serverBook.EpubUrl, baseUrl);
                    if (epubPath.StartsWith("blob:")) serverBook.LocalEpubPath = epubPath;

                    var coverPath = await DownloadFileToDbAsync(serverBook.Id, "cover", serverBook.CoverUrl, baseUrl);
                    if (coverPath.StartsWith("blob:")) serverBook.LocalCoverPath = coverPath;

                    var audioPath = await DownloadFileToDbAsync(serverBook.Id, "audio", serverBook.AudioUrl, baseUrl);
                    if (audioPath.StartsWith("blob:")) serverBook.LocalAudioPath = audioPath;

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
            if (serverPath.StartsWith("blob:")) return serverPath;
            
            var fullUrl = serverPath.StartsWith("http") ? serverPath : $"{baseUrl}/{serverPath}";
            return await _jsRuntime.InvokeAsync<string>("window.quimeraIndexedDb.downloadFileToDb", bookId, fileType, fullUrl);
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
