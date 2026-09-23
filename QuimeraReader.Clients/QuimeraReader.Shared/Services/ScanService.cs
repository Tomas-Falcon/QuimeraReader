using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using QuimeraReader.Shared.Models;

namespace QuimeraReader.Shared.Services;

public interface IScanService
{
    Task<string> TriggerScanAsync(object payload);
    Task CancelScanAsync();
    Task<ScanStatus?> GetScanStatusAsync();
    Task RescanMetadataAsync();
    Task RescanSingleBookMetadataAsync(int bookId);
    Task MergeDuplicatesAsync();
    Task TriggerDeepScanAsync();
}

public class ScanService : IScanService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ScanService> _logger;

    public ScanService(HttpClient httpClient, ILogger<ScanService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> TriggerScanAsync(object payload)
    {
        try
        {
            _logger.LogInformation("[ScanService] Iniciando escaneo manual de librería.");
            var response = await _httpClient.PostAsJsonAsync("api/Books/scan", payload);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScanService] Error al iniciar el escaneo manual.");
            throw;
        }
    }

    public async Task CancelScanAsync()
    {
        try
        {
            _logger.LogInformation("[ScanService] Cancelando escaneo activo.");
            var response = await _httpClient.PostAsync("api/Books/scan/cancel", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScanService] Error al cancelar el escaneo.");
            throw;
        }
    }

    public async Task<ScanStatus?> GetScanStatusAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ScanStatus>("api/Books/scan/status");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ScanService] No se pudo obtener el estado del escaneo (puede no haber escaneo activo).");
            return null;
        }
    }

    public async Task RescanMetadataAsync()
    {
        try
        {
            _logger.LogInformation("[ScanService] Solicitando escaneo de metadatos general.");
            var response = await _httpClient.PostAsync("api/Books/scan/metadata", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScanService] Error al solicitar escaneo de metadatos general.");
            throw;
        }
    }

    public async Task RescanSingleBookMetadataAsync(int bookId)
    {
        try
        {
            _logger.LogInformation("[ScanService] Solicitando rescaneo de metadatos para el libro ID: {BookId}", bookId);
            var response = await _httpClient.PostAsync($"api/Books/{bookId}/scan/metadata", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScanService] Error al solicitar rescaneo de metadatos para el libro ID: {BookId}", bookId);
            throw;
        }
    }

    public async Task MergeDuplicatesAsync()
    {
        try
        {
            _logger.LogInformation("[ScanService] Solicitando fusión de autores duplicados (Mantenimiento).");
            var response = await _httpClient.PostAsync("api/Books/maintenance/merge-duplicates", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScanService] Error al fusionar autores duplicados.");
            throw;
        }
    }

    public async Task TriggerDeepScanAsync()
    {
        try
        {
            _logger.LogInformation("[ScanService] Solicitando Deep Scan (Búsqueda profunda).");
            await _httpClient.PostAsync("api/books/scan/deep", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScanService] Error al solicitar Deep Scan.");
            throw;
        }
    }
}
