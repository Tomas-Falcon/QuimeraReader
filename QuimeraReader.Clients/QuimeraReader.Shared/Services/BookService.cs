using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using QuimeraReader.Shared.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using QuimeraReader.Shared.Models;

namespace QuimeraReader.Shared.Services;

public interface IBookService
{
    Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null, int? skip = null, int? take = null, bool? isAvailableOffline = null);
    Task<Book?> GetBookAsync(int id);
    Book? GetCachedBook(int id);
    List<Book>? GetCachedStatusBooks(string readingStatus);
    List<Book>? GetCachedRecommendations();
    IEnumerable<Category>? GetCachedCategories();
    Task<IEnumerable<Category>> GetCategoriesAsync(bool forceRefresh = false);
    Task<IEnumerable<Author>> GetAuthorsAsync(bool forceRefresh = false);
    Task<string> GetSyncMapAsync(int id);
    Task<Book> UploadEpubAsync(Stream fileStream, string fileName);
    Task UpdatePositionAsync(int bookId, string? epubCfi = null, double? audioPosition = null, double? percentage = null, int? audioTrackNumber = null);
    Task<List<Book>> GetRecommendationsAsync(bool forceRefresh = false);
    Task DeleteEpubAsync(int bookId);
    Task DeleteBooksAsync(int[] ids);
    Task DeleteAudioAsync(int bookId);
    Task DeleteCategoriesAsync(int[] ids);
    Task DeleteAuthorsAsync(int[] ids);
    Task BulkUpdateStatusAsync(List<int> bookIds, string status);
    Task UpdateMetadataAsync(int bookId, UpdateMetadataRequest request);
    Task ToggleOfflineAvailabilityAsync(int bookId, bool isAvailable);
    Task CreateAnnotationAsync(int bookId, string cfiRange, string selectedText, string colorHex, string note);
    Task UpdateAnnotationAsync(int bookId, int annotationId, string colorHex, string note);
    Task DeleteAnnotationAsync(int bookId, int annotationId);
    Task<List<AnnotationDto>> GetAnnotationsAsync(int bookId);
    Task UpdateCoverAsync(int bookId, string imageUrl);
    Task SaveEpubLocationsAsync(int bookId, string locationsJson);
    void InvalidateCache();
    string BaseAddress { get; }
}

