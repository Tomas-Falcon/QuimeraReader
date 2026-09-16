using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using QuimeraReader.Shared.Models;
using System.Linq;

namespace QuimeraReader.Shared.Services;

public interface IQuimeraApiClient
{
    Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50);
    Task<Book?> GetBookAsync(int id);
    Task<string> GetSyncMapAsync(int id);
    Task<Dictionary<string, string>> GetSettingsAsync();
    Task SaveSettingAsync(SystemSetting setting);
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
        await _httpClient.PostAsJsonAsync("api/Settings", setting);
    }
}
