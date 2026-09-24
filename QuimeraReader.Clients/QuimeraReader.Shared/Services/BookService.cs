using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using QuimeraReader.Shared.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using QuimeraReader.Shared.Models;

namespace QuimeraReader.Shared.Services;

public interface IBookService
{
    Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null, int? skip = null, int? take = null);
    Task<Book?> GetBookAsync(int id);
    Task<IEnumerable<Category>> GetCategoriesAsync();
    Task<IEnumerable<Author>> GetAuthorsAsync();
    Task<string> GetSyncMapAsync(int id);
    Task<Book> UploadEpubAsync(Stream fileStream, string fileName);
    Task UpdatePositionAsync(int bookId, string? epubCfi = null, double? audioPosition = null, double? percentage = null, int? audioTrackNumber = null);
    Task<List<Book>> GetRecommendationsAsync();
    Task DeleteEpubAsync(int bookId);
    Task DeleteBooksAsync(int[] ids);
    Task DeleteAudioAsync(int bookId);
        Task DeleteCategoriesAsync(int[] ids);
    Task DeleteAuthorsAsync(int[] ids);
    Task BulkUpdateStatusAsync(List<int> bookIds, string status);
    Task UpdateMetadataAsync(int bookId, UpdateMetadataRequest request);
    Task ToggleOfflineAvailabilityAsync(int bookId, bool isAvailable);
    Task CreateAnnotationAsync(int bookId, string cfiRange, string selectedText, string colorHex, string note);
    Task UpdateCoverAsync(int bookId, string imageUrl);
    Task SaveEpubLocationsAsync(int bookId, string locationsJson);
    string BaseAddress { get; }
}

public class BookService : IBookService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BookService> _logger;
    private readonly INetworkStateService _networkState;
    private readonly IServiceProvider _serviceProvider;

    public BookService(HttpClient httpClient, ILogger<BookService> logger, INetworkStateService networkState, IServiceProvider serviceProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        _networkState = networkState;
        _serviceProvider = serviceProvider;
    }

    private ILocalBookRepository? GetLocalRepo() => _serviceProvider.GetService<ILocalBookRepository>();


    public string BaseAddress => _httpClient.BaseAddress?.ToString() ?? "";

    public async Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null, int? skip = null, int? take = null)
    {
        try
        {
            _logger.LogInformation("[BookService] Obteniendo lista de libros. Page: {Page}, Search: {Search}", page, search);
            var url = $"api/Books?page={page}&pageSize={pageSize}";
            if (skip.HasValue) url += $"&skip={skip.Value}";
            if (take.HasValue) url += $"&take={take.Value}";
            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"&search={Uri.EscapeDataString(search)}";
            }
            if (categoryIds != null && categoryIds.Any())
            {
                foreach(var cid in categoryIds) url += $"&categoryIds={cid}";
            }
            if (!string.IsNullOrWhiteSpace(readingStatus)) url += $"&readingStatus={readingStatus}";
            var response = await _httpClient.GetFromJsonAsync<PaginatedResult<Book>>(url);
            return response ?? new PaginatedResult<Book> { Page = page, PageSize = pageSize, Total = 0, Data = [] };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error obteniendo lista de libros.");
            return new PaginatedResult<Book> { Page = page, PageSize = pageSize, Total = 0, Data = [] };
        }
    }

    public async Task<Book?> GetBookAsync(int id)
    {
        try
        {
            _logger.LogInformation("[BookService] Obteniendo detalles del libro ID: {BookId}", id);
            return await _httpClient.GetFromJsonAsync<Book>($"api/Books/{id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error obteniendo libro ID: {BookId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Category>> GetCategoriesAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<IEnumerable<Category>>("api/Books/categories") ?? Array.Empty<Category>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error obteniendo categorías.");
            return Array.Empty<Category>();
        }
    }

    public async Task<IEnumerable<Author>> GetAuthorsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<IEnumerable<Author>>("api/Books/authors") ?? Array.Empty<Author>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error obteniendo autores.");
            return Array.Empty<Author>();
        }
    }

    public async Task<string> GetSyncMapAsync(int id)
    {
        try 
        {
            _logger.LogInformation("[BookService] Descargando SyncMap para libro ID: {BookId}", id);
            return await _httpClient.GetStringAsync($"api/Books/{id}/syncmap");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BookService] SyncMap no encontrado o error para libro ID: {BookId}", id);
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
        try
        {
            if (_networkState.IsOffline)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null) 
                {
                    await localRepo.UpdateProgressAsync(bookId, epubCfi ?? "", audioPosition, percentage);
                    return;
                }
            }
            var request = new UpdatePositionRequest { CurrentEpubCfi = epubCfi, CurrentAudioPosition = audioPosition, PercentageCompleted = percentage, CurrentAudioTrackNumber = audioTrackNumber };
            var response = await _httpClient.PostAsJsonAsync($"api/Books/{bookId}/positions", request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        { 
            _logger.LogError(ex, "[BookService] Error actualizando posición para libro ID: {BookId}", bookId);
        }
    }

    public async Task<List<Book>> GetRecommendationsAsync()
    {
        try
        {
            var results = await _httpClient.GetFromJsonAsync<List<Book>>("api/Books/recommendations");
            return results ?? new List<Book>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BookService] Error obteniendo recomendaciones.");
            return new List<Book>();
        }
    }

        public async Task DeleteBooksAsync(int[] ids)
    {
        var queryString = string.Join("&", ids.Select(id => $"ids={id}"));
        var response = await _httpClient.DeleteAsync($"api/Books?{queryString}");
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteEpubAsync(int bookId)
    {
        try
        {
            _logger.LogInformation("[BookService] Solicitando eliminar EPUB del libro ID: {BookId}", bookId);
            var response = await _httpClient.DeleteAsync($"api/Books/{bookId}/epub");
            response.EnsureSuccessStatusCode();
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
    }

    public async Task DeleteAuthorsAsync(int[] ids)
    {
        var queryString = string.Join("&", ids.Select(id => $"ids={id}"));
        await _httpClient.DeleteAsync($"api/Books/authors?{queryString}");
    }

    public async Task DeleteAudioAsync(int bookId)
    {
        try
        {
            _logger.LogInformation("[BookService] Solicitando eliminar Audio del libro ID: {BookId}", bookId);
            var response = await _httpClient.DeleteAsync($"api/Books/{bookId}/audio");
            response.EnsureSuccessStatusCode();
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
    }

    public async Task ToggleOfflineAvailabilityAsync(int bookId, bool isAvailable)
    {
        var response = await _httpClient.PutAsync($"api/Books/{bookId}/offline?isAvailable={isAvailable}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task CreateAnnotationAsync(int bookId, string cfiRange, string selectedText, string colorHex, string note)
    {
        var payload = new { CfiRange = cfiRange, SelectedText = selectedText, ColorHex = colorHex, Note = note };
        var response = await _httpClient.PostAsJsonAsync($"api/Books/{bookId}/annotations", payload);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateMetadataAsync(int bookId, UpdateMetadataRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/Books/{bookId}/metadata", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateCoverAsync(int bookId, string imageUrl)
    {
        var request = new UpdateCoverRequest { ImageUrl = imageUrl };
        var response = await _httpClient.PutAsJsonAsync($"api/Books/{bookId}/cover", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task SaveEpubLocationsAsync(int bookId, string locationsJson)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/Books/{bookId}/epub-locations", locationsJson);
        response.EnsureSuccessStatusCode();
    }
}