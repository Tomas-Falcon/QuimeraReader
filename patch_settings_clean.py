import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Just cleanly format the whole file instead of replacing blindly
    content = '''using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using QuimeraReader.Shared.Models;

namespace QuimeraReader.Shared.Services;

public interface ISettingsService
{
    Task<Dictionary<string, string>> GetSettingsAsync();
    Task SaveSettingAsync(SystemSetting setting);
    Task DownloadWhisperModelAsync();
    Task<bool> CheckWhisperModelStatusAsync();
    Task RestartServerAsync();
    Task UpdateContainerAsync();
}

public class SettingsService : ISettingsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(HttpClient httpClient, ILogger<SettingsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> GetSettingsAsync()
    {
        try
        {
            _logger.LogInformation("[SettingsService] Obteniendo configuraciones del servidor.");
            var settings = await _httpClient.GetFromJsonAsync<Dictionary<string, string>>("api/Settings");
            return settings ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsService] Error obteniendo configuraciones.");
            return new Dictionary<string, string>();
        }
    }

    public async Task SaveSettingAsync(SystemSetting setting)
    {
        try
        {
            _logger.LogInformation("[SettingsService] Guardando configuración: {Key}", setting.Key);
            var response = await _httpClient.PostAsJsonAsync("api/Settings", setting);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsService] Error guardando configuración: {Key}", setting.Key);
            throw;
        }
    }

    public async Task DownloadWhisperModelAsync()
    {
        try
        {
            _logger.LogInformation("[SettingsService] Solicitando descarga del modelo Whisper.");
            var response = await _httpClient.PostAsync("api/Settings/whisper/download", null);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsService] Error solicitando descarga del modelo Whisper.");
            throw;
        }
    }

    public async Task<bool> CheckWhisperModelStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/Settings/whisper/status");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
                return result?["isDownloaded"]?.GetValue<bool>() ?? false;
            }
        }
        catch { }
        return false;
    }

    public async Task RestartServerAsync()
    {
        try
        {
            _logger.LogInformation("[SettingsService] Solicitando reinicio del servidor.");
            await _httpClient.PostAsync("api/settings/restart", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsService] Error al intentar reiniciar el servidor.");
            throw;
        }
    }
    
    public async Task UpdateContainerAsync()
    {
        try
        {
            _logger.LogInformation("[SettingsService] Solicitando actualización y reinicio del contenedor.");
            await _httpClient.PostAsync("api/settings/update-container", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsService] Error al intentar actualizar el contenedor.");
            throw;
        }
    }
}
'''
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Services/SettingsService.cs')