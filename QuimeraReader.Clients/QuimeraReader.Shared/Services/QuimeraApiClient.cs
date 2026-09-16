using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using QuimeraReader.Shared.Models;
using System.Linq;

namespace QuimeraReader.Shared.Services;

public class ScanStatus
{
    public bool IsScanning { get; set; }
    public int TotalFilesFound { get; set; }
    public int FilesProcessed { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
}

public interface IQuimeraApiClient
{
    Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50);
    Task<Book?> GetBookAsync(int id);
    Task<IEnumerable<Category>> GetCategoriesAsync();
    Task<IEnumerable<Author>> GetAuthorsAsync();
    Task<string> GetSyncMapAsync(int id);
    Task<Dictionary<string, string>> GetSettingsAsync();
    Task SaveSettingAsync(SystemSetting setting);
    Task<string> TriggerScanAsync(object payload);
    Task CancelScanAsync();
    Task DownloadWhisperModelAsync();
    Task<ScanStatus?> GetScanStatusAsync();
    Task<Book> UploadEpubAsync(Stream fileStream, string fileName);
    Task UpdatePositionAsync(int bookId, string? epubCfi = null, double? audioPosition = null, double? percentage = null);
    Task<List<Book>> GetRecommendationsAsync();
    Task RescanMetadataAsync();
}

public class QuimeraApiClient : IQuimeraApiClient
{
    private readonly HttpClient _httpClient;

    public QuimeraApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50)
    {
        // Esto reemplaza al listBooks de RTK Query y maneja la paginación que antes estaba en transformResponse
        var response = await _httpClient.GetFromJsonAsync<PaginatedResult<Book>>($"api/Books?page={page}&pageSize={pageSize}");
        return response ?? new PaginatedResult<Book> { Page = page, PageSize = pageSize, Total = 0, Data = [] };
    }

    public async Task<IEnumerable<Category>> GetCategoriesAsync()
    {
        return await _httpClient.GetFromJsonAsync<IEnumerable<Category>>("api/Books/categories") ?? Array.Empty<Category>();
    }

    public async Task<IEnumerable<Author>> GetAuthorsAsync()
    {
        return await _httpClient.GetFromJsonAsync<IEnumerable<Author>>("api/Books/authors") ?? Array.Empty<Author>();
    }

    public async Task<Book?> GetBookAsync(int id)
    {
        return await _httpClient.GetFromJsonAsync<Book>($"api/Books/{id}");
    }

    public async Task<string> GetSyncMapAsync(int id)
    {
        try 
        {
            return await _httpClient.GetStringAsync($"api/Books/{id}/syncmap");
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<Dictionary<string, string>> GetSettingsAsync()
    {
        var settings = await _httpClient.GetFromJsonAsync<Dictionary<string, string>>("api/Settings");
        return settings ?? new Dictionary<string, string>();
    }

    public async Task SaveSettingAsync(SystemSetting setting)
    {
        var response = await _httpClient.PostAsJsonAsync("api/Settings", setting);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> TriggerScanAsync(object payload)
    {
        var response = await _httpClient.PostAsJsonAsync("api/Books/scan", payload);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task CancelScanAsync()
    {
        var response = await _httpClient.PostAsync("api/Books/scan/cancel", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task DownloadWhisperModelAsync()
    {
        var response = await _httpClient.PostAsync("api/Settings/whisper/download", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ScanStatus?> GetScanStatusAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ScanStatus>("api/Books/scan/status");
        }
        catch
        {
            return null;
        }
    }

    public async Task<Book> UploadEpubAsync(Stream fileStream, string fileName)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        content.Add(streamContent, "file", fileName);

        var response = await _httpClient.PostAsync("api/Books/upload", content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Book>() ?? throw new InvalidOperationException("Respuesta inválida del servidor");
    }

    public async Task UpdatePositionAsync(int bookId, string? epubCfi = null, double? audioPosition = null, double? percentage = null)
    {
        try
        {
            var request = new { CurrentEpubCfi = epubCfi, CurrentAudioPosition = audioPosition, PercentageCompleted = percentage };
            await _httpClient.PostAsJsonAsync($"api/Books/{bookId}/positions", request);
        }
        catch { }
    }

    public async Task<List<Book>> GetRecommendationsAsync()
    {
        try
        {
            var results = await _httpClient.GetFromJsonAsync<List<Book>>("api/Books/recommendations");
            return results ?? new List<Book>();
        }
        catch
        {
            return new List<Book>();
        }
    }

    public async Task RescanMetadataAsync()
    {
        var response = await _httpClient.PostAsync("api/Books/scan/metadata", null);
        response.EnsureSuccessStatusCode();
    }
}