public class BookService : IBookService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BookService> _logger;
    private readonly INetworkStateService _networkState;
    private readonly IServiceProvider _serviceProvider;

    private static readonly ConcurrentDictionary<int, Book> _bookCache = new();
    private static readonly ConcurrentDictionary<string, (PaginatedResult<Book> Result, DateTime Timestamp)> _queryCache = new();
    private static readonly ConcurrentDictionary<string, List<Book>> _statusRowCache = new();
    private static List<Category>? _categoriesCache;
    private static DateTime _categoriesCacheTime = DateTime.MinValue;
    private static List<Author>? _authorsCache;
    private static DateTime _authorsCacheTime = DateTime.MinValue;
    private static List<Book>? _recommendationsCache;
    private static DateTime _recommendationsCacheTime = DateTime.MinValue;

    public BookService(HttpClient httpClient, ILogger<BookService> logger, INetworkStateService networkState, IServiceProvider serviceProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        _networkState = networkState;
        _serviceProvider = serviceProvider;
    }

    private ILocalBookRepository? GetLocalRepo() => _serviceProvider.GetService<ILocalBookRepository>();

    public string BaseAddress => _httpClient.BaseAddress?.ToString() ?? "";

    public Book? GetCachedBook(int id) => _bookCache.TryGetValue(id, out var b) ? b : null;

    public List<Book>? GetCachedStatusBooks(string readingStatus) =>
        _statusRowCache.TryGetValue(readingStatus, out var list) ? list : null;

    public List<Book>? GetCachedRecommendations() => _recommendationsCache;

    public IEnumerable<Category>? GetCachedCategories() => _categoriesCache;

    public void InvalidateCache()
    {
        _queryCache.Clear();
        _statusRowCache.Clear();
        _categoriesCacheTime = DateTime.MinValue;
        _authorsCacheTime = DateTime.MinValue;
        _recommendationsCacheTime = DateTime.MinValue;
    }

    private static void CacheBookItem(Book b)
    {
        if (b == null || b.Id <= 0) return;
        if (_bookCache.TryGetValue(b.Id, out var existing))
        {
            if (string.IsNullOrEmpty(b.EpubLocationsCache) && !string.IsNullOrEmpty(existing.EpubLocationsCache))
                b.EpubLocationsCache = existing.EpubLocationsCache;
            if (string.IsNullOrEmpty(b.SyncMapJson) && !string.IsNullOrEmpty(existing.SyncMapJson))
                b.SyncMapJson = existing.SyncMapJson;
            if ((b.AudioTracks == null || !b.AudioTracks.Any()) && existing.AudioTracks != null && existing.AudioTracks.Any())
                b.AudioTracks = existing.AudioTracks;
        }
        _bookCache[b.Id] = b;
    }

    public async Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null, int? skip = null, int? take = null, bool? isAvailableOffline = null)
    {
        var network = _serviceProvider.GetService(typeof(QuimeraReader.Shared.Interfaces.INetworkStateService)) as QuimeraReader.Shared.Interfaces.INetworkStateService;
        var localRepo = _serviceProvider.GetService(typeof(QuimeraReader.Shared.Interfaces.ILocalBookRepository)) as QuimeraReader.Shared.Interfaces.ILocalBookRepository;

        if (network != null && network.IsOffline && localRepo != null)
        {
            return await GetOfflineFilteredBooksAsync(localRepo, page, pageSize, search, readingStatus, skip, take);
        }

        string catsKey = categoryIds != null && categoryIds.Length > 0 ? string.Join(",", categoryIds) : "";
        string cacheKey = $"{page}|{pageSize}|{search ?? ""}|{catsKey}|{readingStatus ?? ""}|{skip}|{take}|{isAvailableOffline}";

        if (_queryCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.Timestamp) < TimeSpan.FromSeconds(45))
        {
            return cached.Result;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var url = $"api/Books?page={page}&pageSize={pageSize}";
            if (skip.HasValue) url += $"&skip={skip.Value}";
            if (take.HasValue) url += $"&take={take.Value}";
            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"&search={Uri.EscapeDataString(search)}";
            }
            if (categoryIds != null && categoryIds.Any())
            {
                foreach (var cid in categoryIds) url += $"&categoryIds={cid}";
            }
            if (!string.IsNullOrWhiteSpace(readingStatus)) url += $"&readingStatus={readingStatus}";
            if (isAvailableOffline.HasValue) url += $"&isAvailableOffline={isAvailableOffline.Value.ToString().ToLowerInvariant()}";

            var response = await _httpClient.GetFromJsonAsync<PaginatedResult<Book>>(url);
            sw.Stop();
            if (sw.ElapsedMilliseconds > 1500)
            {
                _logger.LogWarning("[BookService] Petición lenta ({ElapsedMs}ms): GET {Url}", sw.ElapsedMilliseconds, url);
            }

            var result = response ?? new PaginatedResult<Book> { Page = page, PageSize = pageSize, Total = 0, Data = [] };
            if (result.Data != null)
            {
                foreach (var b in result.Data)
                {
                    CacheBookItem(b);
                }
                if (!string.IsNullOrWhiteSpace(readingStatus) && page == 1 && string.IsNullOrWhiteSpace(search))
                {
                    _statusRowCache[readingStatus] = result.Data.ToList();
                }
            }
            _queryCache[cacheKey] = (result, DateTime.UtcNow);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[BookService] Error tras {ElapsedMs}ms en GET api/Books (Status={Status}, Skip={Skip}). Usando caché/local.", sw.ElapsedMilliseconds, readingStatus, skip);

            if (_queryCache.TryGetValue(cacheKey, out var staleCache))
            {
                return staleCache.Result;
            }

            if (localRepo != null)
            {
                try 
                {
                    return await GetOfflineFilteredBooksAsync(localRepo, page, pageSize, search, readingStatus, skip, take);
                }
                catch { }
            }
            return new PaginatedResult<Book> { Page = page, PageSize = pageSize, Total = 0, Data = [] };
        }
    }

    private async Task<PaginatedResult<Book>> GetOfflineFilteredBooksAsync(
        QuimeraReader.Shared.Interfaces.ILocalBookRepository localRepo,
        int page, int pageSize, string? search, string? readingStatus, int? skip, int? take)
    {
        var localBooks = await localRepo.GetOfflineBooksAsync();
        foreach (var b in localBooks) CacheBookItem(b);
        var filtered = localBooks.AsEnumerable();
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(b => 
                (b.Title != null && b.Title.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (b.Authors != null && b.Authors.Any(a => a.Contains(search, StringComparison.OrdinalIgnoreCase))));
        }
        
        if (!string.IsNullOrWhiteSpace(readingStatus))
        {
            filtered = filtered.Where(b => string.Equals(b.ReadingStatus, readingStatus, StringComparison.OrdinalIgnoreCase));
        }

        var totalCount = filtered.Count();
        var data = filtered.Skip(skip ?? ((page - 1) * pageSize)).Take(take ?? pageSize).ToArray();
        return new PaginatedResult<Book> { Page = page, PageSize = pageSize, Total = totalCount, Data = data };
    }

    public async Task<Book?> GetBookAsync(int id)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (_networkState.IsOffline)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null)
                {
                    var localBook = await localRepo.GetBookByIdAsync(id);
                    if (localBook != null) CacheBookItem(localBook);
                    return localBook ?? GetCachedBook(id);
                }
                return GetCachedBook(id);
            }

            var book = await _httpClient.GetFromJsonAsync<Book>($"api/Books/{id}");
            sw.Stop();
            if (sw.ElapsedMilliseconds > 1500)
            {
                _logger.LogWarning("[BookService] Petición lenta ({ElapsedMs}ms): GET api/Books/{BookId}", sw.ElapsedMilliseconds, id);
            }
            if (book != null)
            {
                CacheBookItem(book);
            }
            return book;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[BookService] Error tras {ElapsedMs}ms obteniendo libro ID: {BookId}. Usando versión en caché/local.", sw.ElapsedMilliseconds, id);
            var localRepo = GetLocalRepo();
            if (localRepo != null)
            {
                try
                {
                    var localBook = await localRepo.GetBookByIdAsync(id);
                    if (localBook != null)
                    {
                        CacheBookItem(localBook);
                        return localBook;
                    }
                }
                catch { }
            }
            return GetCachedBook(id);
        }
    }

    public async Task<IEnumerable<Category>> GetCategoriesAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _categoriesCache != null && (DateTime.UtcNow - _categoriesCacheTime) < TimeSpan.FromMinutes(3))
        {
            return _categoriesCache;
        }

        try
        {
            if (!_networkState.IsOffline)
            {
                var list = (await _httpClient.GetFromJsonAsync<IEnumerable<Category>>("api/Books/categories"))?.ToList() ?? new List<Category>();
                if (list.Count > 0)
                {
                    _categoriesCache = list;
                    _categoriesCacheTime = DateTime.UtcNow;
                    return list;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error obteniendo categorías del servidor. Usando caché/local.");
        }

        if (_categoriesCache != null && _categoriesCache.Count > 0)
        {
            return _categoriesCache;
        }

        // Fallback desde libros en caché o SQLite local
        var localRepo = GetLocalRepo();
        if (localRepo != null)
        {
            try
            {
                var offlineBooks = await localRepo.GetOfflineBooksAsync();
                var localCats = offlineBooks
                    .SelectMany(b => b.Categories ?? Enumerable.Empty<string>(), (b, c) => new { Book = b, Cat = c })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Cat))
                    .GroupBy(x => x.Cat)
                    .Select((g, idx) => new Category
                    {
                        Id = idx + 1,
                        Name = g.Key,
                        BookCount = g.Count(),
                        SampleCoverUrl = g.Select(x => x.Book.CoverUrl).FirstOrDefault()
                    })
                    .OrderBy(c => c.Name)
                    .ToList();
                if (localCats.Count > 0) return localCats;
            }
            catch { }
        }

        return _categoriesCache ?? Enumerable.Empty<Category>();
    }

    public async Task<IEnumerable<Author>> GetAuthorsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _authorsCache != null && (DateTime.UtcNow - _authorsCacheTime) < TimeSpan.FromMinutes(3))
        {
            return _authorsCache;
        }

        try
        {
            if (!_networkState.IsOffline)
            {
                var list = (await _httpClient.GetFromJsonAsync<IEnumerable<Author>>("api/Books/authors"))?.ToList() ?? new List<Author>();
                if (list.Count > 0)
                {
                    _authorsCache = list;
                    _authorsCacheTime = DateTime.UtcNow;
                    return list;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error obteniendo autores.");
        }
        return _authorsCache ?? Enumerable.Empty<Author>();
    }

    public async Task<string> GetSyncMapAsync(int id)
    {
        try 
        {
            if (_networkState.IsOffline)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null)
                {
                    var localBook = await localRepo.GetBookByIdAsync(id);
                    if (!string.IsNullOrEmpty(localBook?.SyncMapJson))
                    {
                        return localBook.SyncMapJson;
                    }
                }
                return string.Empty;
            }
            _logger.LogInformation("[BookService] Descargando SyncMap para libro ID: {BookId}", id);
            return await _httpClient.GetStringAsync($"api/Books/{id}/syncmap");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BookService] SyncMap no encontrado o error para libro ID: {BookId}", id);
            var localRepo = GetLocalRepo();
            if (localRepo != null)
            {
                try
                {
                    var localBook = await localRepo.GetBookByIdAsync(id);
                    if (!string.IsNullOrEmpty(localBook?.SyncMapJson))
                    {
                        return localBook.SyncMapJson;
                    }
                }
                catch { }
            }
            return string.Empty;
        }
    }

    public async Task<Book> UploadEpubAsync(Stream fileStream, string fileName)
    {
        try
        {
            _logger.LogInformation("[BookService] Subiendo nuevo libro EPUB: {FileName}", fileName);
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName);

            var response = await _httpClient.PostAsync("api/Books/upload", content);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<Book>() ?? throw new InvalidOperationException("Respuesta inválida del servidor");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error subiendo libro: {FileName}", fileName);
            throw;
        }
    }

    public async Task UpdatePositionAsync(int bookId, string? epubCfi = null, double? audioPosition = null, double? percentage = null, int? audioTrackNumber = null)
    {
        if (_bookCache.TryGetValue(bookId, out var cached))
        {
            if (!string.IsNullOrEmpty(epubCfi)) cached.CurrentEpubCfi = epubCfi;
            if (audioPosition.HasValue) cached.CurrentAudioPosition = audioPosition.Value;
            if (percentage.HasValue) cached.PercentageCompleted = percentage.Value;
            if (audioTrackNumber.HasValue) cached.CurrentAudioTrackNumber = audioTrackNumber.Value;
            cached.LastReadAt = DateTime.UtcNow;
        }
        _queryCache.Clear();
        _statusRowCache.Clear();

        try
        {
            var localRepo = GetLocalRepo();
            if (localRepo != null)
            {
                try
                {
                    await localRepo.UpdateProgressAsync(bookId, epubCfi ?? "", audioPosition, percentage, audioTrackNumber);
                }
                catch { }
            }

            if (_networkState.IsOffline)
            {
                return;
            }
            var request = new UpdatePositionRequest { CurrentEpubCfi = epubCfi, CurrentAudioPosition = audioPosition, PercentageCompleted = percentage, CurrentAudioTrackNumber = audioTrackNumber };
            var response = await _httpClient.PostAsJsonAsync($"api/Books/{bookId}/positions", request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        { 
            _logger.LogError(ex, "[BookService] Error actualizando posición para libro ID: {BookId}. Guardado localmente.", bookId);
        }
    }

    public async Task<List<Book>> GetRecommendationsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _recommendationsCache != null && (DateTime.UtcNow - _recommendationsCacheTime) < TimeSpan.FromMinutes(3))
        {
            return _recommendationsCache;
        }

        try
        {
            if (!_networkState.IsOffline)
            {
                var results = await _httpClient.GetFromJsonAsync<List<Book>>("api/Books/recommendations") ?? new List<Book>();
                if (results.Count > 0)
                {
                    foreach (var b in results) CacheBookItem(b);
                    _recommendationsCache = results;
                    _recommendationsCacheTime = DateTime.UtcNow;
                }
                return results;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error obteniendo recomendaciones.");
        }
        return _recommendationsCache ?? new List<Book>();
    }

    public async Task DeleteBooksAsync(int[] ids)
    {
        var queryString = string.Join("&", ids.Select(id => $"ids={id}"));
        var response = await _httpClient.DeleteAsync($"api/Books?{queryString}");
        response.EnsureSuccessStatusCode();
        foreach (var id in ids) _bookCache.TryRemove(id, out _);
        InvalidateCache();
    }

    public async Task DeleteEpubAsync(int bookId)
    {
        try
        {
            _logger.LogInformation("[BookService] Solicitando eliminar EPUB del libro ID: {BookId}", bookId);
            var response = await _httpClient.DeleteAsync($"api/Books/{bookId}/epub");
            response.EnsureSuccessStatusCode();
            InvalidateCache();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error eliminando EPUB del libro ID: {BookId}", bookId);
            throw;
        }
    }

    public async Task DeleteCategoriesAsync(int[] ids)
    {
        var queryString = string.Join("&", ids.Select(id => $"ids={id}"));
        await _httpClient.DeleteAsync($"api/Books/categories?{queryString}");
        InvalidateCache();
    }

    public async Task DeleteAuthorsAsync(int[] ids)
    {
        var queryString = string.Join("&", ids.Select(id => $"ids={id}"));
        await _httpClient.DeleteAsync($"api/Books/authors?{queryString}");
        InvalidateCache();
    }

    public async Task DeleteAudioAsync(int bookId)
    {
        try
        {
            _logger.LogInformation("[BookService] Solicitando eliminar Audio del libro ID: {BookId}", bookId);
            var response = await _httpClient.DeleteAsync($"api/Books/{bookId}/audio");
            response.EnsureSuccessStatusCode();
            InvalidateCache();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error eliminando Audio del libro ID: {BookId}", bookId);
            throw;
        }
    }

    public async Task BulkUpdateStatusAsync(List<int> bookIds, string status)
    {
        var request = new BulkStatusUpdateRequest { BookIds = bookIds, Status = status };
        var response = await _httpClient.PutAsJsonAsync("api/Books/bulk/status", request);
        response.EnsureSuccessStatusCode();
        InvalidateCache();
    }

    public async Task ToggleOfflineAvailabilityAsync(int bookId, bool isAvailable)
    {
        var response = await _httpClient.PutAsync($"api/Books/{bookId}/offline?isAvailable={isAvailable}", null);
        response.EnsureSuccessStatusCode();
        if (_bookCache.TryGetValue(bookId, out var cached)) cached.IsAvailableOffline = isAvailable;
        InvalidateCache();
    }

    public async Task CreateAnnotationAsync(int bookId, string cfiRange, string selectedText, string colorHex, string note)
    {
        try
        {
            if (_networkState.IsOffline)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null) 
                {
                    await localRepo.QueueAnnotationAsync(bookId, cfiRange, selectedText, colorHex, note);
                    return;
                }
            }

            var payload = new { CfiRange = cfiRange, SelectedText = selectedText, ColorHex = colorHex, Note = note };
            var response = await _httpClient.PostAsJsonAsync($"api/Books/{bookId}/annotations", payload);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BookService] Error creating annotation online. Queueing locally.");
            var localRepo = GetLocalRepo();
            if (localRepo != null) 
            {
                await localRepo.QueueAnnotationAsync(bookId, cfiRange, selectedText, colorHex, note);
            }
        }
    }

    public async Task<List<AnnotationDto>> GetAnnotationsAsync(int bookId)
    {
        try
        {
            if (_networkState.IsOffline)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null) 
                {
                    return await localRepo.GetAnnotationsAsync(bookId);
                }
            }
            _logger.LogInformation("[BookService] Obteniendo anotaciones del libro ID: {BookId}", bookId);
            var list = await _httpClient.GetFromJsonAsync<List<AnnotationDto>>($"api/Books/{bookId}/annotations") ?? new();
            var repo = GetLocalRepo();
            if (repo != null)
            {
                try { await repo.SyncAnnotationsAsync(bookId, list); } catch { }
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BookService] Error obteniendo anotaciones del libro ID: {BookId}. Intentando recuperar versión local.", bookId);
            var localRepo = GetLocalRepo();
            if (localRepo != null) 
            {
                try { return await localRepo.GetAnnotationsAsync(bookId); } catch { }
            }
            return new();
        }
    }

    public async Task UpdateAnnotationAsync(int bookId, int annotationId, string colorHex, string note)
    {
        try
        {
            if (_networkState.IsOffline || annotationId <= 0)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null)
                {
                    var anns = await localRepo.GetAnnotationsAsync(bookId);
                    var target = anns.FirstOrDefault(a => a.Id == annotationId);
                    if (target != null)
                    {
                        target.ColorHex = colorHex;
                        target.Note = note;
                        await localRepo.SyncAnnotationsAsync(bookId, anns);
                        await localRepo.QueueAnnotationAsync(bookId, target.CfiRange, target.SelectedText, colorHex, note);
                    }
                }
                return;
            }

            _logger.LogInformation("[BookService] Actualizando anotación {AnnotationId} del libro {BookId}", annotationId, bookId);
            var payload = new { colorHex, note };
            var response = await _httpClient.PutAsJsonAsync($"api/Books/{bookId}/annotations/{annotationId}", payload);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BookService] Error actualizando anotación online. Guardando localmente.");
            var localRepo = GetLocalRepo();
            if (localRepo != null)
            {
                var anns = await localRepo.GetAnnotationsAsync(bookId);
                var target = anns.FirstOrDefault(a => a.Id == annotationId);
                if (target != null)
                {
                    target.ColorHex = colorHex;
                    target.Note = note;
                    await localRepo.SyncAnnotationsAsync(bookId, anns);
                }
            }
        }
    }

    public async Task DeleteAnnotationAsync(int bookId, int annotationId)
    {
        try
        {
            if (_networkState.IsOffline || annotationId <= 0)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null)
                {
                    var anns = await localRepo.GetAnnotationsAsync(bookId);
                    var target = anns.FirstOrDefault(a => a.Id == annotationId);
                    if (target != null)
                    {
                        anns.Remove(target);
                        await localRepo.SyncAnnotationsAsync(bookId, anns);
                    }
                    var queued = await localRepo.GetQueuedAnnotationsAsync();
                    var qMatch = queued.FirstOrDefault(q => q.BookId == bookId && q.CfiRange == target?.CfiRange);
                    if (qMatch != null)
                    {
                        await localRepo.RemoveQueuedAnnotationAsync(qMatch.Id);
                    }
                }
                return;
            }

            _logger.LogInformation("[BookService] Eliminando anotación {AnnotationId} del libro {BookId}", annotationId, bookId);
            var response = await _httpClient.DeleteAsync($"api/Books/{bookId}/annotations/{annotationId}");
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BookService] Error eliminando anotación online. Eliminando localmente.");
            var localRepo = GetLocalRepo();
            if (localRepo != null)
            {
                var anns = await localRepo.GetAnnotationsAsync(bookId);
                anns.RemoveAll(a => a.Id == annotationId);
                await localRepo.SyncAnnotationsAsync(bookId, anns);
            }
        }
    }

    public async Task UpdateMetadataAsync(int bookId, UpdateMetadataRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/Books/{bookId}/metadata", request);
        response.EnsureSuccessStatusCode();
        InvalidateCache();
    }

    public async Task UpdateCoverAsync(int bookId, string imageUrl)
    {
        var request = new UpdateCoverRequest { ImageUrl = imageUrl };
        var response = await _httpClient.PutAsJsonAsync($"api/Books/{bookId}/cover", request);
        response.EnsureSuccessStatusCode();
        InvalidateCache();
    }

    public async Task SaveEpubLocationsAsync(int bookId, string locationsJson)
    {
        if (_bookCache.TryGetValue(bookId, out var cached))
        {
            cached.EpubLocationsCache = locationsJson;
        }
        var localRepo = GetLocalRepo();
        if (localRepo != null)
        {
            try
            {
                var localBook = await localRepo.GetBookByIdAsync(bookId);
                if (localBook != null)
                {
                    localBook.EpubLocationsCache = locationsJson;
                    await localRepo.SaveBookAsync(localBook);
                }
            }
            catch { }
        }

        if (_networkState.IsOffline) return;

        var response = await _httpClient.PostAsJsonAsync($"api/Books/{bookId}/epub-locations", locationsJson);
        response.EnsureSuccessStatusCode();
    }
}
