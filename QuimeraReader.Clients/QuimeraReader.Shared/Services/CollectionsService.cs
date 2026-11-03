using System.Net.Http.Json;

namespace QuimeraReader.Shared.Services;

/// <summary>Sagas (series) y universos de la biblioteca.</summary>
public class CollectionsService
{
    private readonly HttpClient _httpClient;

    public CollectionsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<SeriesSummary>> GetSeriesAsync(string? search = null, int offset = 0, int limit = 60, bool withoutUniverse = false)
    {
        var url = $"api/series?offset={offset}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search.Trim())}";
        if (withoutUniverse) url += "&withoutUniverse=true";
        return await _httpClient.GetFromJsonAsync<List<SeriesSummary>>(url) ?? new();
    }

    public Task<SeriesDetail?> GetSeriesByIdAsync(int id) =>
        _httpClient.GetFromJsonAsync<SeriesDetail>($"api/series/{id}");

    public async Task AssignSeriesToUniverseAsync(int seriesId, int? universeId)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/series/{seriesId}/universe", new { UniverseId = universeId });
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<UniverseSummary>> GetUniversesAsync() =>
        await _httpClient.GetFromJsonAsync<List<UniverseSummary>>("api/universes") ?? new();

    public Task<UniverseDetail?> GetUniverseAsync(int id) =>
        _httpClient.GetFromJsonAsync<UniverseDetail>($"api/universes/{id}");

    public async Task<List<UniverseSuggestion>> GetSuggestionsAsync() =>
        await _httpClient.GetFromJsonAsync<List<UniverseSuggestion>>("api/universes/suggestions") ?? new();

    /// <returns>Id del universo creado.</returns>
    public async Task<int> CreateUniverseAsync(string name, IEnumerable<int>? seriesIds = null)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/universes", new { Name = name, SeriesIds = seriesIds?.ToList() ?? new List<int>() });
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict) throw new InvalidOperationException("Ya existe un universo con ese nombre.");
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedUniverse>();
        return created?.Id ?? 0;
    }

    public async Task UpdateUniverseAsync(int id, string name, string? description)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/universes/{id}", new { Name = name, Description = description });
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict) throw new InvalidOperationException("Ya existe un universo con ese nombre.");
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteUniverseAsync(int id)
    {
        using var response = await _httpClient.DeleteAsync($"api/universes/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task AddBookToUniverseAsync(int universeId, int bookId)
    {
        using var response = await _httpClient.PutAsync($"api/universes/{universeId}/books/{bookId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveBookFromUniverseAsync(int universeId, int bookId)
    {
        using var response = await _httpClient.DeleteAsync($"api/universes/{universeId}/books/{bookId}");
        response.EnsureSuccessStatusCode();
    }

    private sealed class CreatedUniverse
    {
        public int Id { get; set; }
    }
}
